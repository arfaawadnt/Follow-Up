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

public sealed record ResolveComplaintCommand(Guid Id) : ICommand, IAuthorizedRequest
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

        complaint.Resolve(_user.Username, _clock.CairoNow, satisfied);
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
