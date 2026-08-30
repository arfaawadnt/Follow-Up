using FollowUp.Application.Common.Abstractions;
using FollowUp.Application.Common.Abstractions.Persistence;
using FollowUp.Application.Common.Exceptions;
using FollowUp.Application.Common.Messaging;
using FollowUp.Application.Common.Security;
using FollowUp.Domain.Common;
using FollowUp.Domain.Complaints;
using FollowUp.Domain.Identity;
using FollowUp.Domain.Laboratories;
using FollowUp.Domain.Representatives;
using FollowUp.Domain.Signatures;
using FluentValidation;
using MediatR;

namespace FollowUp.Application.Features.Complaints.Commands;

internal static class ComplaintActionSupport
{
    public const string Module = SignableModule.Complaint; // single authority for the signable-module name (SIG-10)

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

/// <summary>The created complaint's identity (CMP-13): both the id (for the resource URI) and the CMP-n reference.</summary>
public sealed record LogComplaintResult(Guid Id, string Reference);

public sealed record LogComplaintCommand : ICommand<LogComplaintResult>, IAuthorizedRequest
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
        // Mirror the schema's varchar bounds so over-length input is a 400, not a DbUpdateException 22001 → 500
        // (CMP-19). Details/InvestigationNotes are `text` and need no bound.
        RuleFor(x => x.Category).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ViaChannel).NotEmpty().MaximumLength(100);
        RuleFor(x => x.AssignedTeam).MaximumLength(100);
        RuleFor(x => x.ReceivedAt).LessThanOrEqualTo(_ => DateTimeOffset.UtcNow)
            .When(x => x.ReceivedAt.HasValue).WithMessage("The received date cannot be in the future.");
    }
}

public sealed class LogComplaintHandler : ICommandHandler<LogComplaintCommand, LogComplaintResult>
{
    private readonly IComplaintRepository _complaints;
    private readonly ILaboratoryRepository _labs;
    private readonly IRepresentativeRepository _reps;
    private readonly ICurrentUser _user;

    public LogComplaintHandler(IComplaintRepository complaints, ILaboratoryRepository labs,
        IRepresentativeRepository reps, ICurrentUser user)
    {
        _complaints = complaints; _labs = labs; _reps = reps; _user = user;
    }

    public async Task<LogComplaintResult> Handle(LogComplaintCommand request, CancellationToken ct)
    {
        var lab = await _labs.GetByIdAsync(new LaboratoryId(request.LaboratoryId), ct)
            ?? throw new NotFoundException("Laboratory", request.LaboratoryId);
        _user.EnsureInScope(lab);

        // The intake representative is an unconstrained Guid (CMP-12); reject an unknown id here with a clean 404
        // rather than letting a dangling reference persist (the FK is the matching database second line).
        if (request.RepresentativeId is { } repId && !await _reps.ExistsAsync(new RepresentativeId(repId), ct))
            throw new NotFoundException("Representative", repId);

        var number = await _complaints.NextNumberAsync(ct);
        var complaint = Complaint.Log(number, lab.Id, request.Category, request.ViaChannel,
            request.AssignedTeam, request.Details);
        complaint.SetIntake(request.RepresentativeId, request.ReceivedAt);

        _complaints.Add(complaint);
        return new LogComplaintResult(complaint.Id.Value, complaint.Reference); // id for the resource URI + CMP-42
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

// The metadata-only "move stage" command and its POST /complaints/{id}/stage route are retired (CMP-5): they
// duplicated the advance operation below, which survives. The route now returns 410 Gone. The stage still
// stays a captured narrative — status only ever changes through the guarded machine (CMP-2).

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
        // Stage names bound to the enumeration (CMP-14): renaming a ComplaintStage member breaks compilation
        // here instead of silently detaching these conditional rules from the switch below.
        RuleFor(x => x.IsValid).NotNull().When(x => x.Stage == ComplaintStage.ValidityChecked.Name)
            .WithMessage("The validity check requires a valid/invalid decision.");
        RuleFor(x => x.Notes).NotEmpty().When(x => x.Stage == ComplaintStage.Investigation.Name)
            .WithMessage("Investigation notes are required.");
        RuleFor(x => x.OutcomeType).NotEmpty().When(x => x.Stage == ComplaintStage.BusinessOutcome.Name)
            .WithMessage("An outcome type is required.");
        // Schema bounds (CMP-19): OutcomeType varchar(100); Notes/Summary land in varchar(2000) narrative fields.
        RuleFor(x => x.OutcomeType).MaximumLength(100);
        RuleFor(x => x.Notes).MaximumLength(2000);
        RuleFor(x => x.Summary).MaximumLength(2000);
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
        // Route on the enumeration's names (CMP-14) rather than repeated string literals; the default resolves and
        // validates any other stage via FromName (throws on an unknown name), exactly as before.
        if (request.Stage == ComplaintStage.ValidityChecked.Name)
            complaint.CheckValidity(request.IsValid!.Value, request.Notes);
        else if (request.Stage == ComplaintStage.Investigation.Name)
            complaint.RecordInvestigation(request.Notes!);
        else if (request.Stage == ComplaintStage.BusinessOutcome.Name)
            complaint.RecordOutcome(request.OutcomeType!, request.Summary);
        else
            complaint.MoveToStage(Enumeration.FromName<ComplaintStage>(request.Stage));
        return Unit.Value;
    }
}
