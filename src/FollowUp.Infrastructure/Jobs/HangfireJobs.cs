using FollowUp.Application.Common.Abstractions;
using Hangfire;

namespace FollowUp.Infrastructure.Jobs;

// Thin job classes (architect rule): each resolves an application/infrastructure use case and does nothing
// else. [DisableConcurrentExecution] gives the single-execution guarantee that replaces the reference build's
// hand-rolled advisory locks (ADR-0004).

[DisableConcurrentExecution(timeoutInSeconds: 300)]
public sealed class BoardRolloverJob
{
    private readonly BoardService _service;
    public BoardRolloverJob(BoardService service) => _service = service;
    public Task RunAsync(CancellationToken ct) => _service.RunRolloverAsync(ct);
}

[DisableConcurrentExecution(timeoutInSeconds: 300)]
public sealed class MissedSweepJob
{
    private readonly BoardService _service;
    public MissedSweepJob(BoardService service) => _service = service;
    public Task RunAsync(CancellationToken ct) => _service.RunMissedSweepAsync(null, ct);
}

[DisableConcurrentExecution(timeoutInSeconds: 60)]
public sealed class NotificationDispatchJob
{
    private readonly OutboxDispatcher _dispatcher;
    public NotificationDispatchJob(OutboxDispatcher dispatcher) => _dispatcher = dispatcher;
    public Task RunAsync(CancellationToken ct) => _dispatcher.DispatchAsync(ct);
}

[DisableConcurrentExecution(timeoutInSeconds: 300)]
public sealed class OracleSyncJob
{
    private readonly IOracleSyncRunner _runner;
    public OracleSyncJob(IOracleSyncRunner runner) => _runner = runner;
    public Task RunAsync(CancellationToken ct) => _runner.RunAsync(manual: false, ct);
}

/// <summary>
/// Nightly statistics pull — refreshes just the previous (Cairo) day from Oracle for ALL three stats pages: the
/// aggregate Test Statistics, the per-lab Lab Statistics, and the transaction-level Detailed Statistics, over the
/// same window in one pass so the pages can never drift on coverage or snapshot timing (they were previously three
/// separately-scheduled jobs staggered ~5-15 min apart). Timeout covers the heavier detailed (per-line) pull.
/// </summary>
[DisableConcurrentExecution(timeoutInSeconds: 600)]
public sealed class NightlyStatsSyncJob
{
    private readonly IOracleSyncRunner _runner;
    private readonly IClock _clock;
    public NightlyStatsSyncJob(IOracleSyncRunner runner, IClock clock) { _runner = runner; _clock = clock; }
    public Task RunAsync(CancellationToken ct)
    {
        var yesterday = _clock.CairoToday.AddDays(-1);
        return _runner.RunNightlyStatsAsync(yesterday, yesterday, manual: false, ct);
    }
}

[DisableConcurrentExecution(timeoutInSeconds: 600)]
public sealed class RetentionJob
{
    private readonly RetentionService _service;
    public RetentionJob(RetentionService service) => _service = service;
    public Task RunAsync(CancellationToken ct) => _service.PurgeAsync(ct);
}
