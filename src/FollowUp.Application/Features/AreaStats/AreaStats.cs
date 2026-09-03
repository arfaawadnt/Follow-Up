using FluentValidation;
using FollowUp.Application.Common.Abstractions;
using FollowUp.Application.Common.Messaging;
using FollowUp.Domain.Identity;

namespace FollowUp.Application.Features.AreaStats;

/// <summary>
/// Daily test volumes rolled up to the geography grain (date, governorate, city, area). Derived at read time
/// from <c>DailyLabStatistic</c> joined to each lab's stamped geography — there is no separate area-statistics
/// table or Oracle feed; the nightly lab-stats sync keeps the underlying rows current. City is carried for
/// filtering; the page groups by governorate → area.
/// </summary>
public sealed record AreaStatDto(DateOnly Date, string? Governorate, string? City, string? Area, int TestCount, decimal Income);

public interface IAreaStatsQueries
{
    Task<IReadOnlyList<AreaStatDto>> ListAsync(DateOnly from, DateOnly to, OrgScope scope, CancellationToken ct);
}

/// <summary>Lists daily test volumes aggregated by geography over a range, scoped to the caller's labs.</summary>
public sealed record GetAreaStatsQuery(DateOnly From, DateOnly To) : IQuery<IReadOnlyList<AreaStatDto>>, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ViewAreaStats };
}

public sealed class GetAreaStatsHandler : IQueryHandler<GetAreaStatsQuery, IReadOnlyList<AreaStatDto>>
{
    private readonly IAreaStatsQueries _queries;
    private readonly ICurrentUser _user;
    public GetAreaStatsHandler(IAreaStatsQueries queries, ICurrentUser user) { _queries = queries; _user = user; }
    public Task<IReadOnlyList<AreaStatDto>> Handle(GetAreaStatsQuery request, CancellationToken ct) =>
        _queries.ListAsync(request.From, request.To, _user.Scope, ct);
}

// ---- Area stats Oracle sync (date-scoped) ----

/// <summary>
/// Pulls the per-lab daily statistics that back the area rollup from Oracle for an inclusive date range and
/// upserts them (SRS FR-17). Area Statistics is a re-grouping of the lab-statistics data, so this reuses the
/// lab-stats sync runner; the nightly "yesterday" job already refreshes the same data automatically.
/// Triggered manually from the Area Statistics page (operator-chosen range, default yesterday→today).
/// </summary>
public sealed record SyncAreaStatsCommand(DateOnly From, DateOnly To) : ICommand<OracleSyncResult>, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ViewAreaStats };
}

public sealed class SyncAreaStatsValidator : AbstractValidator<SyncAreaStatsCommand>
{
    public SyncAreaStatsValidator()
    {
        RuleFor(x => x.From).LessThanOrEqualTo(x => x.To)
            .WithMessage("The start date must be on or before the end date.");
    }
}

public sealed class SyncAreaStatsHandler : ICommandHandler<SyncAreaStatsCommand, OracleSyncResult>
{
    private readonly IOracleSyncRunner _runner;
    public SyncAreaStatsHandler(IOracleSyncRunner runner) => _runner = runner;
    public Task<OracleSyncResult> Handle(SyncAreaStatsCommand r, CancellationToken ct) =>
        _runner.RunLabStatsAsync(r.From, r.To, manual: true, ct);
}
