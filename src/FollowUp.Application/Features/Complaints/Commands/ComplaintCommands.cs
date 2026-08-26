using FollowUp.Application.Common.Abstractions;
using FollowUp.Application.Common.Abstractions.Persistence;
using FollowUp.Application.Common.Exceptions;
using FollowUp.Application.Common.Messaging;
using FollowUp.Application.Common.Security;
using FollowUp.Domain.Common;
using FollowUp.Domain.Complaints;
using FollowUp.Domain.Identity;
using FollowUp.Domain.Laboratories;
using FluentValidation;
using MediatR;

namespace FollowUp.Application.Features.Complaints.Commands;

internal static class ComplaintActionSupport
{
    public const string Module = "complaint";

    public static async Task<Complaint> LoadAuthorizedAsync(Guid id, IComplaintRepository complaints,
        ILaboratoryRepository labs, ICurrentUser user, CancellationToken ct)
    {
        var complaint = await complaints.GetByIdAsync(new ComplaintId(id), ct)
            ?? throw new NotFoundException("Complaint", id);
        var lab = await labs.GetByIdAsync(complaint.LaboratoryId, ct)
            ?? throw new NotFoundException("Laboratory", complaint.LaboratoryId.Value);
        user.EnsureInScope(lab);
        return complaint;
    }
}

// ---- Log (create) ----

public sealed record LogComplaintCommand : ICommand<string>, IAuthorizedRequest
{
    public Guid LaboratoryId { get; init; }
    public string Category { get; init; } = string.Empty;
    public string ViaChannel { get; init; } = string.Empty;
    public string? AssignedTeam { get; init; }
    public string Details { get; init; } = string.Empty;
    public Guid? RepresentativeId { get; init; }
    public DateTimeOffset? ReceivedAt { get; init; }

    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.AddComplaints, Privileges.ManageComplaints };
}

public sealed class LogComplaintValidator : AbstractValidator<LogComplaintCommand>
{
    public LogComplaintValidator()
    {
        RuleFor(x => x.LaboratoryId).NotEmpty();
        RuleFor(x => x.Category).NotEmpty();
        RuleFor(x => x.ViaChannel).NotEmpty();
    }
}

public sealed class LogComplaintHandler : ICommandHandler<LogComplaintCommand, string>
{
    private readonly IComplaintRepository _complaints;
    private readonly ILaboratoryRepository _labs;
    private readonly ICurrentUser _user;

    public LogComplaintHandler(IComplaintRepository complaints, ILaboratoryRepository labs, ICurrentUser user)
    {
        _complaints = complaints; _labs = labs; _user = user;
    }

    public async Task<string> Handle(LogComplaintCommand request, CancellationToken ct)
    {
        var lab = await _labs.GetByIdAsync(new LaboratoryId(request.LaboratoryId), ct)
            ?? throw new NotFoundException("Laboratory", request.LaboratoryId);
        _user.EnsureInScope(lab);

        var number = await _complaints.NextNumberAsync(ct);
        var complaint = Complaint.Log(number, lab.Id, request.Category, request.ViaChannel,
            request.AssignedTeam, request.Details);
        complaint.SetIntake(request.RepresentativeId, request.ReceivedAt);

        _complaints.Add(complaint);
        return complaint.Reference; // e.g. CMP-42
    }
}

// ---- Start (Open -> InProgress) ----

public sealed record StartComplaintCommand(Guid Id) : ICommand, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.UpdateComplaints, Privileges.ManageComplaints };
}

public sealed class StartComplaintHandler : ICommandHandler<StartComplaintCommand>
{
    private readonly IComplaintRepository _complaints;
    private readonly ILaboratoryRepository _labs;
    private readonly ICurrentUser _user;

    public StartComplaintHandler(IComplaintRepository complaints, ILaboratoryRepository labs, ICurrentUser user)
    {
        _complaints = complaints; _labs = labs; _user = user;
    }

    public async Task<Unit> Handle(StartComplaintCommand request, CancellationToken ct)
    {
        var complaint = await ComplaintActionSupport.LoadAuthorizedAsync(request.Id, _complaints, _labs, _user, ct);
        complaint.Start();
        return Unit.Value;
    }
}

// ---- Resolve (-> Resolved; e-sign gate) ----

public sealed record ResolveComplaintCommand(Guid Id, string? ResolutionSummary = null) : ICommand, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ResolveComplaints, Privileges.ManageComplaints };
}

public sealed class ResolveComplaintHandler : ICommandHandler<ResolveComplaintCommand>
{
    private readonly IComplaintRepository _complaints;
    private readonly ILaboratoryRepository _labs;
    private readonly ICurrentUser _user;
    private readonly IClock _clock;
    private readonly IElectronicSignatureGate _signatureGate;

    public ResolveComplaintHandler(IComplaintRepository complaints, ILaboratoryRepository labs, ICurrentUser user,
        IClock clock, IElectronicSignatureGate signatureGate)
    {
        _complaints = complaints; _labs = labs; _user = user; _clock = clock; _signatureGate = signatureGate;
    }

    public async Task<Unit> Handle(ResolveComplaintCommand request, CancellationToken ct)
    {
        var complaint = await ComplaintActionSupport.LoadAuthorizedAsync(request.Id, _complaints, _labs, _user, ct);

        // Only the single state machine changes status (CMP-STAGE fix); the e-sign gate is checked here.
        var enforced = await _signatureGate.IsEnforcedAsync(ComplaintActionSupport.Module, ct);
        var satisfied = !enforced ||
            await _signatureGate.HasValidSignatureAsync(ComplaintActionSupport.Module, complaint.Id.ToString(), ct);

        complaint.SetResolutionSummary(request.ResolutionSummary);
        complaint.Resolve(_user.Username, _clock.UtcNow, satisfied);
        return Unit.Value;
    }
}

// ---- Reopen (-> Open) ----

public sealed record ReopenComplaintCommand(Guid Id) : ICommand, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.UpdateComplaints, Privileges.ManageComplaints };
}

public sealed class ReopenComplaintHandler : ICommandHandler<ReopenComplaintCommand>
{
    private readonly IComplaintRepository _complaints;
    private readonly ILaboratoryRepository _labs;
    private readonly ICurrentUser _user;

    public ReopenComplaintHandler(IComplaintRepository complaints, ILaboratoryRepository labs, ICurrentUser user)
    {
        _complaints = complaints; _labs = labs; _user = user;
    }

    public async Task<Unit> Handle(ReopenComplaintCommand request, CancellationToken ct)
    {
        var complaint = await ComplaintActionSupport.LoadAuthorizedAsync(request.Id, _complaints, _labs, _user, ct);
        complaint.Reopen();
        return Unit.Value;
    }
}

// ---- Move stage (metadata only; never changes status) ----

public sealed record MoveComplaintStageCommand(Guid Id, string Stage) : ICommand, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.UpdateComplaints, Privileges.ManageComplaints };
}

public sealed class MoveComplaintStageHandler : ICommandHandler<MoveComplaintStageCommand>
{
    private readonly IComplaintRepository _complaints;
    private readonly ILaboratoryRepository _labs;
    private readonly ICurrentUser _user;

    public MoveComplaintStageHandler(IComplaintRepository complaints, ILaboratoryRepository labs, ICurrentUser user)
    {
        _complaints = complaints; _labs = labs; _user = user;
    }

    public async Task<Unit> Handle(MoveComplaintStageCommand request, CancellationToken ct)
    {
        var complaint = await ComplaintActionSupport.LoadAuthorizedAsync(request.Id, _complaints, _labs, _user, ct);
        complaint.MoveToStage(Enumeration.FromName<ComplaintStage>(request.Stage));
        return Unit.Value;
    }
}

// ---- Advance stage with payload (reference parity: validity / investigation / outcome narratives) ----

/// <summary>Advances the staged-investigation narrative with the stage's payload (SRS FR-11).
/// Validity: IsValid + Notes; Investigation: Notes; BusinessOutcome: OutcomeType + Summary.</summary>
public sealed record AdvanceComplaintStageCommand(
    Guid Id, string Stage, string? Notes = null, bool? IsValid = null,
    string? OutcomeType = null, string? Summary = null) : ICommand, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.UpdateComplaints, Privileges.ManageComplaints };
}

public sealed class AdvanceComplaintStageValidator : AbstractValidator<AdvanceComplaintStageCommand>
{
    public AdvanceComplaintStageValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Stage).NotEmpty();
        RuleFor(x => x.IsValid).NotNull().When(x => x.Stage == "ValidityChecked")
            .WithMessage("The validity check requires a valid/invalid decision.");
        RuleFor(x => x.Notes).NotEmpty().When(x => x.Stage == "Investigation")
            .WithMessage("Investigation notes are required.");
        RuleFor(x => x.OutcomeType).NotEmpty().When(x => x.Stage == "BusinessOutcome")
            .WithMessage("An outcome type is required.");
    }
}

public sealed class AdvanceComplaintStageHandler : ICommandHandler<AdvanceComplaintStageCommand>
{
    private readonly IComplaintRepository _complaints;
    private readonly ILaboratoryRepository _labs;
    private readonly ICurrentUser _user;

    public AdvanceComplaintStageHandler(IComplaintRepository complaints, ILaboratoryRepository labs, ICurrentUser user)
    {
        _complaints = complaints; _labs = labs; _user = user;
    }

    public async Task<Unit> Handle(AdvanceComplaintStageCommand request, CancellationToken ct)
    {
        var complaint = await ComplaintActionSupport.LoadAuthorizedAsync(request.Id, _complaints, _labs, _user, ct);
        switch (request.Stage)
        {
            case "ValidityChecked":
                complaint.CheckValidity(request.IsValid!.Value, request.Notes);
                break;
            case "Investigation":
                complaint.RecordInvestigation(request.Notes!);
                break;
            case "BusinessOutcome":
                complaint.RecordOutcome(request.OutcomeType!, request.Summary);
                break;
            default:
                complaint.MoveToStage(Enumeration.FromName<ComplaintStage>(request.Stage));
                break;
        }
        return Unit.Value;
    }
}
