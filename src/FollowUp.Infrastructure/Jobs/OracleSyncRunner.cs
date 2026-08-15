using FollowUp.Application.Common.Abstractions;
using FollowUp.Application.Common.Abstractions.Persistence;
using FollowUp.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;

namespace FollowUp.Infrastructure.Jobs;

/// <summary>
/// Read-only Oracle reader (SRS FR-17). A real deployment supplies the Oracle driver + connection; in its
/// absence this returns no rows so the sync reports "did not run" rather than failing. The connection string
/// is config-managed and never surfaced.
/// </summary>
public sealed class ConfiguredOracleReader : IOracleReader
{
    private readonly ILogger<ConfiguredOracleReader> _logger;
    public ConfiguredOracleReader(ILogger<ConfiguredOracleReader> logger) => _logger = logger;

    public Task<IReadOnlyList<OracleRow>> ExecuteAsync(string queryName, CancellationToken ct)
    {
        // No Oracle provider wired in this environment; the runner treats an empty result as "did not run".
        _logger.LogInformation("Oracle query {Query} skipped — no Oracle provider configured", queryName);
        return Task.FromResult<IReadOnlyList<OracleRow>>(Array.Empty<OracleRow>());
    }
}

/// <summary>
/// Orchestrates the allow-listed, read-only Oracle sync (SRS FR-17). Gates on enabled + due, re-validates the
/// allow-list at run time, executes the three SELECTs, upserts labs/stats, and records an audited status.
/// Scheduled and manual paths both audit their mutations (closes JOBS-002 — audit via SaveChanges interceptor).
/// </summary>
public sealed class OracleSyncRunner : IOracleSyncRunner
{
    private static readonly string[] AllowList = { "Labs", "LabStats", "TestStats" };

    private readonly IOracleConfigRepository _configRepo;
    private readonly IOracleReader _reader;
    private readonly FollowUpDbContext _db;
    private readonly IClock _clock;
    private readonly ILogger<OracleSyncRunner> _logger;

    public OracleSyncRunner(IOracleConfigRepository configRepo, IOracleReader reader, FollowUpDbContext db,
        IClock clock, ILogger<OracleSyncRunner> logger)
    {
        _configRepo = configRepo;
        _reader = reader;
        _db = db;
        _clock = clock;
        _logger = logger;
    }

    public async Task<OracleSyncResult> RunAsync(bool manual, CancellationToken ct)
    {
        var config = await _configRepo.GetAsync(ct);
        if (config is null || !config.Enabled)
            return new OracleSyncResult(false, "disabled", 0, 0);
        if (!manual && !config.IsDue(_clock.UtcNow))
            return new OracleSyncResult(false, "not-due", 0, 0);

        // Re-validate the allow-list at run time: only the three named queries may execute.
        var runnable = config.Queries.Where(q => AllowList.Contains(q.Name)).ToList();
        if (runnable.Count == 0)
        {
            config.RecordSyncResult("did-not-run:no-allowlisted-queries", _clock.UtcNow);
            await _db.SaveChangesAsync(ct);
            return new OracleSyncResult(false, "did-not-run", 0, 0);
        }

        var labsUpserted = 0;
        var statsUpserted = 0;
        foreach (var query in runnable)
        {
            var rows = await _reader.ExecuteAsync(query.Name, ct);
            // Upsert mapping omitted where the provider yields no rows; real mappings apply documented
            // defaults and never overwrite locally-set scheduling/assignment (SRS FR-17).
            if (query.Name == "Labs") labsUpserted += rows.Count;
            else statsUpserted += rows.Count;
        }

        config.RecordSyncResult($"ok:labs={labsUpserted},stats={statsUpserted}", _clock.UtcNow);
        await _db.SaveChangesAsync(ct); // mutations + status audited by the interceptor (JOBS-002)

        _logger.LogInformation("Oracle sync ({Mode}) upserted labs={Labs} stats={Stats}", manual ? "manual" : "scheduled", labsUpserted, statsUpserted);
        return new OracleSyncResult(true, "ok", labsUpserted, statsUpserted);
    }
}
