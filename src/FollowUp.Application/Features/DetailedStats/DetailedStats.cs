using FluentValidation;
using FollowUp.Application.Common.Abstractions;
using FollowUp.Application.Common.Messaging;
using FollowUp.Domain.Identity;

namespace FollowUp.Application.Features.DetailedStats;

/// <summary>
/// One synced Oracle registration test-line, enriched with the lab's stamped geography/category/branch. The page
/// groups these by governorate → city → area → lab → reg-date → patient/accession → test and shows the combined
/// fee (cash + insurance) plus lab-per-date and per-patient subtotals. <c>LabCode</c>/geography are null for
/// registrations that resolve to no lab ("No lab").
/// </summary>
public sealed record DetailedStatDto(DateOnly Date, string? Governorate, string? City, string? Area,
    string? Category, string? Branch, string? RegBranch, string? LabCode, string? LabName,
    string AccNo, string PatientName, string TestCode, int TestType, string? TestName, decimal Fee,
    string? SampleStatus, string? TestStatus);

public interface IDetailedStatsQueries
{
    Task<IReadOnlyList<DetailedStatDto>> ListAsync(DateOnly from, DateOnly to, OrgScope scope, CancellationToken ct);
}

/// <summary>Lists synced registration test-lines over an inclusive range, scoped to the caller's labs.</summary>
public sealed record GetDetailedStatsQuery(DateOnly From, DateOnly To) : IQuery<IReadOnlyList<DetailedStatDto>>, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ViewDetailedStats };
}

public sealed class GetDetailedStatsHandler : IQueryHandler<GetDetailedStatsQuery, IReadOnlyList<DetailedStatDto>>
{
    private readonly IDetailedStatsQueries _queries;
    private readonly ICurrentUser _user;
    public GetDetailedStatsHandler(IDetailedStatsQueries queries, ICurrentUser user) { _queries = queries; _user = user; }
    public Task<IReadOnlyList<DetailedStatDto>> Handle(GetDetailedStatsQuery request, CancellationToken ct) =>
        _queries.ListAsync(request.From, request.To, _user.Scope, ct);
}

// ---- Detailed stats Oracle sync (date-scoped) ----

/// <summary>
/// Pulls transaction-level registration test-lines (patient, accession, test, fees) from Oracle for an inclusive
/// date range and replaces the synced rows for that window (SRS FR-17). Triggered manually from the Detailed
/// Statistics page (operator-chosen range, default yesterday→today); the nightly job also refreshes "yesterday".
/// </summary>
public sealed record SyncDetailedStatsCommand(DateOnly From, DateOnly To) : ICommand<OracleSyncResult>, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ViewDetailedStats };
}

public sealed class SyncDetailedStatsValidator : AbstractValidator<SyncDetailedStatsCommand>
{
    public SyncDetailedStatsValidator()
    {
        RuleFor(x => x.From).LessThanOrEqualTo(x => x.To)
            .WithMessage("The start date must be on or before the end date.");
    }
}

public sealed class SyncDetailedStatsHandler : ICommandHandler<SyncDetailedStatsCommand, OracleSyncResult>
{
    private readonly IOracleSyncRunner _runner;
    public SyncDetailedStatsHandler(IOracleSyncRunner runner) => _runner = runner;
    public Task<OracleSyncResult> Handle(SyncDetailedStatsCommand r, CancellationToken ct) =>
        _runner.RunDetailedStatsAsync(r.From, r.To, manual: true, ct);
}
