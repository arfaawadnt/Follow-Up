using System.Data.Common;
using FollowUp.Application.Common.Abstractions;
using FollowUp.Application.Common.Abstractions.Persistence;
using Microsoft.Extensions.Logging;
using Oracle.ManagedDataAccess.Client;

namespace FollowUp.Infrastructure.Jobs;

/// <summary>
/// Default, config-managed SQL for the allow-listed Oracle feeds (SRS FR-17). Used at startup provisioning
/// when no explicit override is supplied via environment. The SQL is SELECT-only and parameterised on a
/// date window (<c>:from_date</c>/<c>:to_date</c>) so each sync pulls a bounded, incremental range.
/// </summary>
public static class OracleDefaultQueries
{
    /// <summary>Per-test daily counts and income (maps to <c>TestStatistic</c>: date, test code, count, income).</summary>
    public const string TestStats =
        "SELECT TRUNC(r.reg_date) AS the_date, " +
        "rss.service_code AS test_code, " +
        "COUNT(rss.service_code) AS test_count, " +
        "SUM(NVL(rss.patient_fee,0)+NVL(rss.insurance_fee,0)) AS test_income " +
        "FROM reg r " +
        "JOIN reg_selected_services rss ON rss.reg_key = r.reg_key " +
        "JOIN global_tests2 gt ON gt.test_code = rss.service_code AND gt.test_type = rss.service_type " +
        "WHERE r.reg_date >= :from_date AND r.reg_date < :to_date " +
        "AND rss.service_type <> 7 AND NVL(rss.iscancelled,0) <> 1 " +
        "GROUP BY TRUNC(r.reg_date), rss.service_code";

    /// <summary>
    /// Per-lab daily volumes (maps to <c>DailyLabStatistic</c>: date, lab code, registrations, test count, income).
    /// Attributes each registration to a lab via its referring doctor (reg.doctor → doctors.doctor_name →
    /// lab.doctor_code), aggregated to the lab grain. Cancelled services are excluded in the join so they never
    /// count toward tests/income; rows whose doctor resolves to no lab are dropped.
    /// </summary>
    public const string LabStats =
        "SELECT TRUNC(r.reg_date) AS the_date, " +
        "l.lab_code AS lab_code, " +
        "COUNT(DISTINCT r.reg_key) AS reg_count, " +
        "COUNT(rss.service_code) AS test_count, " +
        "SUM(NVL(rss.patient_fee,0)+NVL(rss.insurance_fee,0)) AS income " +
        "FROM reg r " +
        "LEFT JOIN doctors d ON UPPER(TRIM(d.doctor_name)) = UPPER(TRIM(r.doctor)) " +
        "LEFT JOIN lab l ON l.doctor_code = d.doctor_code " +
        "LEFT JOIN reg_selected_services rss ON rss.reg_key = r.reg_key AND NVL(rss.iscancelled,0) <> 1 " +
        "WHERE r.doctor IS NOT NULL AND l.lab_code IS NOT NULL " +
        "AND r.reg_date >= :from_date AND r.reg_date < :to_date " +
        "GROUP BY TRUNC(r.reg_date), l.lab_code";

    /// <summary>Active test-group master (maps to <c>TestGroup</c>: code, name). Only VISIBLE=1; mirrored by the sync.</summary>
    public const string Groups =
        "SELECT group_code, group_name FROM groups WHERE visible = 1";

    /// <summary>Active test-catalogue master (maps to <c>TestSetup</c>: code, name, type, group, cost). Only VISIBLE=1; mirrored.</summary>
    public const string Tests =
        "SELECT test_code, test_name, test_type, group_code, group_name, cost FROM global_tests2 WHERE visible = 1";

    // --- Reference / geography / workforce / labs (SRS FR-18/FR-4/FR-3) ---

    public const string Governorates = "SELECT governcate_code, governcate_name FROM governcate";
    public const string Cities = "SELECT city_code, city_name, governcate_code FROM city";
    public const string Areas = "SELECT area_code, area_name, city_code FROM area";
    public const string LabCategories = "SELECT category_id, category_name FROM lab_category";
    public const string Branches = "SELECT branch_code, branch_name FROM branches";
    public const string Reps = "SELECT rep_code, rep_name FROM representative";

    /// <summary>Lab master joined to its default COLLECTOR rep (LAB_REP, preferring default_rep).</summary>
    public const string Labs =
        "SELECT l.lab_code, l.lab_name, l.governcate, l.city, l.area, l.address, l.category_id, " +
        "lr.rep_code AS collector_rep_code " +
        "FROM lab l LEFT JOIN (" +
        "  SELECT lab_code, MIN(rep_code) KEEP (DENSE_RANK FIRST ORDER BY NVL(default_rep,0) DESC) AS rep_code " +
        "  FROM lab_rep GROUP BY lab_code" +
        ") lr ON lr.lab_code = l.lab_code";
}

/// <summary>
/// Read-only Oracle reader (SRS FR-17) backed by fully-managed ODP.NET. Loads the config-managed connection
/// string and the allow-listed, fingerprint-validated SELECT for the requested feed, binds a bounded date
/// window, executes it, and returns untyped rows for the sync runner to map. Never writes to Oracle.
/// If no connection is configured it returns no rows so the sync reports "did not run" rather than failing.
/// </summary>
public sealed class OracleDbReader : IOracleReader
{
    private static readonly string[] AllowList =
    {
        "LabStats", "TestStats", "Groups", "Tests",
        "Governorates", "Cities", "Areas", "LabCategories", "Branches", "Reps", "Labs",
    };

    private readonly IOracleConfigRepository _configRepo;
    private readonly ILogger<OracleDbReader> _logger;

    public OracleDbReader(IOracleConfigRepository configRepo, ILogger<OracleDbReader> logger)
    {
        _configRepo = configRepo;
        _logger = logger;
    }

    public Task<IReadOnlyList<OracleRow>> ExecuteAsync(string queryName, CancellationToken ct)
    {
        // Default rolling window: [today - lookback, today + 1).
        var lookback = Math.Abs(ReadInt("FOLLOWUP_ORACLE_LOOKBACK_DAYS", 45));
        var today = DateTime.Today;
        return ExecuteAsync(queryName, new OracleDateWindow(today.AddDays(-lookback), today.AddDays(1)), ct);
    }

    public async Task<IReadOnlyList<OracleRow>> ExecuteAsync(string queryName, OracleDateWindow window, CancellationToken ct)
    {
        var cfg = await _configRepo.GetAsync(ct);
        if (cfg is null || string.IsNullOrWhiteSpace(cfg.ConnectionString))
        {
            _logger.LogWarning("Oracle query {Query} skipped — no connection string configured", queryName);
            return Array.Empty<OracleRow>();
        }

        // Re-validate the allow-list and the SQL fingerprint at run time (tamper guard, SRS FR-17).
        if (!AllowList.Contains(queryName))
            throw new InvalidOperationException($"Query '{queryName}' is not allow-listed.");
        var q = cfg.Queries.FirstOrDefault(x => x.Name == queryName);
        if (q is null)
            return Array.Empty<OracleRow>();
        if (!q.Matches(q.Sql))
            throw new InvalidOperationException($"Allow-listed query '{queryName}' failed fingerprint validation.");

        var fromDate = window.FromDate;
        var toDate = window.ToExclusive;

        var rows = new List<OracleRow>();
        await using var conn = new OracleConnection(cfg.ConnectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = q.Sql;
        cmd.CommandTimeout = ReadInt("FOLLOWUP_ORACLE_TIMEOUT_SECONDS", 120);
        ((OracleCommand)cmd).BindByName = true;
        if (q.Sql.Contains(":from_date", StringComparison.OrdinalIgnoreCase))
            cmd.Parameters.Add(new OracleParameter("from_date", OracleDbType.Date) { Value = fromDate });
        if (q.Sql.Contains(":to_date", StringComparison.OrdinalIgnoreCase))
            cmd.Parameters.Add(new OracleParameter("to_date", OracleDbType.Date) { Value = toDate });

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var values = new Dictionary<string, object?>(reader.FieldCount, StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < reader.FieldCount; i++)
                values[reader.GetName(i).ToUpperInvariant()] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            rows.Add(new OracleRow(values));
        }

        _logger.LogInformation(
            "Oracle query {Query} returned {Count} rows (window {From:yyyy-MM-dd}..{To:yyyy-MM-dd})",
            queryName, rows.Count, fromDate, toDate);
        return rows;
    }

    private static int ReadInt(string envVar, int fallback) =>
        int.TryParse(Environment.GetEnvironmentVariable(envVar), out var v) ? v : fallback;
}
