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

/// <summary>Collector checks in a visit and records its sample count (SRS FR-5).</summary>
public sealed record CheckInVisitCommand(Guid VisitId, int SampleCount) : ICommand, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.AddDailyFollowup };
}

public sealed class CheckInVisitValidator : AbstractValidator<CheckInVisitCommand>
{
    public CheckInVisitValidator()
    {
        RuleFor(x => x.VisitId).NotEmpty();
        RuleFor(x => x.SampleCount).GreaterThanOrEqualTo(0);
    }
}

public sealed class CheckInVisitHandler : ICommandHandler<CheckInVisitCommand>
{
    private readonly IDailyVisitRepository _visits;
    private readonly ILaboratoryRepository _labs;
    private readonly ICurrentUser _user;
    private readonly IClock _clock;

    public CheckInVisitHandler(IDailyVisitRepository visits, ILaboratoryRepository labs, ICurrentUser user, IClock clock)
    {
        _visits = visits; _labs = labs; _user = user; _clock = clock;
    }

    public async Task<Unit> Handle(CheckInVisitCommand request, CancellationToken ct)
    {
        var (visit, lab) = await VisitActionSupport.LoadAuthorizedAsync(request.VisitId, _visits, _labs, _user, ct);
        visit.CheckIn(request.SampleCount, _user.Username, _clock.UtcNow);
        lab.DeriveActiveFromActivity(); // BR-5
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
