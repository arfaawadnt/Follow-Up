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
    private readonly ICurrentUser _user;
    private readonly IClock _clock;

    public CheckInVisitHandler(IDailyVisitRepository visits, ILaboratoryRepository labs,
        IOutsourceSampleRepository outsource, ICurrentUser user, IClock clock)
    {
        _visits = visits; _labs = labs; _outsource = outsource; _user = user; _clock = clock;
    }

    public async Task<Unit> Handle(CheckInVisitCommand request, CancellationToken ct)
    {
        var (visit, lab) = await VisitActionSupport.LoadAuthorizedAsync(request.VisitId, _visits, _labs, _user, ct);

        if (request.CollectorRepId is { } repId)
            visit.ReassignCollector(new Domain.Representatives.RepresentativeId(repId));

        visit.CheckIn(request.SampleCount, _user.Username, _clock.UtcNow,
            request.TotalRequired, request.RequestCount, request.OutsourceCount, request.Notes);
        lab.DeriveActiveFromActivity(); // BR-5

        // FR-9: a check-in with an outsource count auto-creates the outsource row (unique per lab+date).
        if (request.OutsourceCount is > 0 && !await _outsource.ExistsForAsync(visit.LaboratoryId, visit.VisitDate, ct))
            _outsource.Add(Domain.Operations.OutsourceSample.Create(
                visit.LaboratoryId, visit.VisitDate, null, request.OutsourceCount.Value, visit.Notes));

        return Unit.Value;
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
