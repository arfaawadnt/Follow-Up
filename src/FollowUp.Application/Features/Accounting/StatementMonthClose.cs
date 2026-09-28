using FollowUp.Application.Common.Abstractions;
using FollowUp.Application.Common.Abstractions.Persistence;
using FollowUp.Application.Common.Exceptions;
using FollowUp.Application.Common.Messaging;
using FollowUp.Application.Common.Security;
using FollowUp.Domain.Accounting;
using FollowUp.Domain.Common;
using FollowUp.Domain.Identity;
using FollowUp.Domain.Laboratories;
using FollowUp.Domain.Reference;
using FollowUp.Domain.Representatives;
using FluentValidation;
using MediatR;
using System.Globalization;

namespace FollowUp.Application.Features.Accounting;

/// <summary>Culture-invariant month label for messages ("September 2026") — the service thread culture may be Arabic.</summary>
public static class StatementMonthText
{
    public static string Of(int year, int month) => new DateOnly(year, month, 1).ToString("MMMM yyyy", CultureInfo.InvariantCulture);
}

// =====================================================================================================================
// Month close of a Lab Responsible's statement (operator decisions, 2026-09-28): closing is manual (a button), at the
// Lab Responsible level only; the closing balance is carried into the next month as its opening balance — a debit when
// the rep owes, a credit when the balance is in the rep's favour; the Rep Income sheet's "previous remaining" is NOT
// affected (the statement recomputes the balance its own way). Records dated inside a closed month can no longer be
// added, changed or deleted until the month is reopened (IStatementCloseGuard, called by every accounting command).
// =====================================================================================================================

/// <summary>
/// Refuses a write that would change a closed month of a Lab Responsible's statement. Implemented over the database in
/// Infrastructure; <see cref="NoStatementCloseGuard"/> is the no-op used when nothing is registered (unit tests).
/// </summary>
public interface IStatementCloseGuard
{
    /// <summary>The month of <paramref name="date"/> must be open for every rep given.</summary>
    Task EnsureOpenForRepsAsync(IEnumerable<RepresentativeId> reps, DateOnly date, CancellationToken ct);
    /// <summary>The month must be open for the Lab Responsible of the lab and (a Rep penalty) for the rep who made it.</summary>
    Task EnsureOpenForPenaltyAsync(LaboratoryId lab, RepresentativeId? performedBy, DateOnly date, CancellationToken ct);
    /// <summary>The month must be open for every Lab Responsible of the area's labs (a deduction posts to all of them).</summary>
    Task EnsureOpenForAreaAsync(AreaId area, DateOnly date, CancellationToken ct);
}

public sealed class NoStatementCloseGuard : IStatementCloseGuard
{
    public static readonly NoStatementCloseGuard Instance = new();
    public Task EnsureOpenForRepsAsync(IEnumerable<RepresentativeId> reps, DateOnly date, CancellationToken ct) => Task.CompletedTask;
    public Task EnsureOpenForPenaltyAsync(LaboratoryId lab, RepresentativeId? performedBy, DateOnly date, CancellationToken ct) => Task.CompletedTask;
    public Task EnsureOpenForAreaAsync(AreaId area, DateOnly date, CancellationToken ct) => Task.CompletedTask;
}

/// <summary>One closed month of a Lab Responsible's statement. <c>IsLatest</c>: the only one that can be reopened.</summary>
public sealed record StatementMonthCloseDto(Guid Id, Guid RepresentativeId, int Year, int Month, decimal ClosingBalance,
    DateTimeOffset ClosedAt, string ClosedBy, string? Notes, bool IsLatest);

/// <summary>The closed months of a Lab Responsible, latest first.</summary>
public sealed record GetStatementMonthClosesQuery(Guid RepresentativeId) : IQuery<IReadOnlyList<StatementMonthCloseDto>>, IAuthorizedRequest
{ public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ViewAccounting }; }
public sealed class GetStatementMonthClosesValidator : AbstractValidator<GetStatementMonthClosesQuery>
{ public GetStatementMonthClosesValidator() => RuleFor(x => x.RepresentativeId).NotEmpty(); }
public sealed class GetStatementMonthClosesHandler : IQueryHandler<GetStatementMonthClosesQuery, IReadOnlyList<StatementMonthCloseDto>>
{
    private readonly IAccountingQueries _queries; private readonly ICurrentUser _user;
    public GetStatementMonthClosesHandler(IAccountingQueries queries, ICurrentUser user) { _queries = queries; _user = user; }
    public Task<IReadOnlyList<StatementMonthCloseDto>> Handle(GetStatementMonthClosesQuery r, CancellationToken ct) =>
        _queries.MonthClosesAsync(r.RepresentativeId, _user.Scope, ct);
}

/// <summary>
/// Closes one month of a Lab Responsible's statement: the month must have ended, months close in order (the next after
/// the latest close), and the closing balance is the statement balance at the month's last day — computed from the day
/// after the previous close (its opening balance carries everything before), or from the beginning of the data for the
/// first close ever. Returns the stored close.
/// </summary>
public sealed record CloseStatementMonthCommand(Guid RepresentativeId, int Year, int Month, string? Notes) : ICommand<StatementMonthCloseDto>, IAuthorizedRequest
{ public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ManageAccounting }; }
public sealed class CloseStatementMonthValidator : AbstractValidator<CloseStatementMonthCommand>
{
    public CloseStatementMonthValidator()
    {
        RuleFor(x => x.RepresentativeId).NotEmpty();
        RuleFor(x => x.Year).InclusiveBetween(2000, 2100);
        RuleFor(x => x.Month).InclusiveBetween(1, 12);
        RuleFor(x => x.Notes).MaximumLength(500);
    }
}
public sealed class CloseStatementMonthHandler : ICommandHandler<CloseStatementMonthCommand, StatementMonthCloseDto>
{
    /// <summary>Where the very first close starts summing from: all data (there is nothing earlier to carry).</summary>
    public static readonly DateOnly BeginningOfData = new(2000, 1, 1);

    private readonly IStatementMonthCloseRepository _repo; private readonly IRepresentativeRepository _reps;
    private readonly IAccountingQueries _queries; private readonly ICurrentUser _user; private readonly IClock _clock;
    public CloseStatementMonthHandler(IStatementMonthCloseRepository repo, IRepresentativeRepository reps, IAccountingQueries queries, ICurrentUser user, IClock clock)
    { _repo = repo; _reps = reps; _queries = queries; _user = user; _clock = clock; }

    public async Task<StatementMonthCloseDto> Handle(CloseStatementMonthCommand r, CancellationToken ct)
    {
        var rep = await _reps.GetByIdAsync(new RepresentativeId(r.RepresentativeId), ct) ?? throw new NotFoundException("Representative", r.RepresentativeId);
        _user.EnsureInScope(rep);
        if (rep.Type != RepresentativeType.LabResponsible)
            throw new DomainException("Only a Lab Responsible's statement is closed by month.");
        var monthStart = new DateOnly(r.Year, r.Month, 1);
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);
        if (monthEnd >= _clock.CairoToday)
            throw new DomainException($"{StatementMonthText.Of(r.Year, r.Month)} can be closed only after it has ended.");

        var closes = await _repo.ForRepAsync(rep.Id, ct);
        if (closes.Any(c => c.Year == r.Year && c.Month == r.Month))
            throw new DomainException($"{StatementMonthText.Of(r.Year, r.Month)} is already closed for {rep.FullName}.");
        var latest = closes.OrderBy(c => c.Year).ThenBy(c => c.Month).LastOrDefault();
        if (latest is not null)
        {
            var (ny, nm) = StatementMonthClose.NextMonth(latest.Year, latest.Month);
            if (ny != r.Year || nm != r.Month)
                throw new DomainException($"Months close in order: the next month to close for {rep.FullName} is {StatementMonthText.Of(ny, nm)}.");
        }

        // The statement from the day after the previous close (its opening line carries the stored balance) to the month's end.
        var from = latest is null ? BeginningOfData : latest.MonthEnd.AddDays(1);
        var statement = await _queries.StatementAsync(StatementBy.Responsible, rep.Id.Value, from, monthEnd, _user.Scope, ct)
            ?? throw new NotFoundException("Representative", r.RepresentativeId);
        var close = StatementMonthClose.Close(rep.Id, r.Year, r.Month, statement.Balance, _user.UserId, _clock.UtcNow, r.Notes);
        _repo.Add(close);
        return new StatementMonthCloseDto(close.Id.Value, rep.Id.Value, close.Year, close.Month, close.ClosingBalance.Amount,
            close.ClosedAtUtc, _user.Username, close.Notes, IsLatest: true);
    }
}

/// <summary>Reopens the LATEST closed month of a Lab Responsible (earlier months stay closed until the later ones are reopened).</summary>
public sealed record ReopenStatementMonthCommand(Guid RepresentativeId, int Year, int Month) : ICommand, IAuthorizedRequest
{ public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ManageAccounting }; }
public sealed class ReopenStatementMonthValidator : AbstractValidator<ReopenStatementMonthCommand>
{
    public ReopenStatementMonthValidator()
    {
        RuleFor(x => x.RepresentativeId).NotEmpty();
        RuleFor(x => x.Year).InclusiveBetween(2000, 2100);
        RuleFor(x => x.Month).InclusiveBetween(1, 12);
    }
}
public sealed class ReopenStatementMonthHandler : ICommandHandler<ReopenStatementMonthCommand>
{
    private readonly IStatementMonthCloseRepository _repo; private readonly IRepresentativeRepository _reps; private readonly ICurrentUser _user;
    public ReopenStatementMonthHandler(IStatementMonthCloseRepository repo, IRepresentativeRepository reps, ICurrentUser user) { _repo = repo; _reps = reps; _user = user; }
    public async Task<Unit> Handle(ReopenStatementMonthCommand r, CancellationToken ct)
    {
        var rep = await _reps.GetByIdAsync(new RepresentativeId(r.RepresentativeId), ct) ?? throw new NotFoundException("Representative", r.RepresentativeId);
        _user.EnsureInScope(rep);
        var closes = await _repo.ForRepAsync(rep.Id, ct);
        var close = closes.FirstOrDefault(c => c.Year == r.Year && c.Month == r.Month)
            ?? throw new NotFoundException("StatementMonthClose", $"{r.Year}-{r.Month:00}");
        var latest = closes.OrderBy(c => c.Year).ThenBy(c => c.Month).Last();
        if (latest.Id != close.Id)
            throw new DomainException($"Reopen the later months first: {StatementMonthText.Of(latest.Year, latest.Month)} is still closed.");
        _repo.Remove(close);
        return Unit.Value;
    }
}
