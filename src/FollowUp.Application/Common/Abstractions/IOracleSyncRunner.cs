namespace FollowUp.Application.Common.Abstractions;

/// <summary>Outcome of an Oracle synchronization run. Typed fields cover the original feeds; <see cref="Upserts"/>
/// and <see cref="Removes"/> carry per-feed counts for every feed (keyed by feed name) for the UI.</summary>
public sealed record OracleSyncResult(
    bool Ran, string Status, int LabsUpserted, int StatsUpserted,
    int GroupsUpserted = 0, int TestsUpserted = 0, int GroupsDeleted = 0, int TestsDeleted = 0,
    int LabsDeactivated = 0,
    IReadOnlyDictionary<string, int>? Upserts = null,
    IReadOnlyDictionary<string, int>? Removes = null);

/// <summary>
/// Executes the allow-listed, read-only Oracle sync (SRS FR-17). Invoked by the manual "sync now" use case
/// and by the scheduled Hangfire job. The scheduled path audits mutations (JOBS-002). Implemented in
/// Infrastructure; re-validates the allow-list at run time.
/// </summary>
public interface IOracleSyncRunner
{
    Task<OracleSyncResult> RunAsync(bool manual, CancellationToken ct);

    /// <summary>Runs only the TestStats feed over an explicit inclusive date range, upserting into existing
    /// statistics. Drives the nightly "yesterday" job and the date-scoped Test Statistics page button.</summary>
    Task<OracleSyncResult> RunTestStatsAsync(DateOnly from, DateOnly to, bool manual, CancellationToken ct);

    /// <summary>Runs only the LabStats feed over an explicit inclusive date range, upserting into existing
    /// per-lab statistics. Drives the nightly "yesterday" job and the date-scoped Lab Statistics page button.</summary>
    Task<OracleSyncResult> RunLabStatsAsync(DateOnly from, DateOnly to, bool manual, CancellationToken ct);

    /// <summary>Runs only the DetailedStats feed over an explicit inclusive date range, replacing the synced
    /// transaction-level rows for that window. Drives the nightly "yesterday" job and the Detailed Statistics
    /// page button.</summary>
    Task<OracleSyncResult> RunDetailedStatsAsync(DateOnly from, DateOnly to, bool manual, CancellationToken ct);
}
