namespace FollowUp.Application.Common.Abstractions;

/// <summary>Outcome of an Oracle synchronization run.</summary>
public sealed record OracleSyncResult(bool Ran, string Status, int LabsUpserted, int StatsUpserted);

/// <summary>
/// Executes the allow-listed, read-only Oracle sync (SRS FR-17). Invoked by the manual "sync now" use case
/// and by the scheduled Hangfire job. The scheduled path audits mutations (JOBS-002). Implemented in
/// Infrastructure; re-validates the allow-list at run time.
/// </summary>
public interface IOracleSyncRunner
{
    Task<OracleSyncResult> RunAsync(bool manual, CancellationToken ct);
}
