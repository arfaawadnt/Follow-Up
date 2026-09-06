using FollowUp.Application.Common.Abstractions;
using FollowUp.Application.Common.Abstractions.Persistence;
using FollowUp.Application.Common.Exceptions;
using FollowUp.Application.Common.Messaging;
using FollowUp.Application.Common.Security;
using FollowUp.Domain.Identity;
using FollowUp.Domain.Laboratories;
using FollowUp.Domain.Operations;
using FluentValidation;
using MediatR;

namespace FollowUp.Application.Features.DailyBoard.Commands;

/// <summary>Shared loading + Layer-3 authorization for a board action on a single visit.</summary>
internal static class VisitActionSupport
{
    public static async Task<(DailyVisit visit, Laboratory lab)> LoadAuthorizedAsync(
        Guid visitId, IDailyVisitRepository visits, ILaboratoryRepository labs, ICurrentUser user, CancellationToken ct)
    {
        var visit = await visits.GetByIdAsync(new DailyVisitId(visitId), ct)
            ?? throw new NotFoundException("Visit", visitId);
        var lab = await labs.GetByIdAsync(visit.LaboratoryId, ct)
            ?? throw new NotFoundException("Laboratory", visit.LaboratoryId.Value);

        user.EnsureInScope(lab);
        user.EnsureOwnedIfRepLinked(visit.CollectorRepId);
        return (visit, lab);
    }

    /// <summary>Binds the pending (just-uploaded) attachments to the visit they were recorded with.</summary>
    public static async Task BindAttachmentsAsync(IReadOnlyCollection<Guid> attachmentIds, Guid visitId,
        LaboratoryId labId, IVisitAttachmentRepository attachments, CancellationToken ct)
    {
        if (attachmentIds is null || attachmentIds.Count == 0) return;
        var ids = attachmentIds.Distinct().Select(g => new VisitAttachmentId(g)).ToList();
        foreach (var a in await attachments.GetByIdsAsync(ids, ct))
            a.BindTo(visitId, labId);
    }
}

// ---- Check-in (Pending -> Visited) ----

/// <summary>Collector checks in a visit via the record-visit popup (SRS FR-5): sample count plus the
/// reference-parity extras — collector override, totals, outsource count (FR-9 auto-create) and notes.</summary>
public sealed record CheckInVisitCommand(Guid VisitId, int SampleCount) : ICommand, IAuthorizedRequest
{
    public Guid? CollectorRepId { get; init; }
    public int? TotalRequired { get; init; }
    public int? RequestCount { get; init; }
    public int? OutsourceCount { get; init; }
    public string? Notes { get; init; }
    /// <summary>Optional documents uploaded with this record (pending attachment ids to bind to the visit).</summary>
    public IReadOnlyList<Guid> AttachmentIds { get; init; } = Array.Empty<Guid>();

    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.AddDailyFollowup };
}

public sealed class CheckInVisitValidator : AbstractValidator<CheckInVisitCommand>
{
    public CheckInVisitValidator()
    {
        RuleFor(x => x.VisitId).NotEmpty();
        RuleFor(x => x.SampleCount).GreaterThanOrEqualTo(0);
        RuleFor(x => x.TotalRequired).GreaterThanOrEqualTo(0).When(x => x.TotalRequired.HasValue);
        RuleFor(x => x.RequestCount).GreaterThanOrEqualTo(0).When(x => x.RequestCount.HasValue);
        RuleFor(x => x.OutsourceCount).GreaterThanOrEqualTo(0).When(x => x.OutsourceCount.HasValue);
    }
}

public sealed class CheckInVisitHandler : ICommandHandler<CheckInVisitCommand>
{
    private readonly IDailyVisitRepository _visits;
    private readonly ILaboratoryRepository _labs;
    private readonly IOutsourceSampleRepository _outsource;
    private readonly IRepresentativeRepository _reps;
    private readonly IVisitAttachmentRepository _attachments;
    private readonly ICurrentUser _user;
    private readonly IClock _clock;

    public CheckInVisitHandler(IDailyVisitRepository visits, ILaboratoryRepository labs,
        IOutsourceSampleRepository outsource, IRepresentativeRepository reps, IVisitAttachmentRepository attachments,
        ICurrentUser user, IClock clock)
    {
        _visits = visits; _labs = labs; _outsource = outsource; _reps = reps; _attachments = attachments; _user = user; _clock = clock;
    }

    public async Task<Unit> Handle(CheckInVisitCommand request, CancellationToken ct)
    {
        var (visit, lab) = await VisitActionSupport.LoadAuthorizedAsync(request.VisitId, _visits, _labs, _user, ct);

        // A reassigned collector must exist — mirror ConfirmTransferHandler's rep check so a crafted check-in
        // can't credit samples/commission to a non-existent rep (which otherwise fails late at the Restrict FK
        // as an unmapped 500) (finding BRD-7).
        if (request.CollectorRepId is { } repId)
        {
            var collectorRepId = new Domain.Representatives.RepresentativeId(repId);
            if (!await _reps.ExistsAsync(collectorRepId, ct))
                throw new NotFoundException("Representative", repId);
            visit.ReassignCollector(collectorRepId);
        }

        visit.CheckIn(request.SampleCount, _user.Username, _clock.UtcNow,
            request.TotalRequired, request.RequestCount, request.OutsourceCount, request.Notes);
        lab.DeriveActiveFromActivity(); // BR-5

        // FR-9: a check-in with an outsource count auto-creates the outsource row (unique per lab+date).
        if (request.OutsourceCount is > 0 && !await _outsource.ExistsForAsync(visit.LaboratoryId, visit.VisitDate, ct))
            _outsource.Add(Domain.Operations.OutsourceSample.Create(
                visit.LaboratoryId, visit.VisitDate, null, request.OutsourceCount.Value, visit.Notes));

        await VisitActionSupport.BindAttachmentsAsync(request.AttachmentIds, visit.Id.Value, lab.Id, _attachments, ct);
        return Unit.Value;
    }
}

// ---- Manual record (create an ad-hoc Collected visit for ANY lab, regardless of status) ----

/// <summary>
/// Records a visit that isn't on today's generated board: the operator picks any lab (any status — the scheduler's
/// Schedulable gate is bypassed here) and records it as Collected for today, with the same extras and optional
/// document attachments as a normal check-in (SRS FR-5 manual entry).
/// </summary>
public sealed record RecordManualVisitCommand(Guid LaboratoryId, int SampleCount) : ICommand<Guid>, IAuthorizedRequest
{
    public Guid? CollectorRepId { get; init; }
    public int? TotalRequired { get; init; }
    public int? RequestCount { get; init; }
    public int? OutsourceCount { get; init; }
    public string? Notes { get; init; }
    public IReadOnlyList<Guid> AttachmentIds { get; init; } = Array.Empty<Guid>();

    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.AddDailyFollowup };
}

public sealed class RecordManualVisitValidator : AbstractValidator<RecordManualVisitCommand>
{
    public RecordManualVisitValidator()
    {
        RuleFor(x => x.LaboratoryId).NotEmpty();
        RuleFor(x => x.SampleCount).GreaterThanOrEqualTo(0);
        RuleFor(x => x.TotalRequired).GreaterThanOrEqualTo(0).When(x => x.TotalRequired.HasValue);
        RuleFor(x => x.RequestCount).GreaterThanOrEqualTo(0).When(x => x.RequestCount.HasValue);
        RuleFor(x => x.OutsourceCount).GreaterThanOrEqualTo(0).When(x => x.OutsourceCount.HasValue);
    }
}

public sealed class RecordManualVisitHandler : ICommandHandler<RecordManualVisitCommand, Guid>
{
    private readonly IDailyVisitRepository _visits;
    private readonly ILaboratoryRepository _labs;
    private readonly IOutsourceSampleRepository _outsource;
    private readonly IRepresentativeRepository _reps;
    private readonly IVisitAttachmentRepository _attachments;
    private readonly ICurrentUser _user;
    private readonly IClock _clock;

    public RecordManualVisitHandler(IDailyVisitRepository visits, ILaboratoryRepository labs,
        IOutsourceSampleRepository outsource, IRepresentativeRepository reps, IVisitAttachmentRepository attachments,
        ICurrentUser user, IClock clock)
    {
        _visits = visits; _labs = labs; _outsource = outsource; _reps = reps; _attachments = attachments; _user = user; _clock = clock;
    }

    public async Task<Guid> Handle(RecordManualVisitCommand request, CancellationToken ct)
    {
        var lab = await _labs.GetByIdAsync(new LaboratoryId(request.LaboratoryId), ct)
            ?? throw new NotFoundException("Laboratory", request.LaboratoryId);
        _user.EnsureInScope(lab); // may only record for a lab in the caller's org-scope

        Domain.Representatives.RepresentativeId? collector = null;
        if (request.CollectorRepId is { } repId)
        {
            collector = new Domain.Representatives.RepresentativeId(repId);
            if (!await _reps.ExistsAsync(collector.Value, ct))
                throw new NotFoundException("Representative", repId);
        }

        var today = _clock.CairoToday;
        var now = TimeOnly.FromTimeSpan(_clock.CairoNow.TimeOfDay); // distinct slot per record (second precision)
        var visit = DailyVisit.Schedule(lab.Id, collector, today, now);
        visit.CheckIn(request.SampleCount, _user.Username, _clock.UtcNow,
            request.TotalRequired, request.RequestCount, request.OutsourceCount, request.Notes);
        lab.DeriveActiveFromActivity(); // BR-5
        _visits.Add(visit);

        if (request.OutsourceCount is > 0 && !await _outsource.ExistsForAsync(lab.Id, today, ct))
            _outsource.Add(Domain.Operations.OutsourceSample.Create(lab.Id, today, null, request.OutsourceCount.Value, request.Notes));

        await VisitActionSupport.BindAttachmentsAsync(request.AttachmentIds, visit.Id.Value, lab.Id, _attachments, ct);
        return visit.Id.Value;
    }
}

// ---- Miss (Pending -> Missed) ----

public sealed record MissVisitCommand(Guid VisitId) : ICommand, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.UpdateDailyFollowup };
}

public sealed class MissVisitHandler : ICommandHandler<MissVisitCommand>
{
    private readonly IDailyVisitRepository _visits;
    private readonly ILaboratoryRepository _labs;
    private readonly ICurrentUser _user;

    public MissVisitHandler(IDailyVisitRepository visits, ILaboratoryRepository labs, ICurrentUser user)
    {
        _visits = visits; _labs = labs; _user = user;
    }

    public async Task<Unit> Handle(MissVisitCommand request, CancellationToken ct)
    {
        var (visit, _) = await VisitActionSupport.LoadAuthorizedAsync(request.VisitId, _visits, _labs, _user, ct);
        visit.Miss();
        return Unit.Value;
    }
}

// ---- Undo (Visited -> Pending; refused once transferred) ----

public sealed record UndoVisitCommand(Guid VisitId) : ICommand, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.UpdateDailyFollowup };
}

public sealed class UndoVisitHandler : ICommandHandler<UndoVisitCommand>
{
    private readonly IDailyVisitRepository _visits;
    private readonly ILaboratoryRepository _labs;
    private readonly ICurrentUser _user;

    public UndoVisitHandler(IDailyVisitRepository visits, ILaboratoryRepository labs, ICurrentUser user)
    {
        _visits = visits; _labs = labs; _user = user;
    }

    public async Task<Unit> Handle(UndoVisitCommand request, CancellationToken ct)
    {
        var (visit, _) = await VisitActionSupport.LoadAuthorizedAsync(request.VisitId, _visits, _labs, _user, ct);
        visit.Undo(); // throws DomainException (->400) if already transferred
        return Unit.Value;
    }
}

// ---- Verify (elevated admin-checked toggle) ----

public sealed record VerifyVisitCommand(Guid VisitId, bool Verified) : ICommand, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.VerifyDailyFollowup };
}

public sealed class VerifyVisitHandler : ICommandHandler<VerifyVisitCommand>
{
    private readonly IDailyVisitRepository _visits;
    private readonly ILaboratoryRepository _labs;
    private readonly ICurrentUser _user;

    public VerifyVisitHandler(IDailyVisitRepository visits, ILaboratoryRepository labs, ICurrentUser user)
    {
        _visits = visits; _labs = labs; _user = user;
    }

    public async Task<Unit> Handle(VerifyVisitCommand request, CancellationToken ct)
    {
        var (visit, _) = await VisitActionSupport.LoadAuthorizedAsync(request.VisitId, _visits, _labs, _user, ct);
        visit.SetVerified(request.Verified);
        return Unit.Value;
    }
}
