using FollowUp.Application.Common.Abstractions;
using FollowUp.Application.Common.Abstractions.Persistence;
using FollowUp.Domain.Common;
using FollowUp.Domain.Laboratories;
using FollowUp.Domain.Reference;
using FollowUp.Domain.Representatives;
using FollowUp.Domain.Statistics;
using FollowUp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
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

    public Task<IReadOnlyList<OracleRow>> ExecuteAsync(string queryName, OracleDateWindow window, CancellationToken ct) =>
        ExecuteAsync(queryName, ct);
}

/// <summary>
/// Orchestrates the allow-listed, read-only Oracle sync (SRS FR-17). Gates on enabled + due, re-validates the
/// allow-list at run time, executes the three SELECTs, upserts labs/stats, and records an audited status.
/// Scheduled and manual paths both audit their mutations (closes JOBS-002 — audit via SaveChanges interceptor).
/// </summary>
public sealed class OracleSyncRunner : IOracleSyncRunner
{
    private static readonly string[] AllowList =
    {
        "LabStats", "TestStats", "Groups", "Tests",
        "Governorates", "Cities", "Areas", "LabCategories", "Branches", "Reps", "Labs",
    };

    // Feeds run in dependency order: geography/reference before the records that resolve against them.
    private static readonly Dictionary<string, int> FeedOrder = new()
    {
        ["Governorates"] = 0, ["LabCategories"] = 1, ["Branches"] = 2, ["Cities"] = 3, ["Areas"] = 4,
        ["Reps"] = 5, ["Groups"] = 6, ["Tests"] = 7, ["Labs"] = 8, ["LabStats"] = 9, ["TestStats"] = 10,
    };

    private readonly IOracleConfigRepository _configRepo;
    private readonly IOracleReader _reader;
    private readonly ITestStatisticRepository _testStats;
    private readonly IDailyLabStatisticRepository _labStats;
    private readonly IDetailedRegistrationRepository _detailed;
    private readonly ITestGroupRepository _groups;
    private readonly ITestSetupRepository _setups;
    private readonly IRefItemRepository _refs;
    private readonly ICityRepository _cities;
    private readonly IAreaRepository _areas;
    private readonly IRepresentativeRepository _repsRepo;
    private readonly ILaboratoryRepository _labs;
    private readonly FollowUpDbContext _db;
    private readonly IClock _clock;
    private readonly ILogger<OracleSyncRunner> _logger;

    public OracleSyncRunner(IOracleConfigRepository configRepo, IOracleReader reader,
        ITestStatisticRepository testStats, IDailyLabStatisticRepository labStats,
        IDetailedRegistrationRepository detailed,
        ITestGroupRepository groups, ITestSetupRepository setups,
        IRefItemRepository refs, ICityRepository cities, IAreaRepository areas,
        IRepresentativeRepository repsRepo, ILaboratoryRepository labs,
        FollowUpDbContext db, IClock clock, ILogger<OracleSyncRunner> logger)
    {
        _configRepo = configRepo;
        _reader = reader;
        _testStats = testStats;
        _labStats = labStats;
        _detailed = detailed;
        _groups = groups;
        _setups = setups;
        _refs = refs;
        _cities = cities;
        _areas = areas;
        _repsRepo = repsRepo;
        _labs = labs;
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

        // Re-validate the allow-list at run time; run feeds in dependency order (groups before tests).
        // TestStats and LabStats are excluded here — each runs on its own date-scoped path (nightly "yesterday"
        // job + a page button), so the general sync never pulls the multi-year statistics history.
        var runnable = config.Queries
            .Where(q => AllowList.Contains(q.Name) && q.Name != "TestStats" && q.Name != "LabStats")
            .OrderBy(q => FeedOrder.TryGetValue(q.Name, out var o) ? o : 99)
            .ToList();
        if (runnable.Count == 0)
        {
            config.RecordSyncResult("did-not-run:no-allowlisted-queries", _clock.UtcNow);
            await _db.SaveChangesAsync(ct);
            return new OracleSyncResult(false, "did-not-run", 0, 0);
        }

        var upserts = new Dictionary<string, int>();
        var removes = new Dictionary<string, int>();
        foreach (var query in runnable)
        {
            var rows = await _reader.ExecuteAsync(query.Name, ct);
            (int up, int rem) r = query.Name switch
            {
                "Governorates" => await MirrorRefItemsAsync(rows, RefType.Governorate, "GOVERNCATE_CODE", "GOVERNCATE_NAME", ct),
                "Branches"     => await MirrorRefItemsAsync(rows, RefType.Branch, "BRANCH_CODE", "BRANCH_NAME", ct),
                "LabCategories"=> await MirrorRefItemsAsync(rows, RefType.LabCategory, "CATEGORY_ID", "CATEGORY_NAME", ct),
                "Cities"       => await MirrorCitiesAsync(rows, ct),
                "Areas"        => await MirrorAreasAsync(rows, ct),
                "Reps"         => await MirrorRepsAsync(rows, ct),
                "Labs"         => await MirrorLabsAsync(rows, ct),
                "Groups"       => await MirrorGroupsAsync(rows, ct),
                "Tests"        => await MirrorTestsAsync(rows, ct),
                _              => (0, 0),
            };
            upserts[query.Name] = r.up;
            removes[query.Name] = r.rem;
            // Persist after each feed so later feeds see prior results (e.g. Cities resolve governorates,
            // Labs resolve geography/reps) and so a large run commits incrementally.
            await _db.SaveChangesAsync(ct);
        }

        var status = "ok:" + string.Join(",", upserts.Select(kv => $"{kv.Key}={kv.Value}(-{removes[kv.Key]})"));
        config.RecordSyncResult(status.Length > 500 ? status[..500] : status, _clock.UtcNow);
        await _db.SaveChangesAsync(ct); // mutations + status audited by the interceptor (JOBS-002)

        _logger.LogInformation("Oracle sync ({Mode}) {Status}", manual ? "manual" : "scheduled", status);

        int U(string k) => upserts.TryGetValue(k, out var v) ? v : 0;
        int D(string k) => removes.TryGetValue(k, out var v) ? v : 0;
        return new OracleSyncResult(true, "ok",
            LabsUpserted: U("Labs"), StatsUpserted: U("TestStats"),
            GroupsUpserted: U("Groups"), TestsUpserted: U("Tests"),
            GroupsDeleted: D("Groups"), TestsDeleted: D("Tests"),
            LabsDeactivated: D("Labs"),
            Upserts: upserts, Removes: removes);
    }

    /// <summary>
    /// Mirrors the Oracle GROUPS snapshot into <c>TestGroup</c>: adds/updates Oracle-sourced groups and deletes
    /// Oracle-sourced groups that no longer exist upstream. Manually-added groups are never touched (they keep
    /// their own code space and are protected from the mirror).
    /// </summary>
    private async Task<(int upserted, int deleted)> MirrorGroupsAsync(IReadOnlyList<OracleRow> rows, CancellationToken ct)
    {
        if (rows.Count == 0) return (0, 0);
        var existing = (await _groups.GetAllAsync(ct)).ToDictionary(g => g.Code, StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var upserted = 0;

        foreach (var row in rows)
        {
            var code = Str(row, "GROUP_CODE");
            if (string.IsNullOrWhiteSpace(code)) continue;
            var name = Str(row, "GROUP_NAME");
            seen.Add(code);
            if (existing.TryGetValue(code, out var group)) group.ApplyOracle(name);
            else _groups.Add(TestGroup.FromOracle(code, name));
            upserted++;
        }

        // Delete Oracle-sourced groups no longer in the active snapshot. Tests linked to a removed group are
        // unlinked automatically via the SET NULL FK; the Tests feed then relinks any that are still active.
        var deleted = 0;
        foreach (var g in existing.Values.Where(g => g.Source == CatalogueSource.Oracle && !seen.Contains(g.Code)))
        {
            _groups.Remove(g);
            deleted++;
        }
        return (upserted, deleted);
    }

    /// <summary>
    /// Mirrors the Oracle GLOBAL_TESTS2 snapshot into <c>TestSetup</c> keyed by (code, type): adds/updates
    /// Oracle-sourced tests (resolving each to its group by code) and deletes Oracle-sourced tests removed
    /// upstream. Manually-added tests are never touched.
    /// </summary>
    private async Task<(int upserted, int deleted)> MirrorTestsAsync(IReadOnlyList<OracleRow> rows, CancellationToken ct)
    {
        if (rows.Count == 0) return (0, 0);
        var groupsByCode = (await _groups.GetAllAsync(ct)).ToDictionary(g => g.Code, StringComparer.OrdinalIgnoreCase);
        var existing = (await _setups.GetAllAsync(ct))
            .ToDictionary(s => (s.Code, s.TestType));
        var seen = new HashSet<(string, int)>();
        var upserted = 0;

        foreach (var row in rows)
        {
            var code = Str(row, "TEST_CODE")?.Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(code)) continue;
            var type = Int(row, "TEST_TYPE");
            var name = Str(row, "TEST_NAME");
            var cost = new Money(Math.Max(0m, Dec(row, "COST")));

            // Resolve the group by Oracle group_code, but ONLY to an active (synced) group. Tests whose group
            // is hidden/absent are left unlinked so the Groups page stays strictly VISIBLE=1.
            TestGroupId? groupId = null;
            var groupCode = Str(row, "GROUP_CODE");
            if (!string.IsNullOrWhiteSpace(groupCode) && groupsByCode.TryGetValue(groupCode, out var grp))
                groupId = grp.Id;

            var key = (code, type);
            seen.Add(key);
            if (existing.TryGetValue(key, out var setup)) setup.ApplyOracle(name, groupId, type, cost);
            else _setups.Add(TestSetup.FromOracle(code, name, groupId, type, cost));
            upserted++;
        }

        var deleted = 0;
        foreach (var s in existing.Values.Where(s => s.Source == CatalogueSource.Oracle && !seen.Contains((s.Code, s.TestType))))
        {
            _setups.Remove(s);
            deleted++;
        }
        return (upserted, deleted);
    }

    /// <summary>Generic mirror for RefItem-backed feeds (governorates, branches, lab categories), keyed by (type, code).</summary>
    private async Task<(int, int)> MirrorRefItemsAsync(IReadOnlyList<OracleRow> rows, RefType type,
        string codeCol, string nameCol, CancellationToken ct)
    {
        if (rows.Count == 0) return (0, 0);
        var existing = (await _refs.GetByTypeAsync(type, ct)).ToDictionary(r => r.Code, StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var up = 0;
        foreach (var row in rows)
        {
            var code = Str(row, codeCol);
            if (string.IsNullOrWhiteSpace(code)) continue;
            var name = Str(row, nameCol);
            seen.Add(code);
            if (existing.TryGetValue(code, out var item)) item.ApplyOracle(name);
            else _refs.Add(RefItem.FromOracle(type, code, name));
            up++;
        }
        var del = 0;
        foreach (var it in existing.Values.Where(i => i.Source == RecordSource.Oracle && !seen.Contains(i.Code)))
        { _refs.Remove(it); del++; }
        return (up, del);
    }

    /// <summary>Mirrors CITY → <see cref="City"/> by Oracle CITY_CODE, resolving the governorate name from its code.</summary>
    private async Task<(int, int)> MirrorCitiesAsync(IReadOnlyList<OracleRow> rows, CancellationToken ct)
    {
        if (rows.Count == 0) return (0, 0);
        var govName = (await _refs.GetByTypeAsync(RefType.Governorate, ct))
            .ToDictionary(r => r.Code, r => r.NameEn, StringComparer.OrdinalIgnoreCase);
        var existing = (await _cities.GetAllAsync(ct)).Where(c => c.SourceCode != null)
            .ToDictionary(c => c.SourceCode!, StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var up = 0;
        foreach (var row in rows)
        {
            var code = Str(row, "CITY_CODE");
            if (string.IsNullOrWhiteSpace(code)) continue;
            var name = Str(row, "CITY_NAME") ?? code;
            var gov = Lookup(govName, Str(row, "GOVERNCATE_CODE")) ?? "-";
            seen.Add(code);
            if (existing.TryGetValue(code, out var city)) city.ApplyOracle(name, gov);
            else _cities.Add(City.FromOracle(code, name, gov));
            up++;
        }
        var del = 0;
        foreach (var c in existing.Values.Where(c => c.Source == RecordSource.Oracle && !seen.Contains(c.SourceCode!)))
        { _cities.Remove(c); del++; }
        return (up, del);
    }

    /// <summary>Mirrors AREA → <see cref="Area"/> by Oracle AREA_CODE, resolving its city by code.</summary>
    private async Task<(int, int)> MirrorAreasAsync(IReadOnlyList<OracleRow> rows, CancellationToken ct)
    {
        if (rows.Count == 0) return (0, 0);
        var cityIdByCode = (await _cities.GetAllAsync(ct)).Where(c => c.SourceCode != null)
            .ToDictionary(c => c.SourceCode!, c => c.Id, StringComparer.OrdinalIgnoreCase);
        var existing = (await _areas.GetAllAsync(ct)).Where(a => a.SourceCode != null)
            .ToDictionary(a => a.SourceCode!, StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var up = 0;
        foreach (var row in rows)
        {
            var code = Str(row, "AREA_CODE");
            if (string.IsNullOrWhiteSpace(code)) continue;
            var cityCode = Str(row, "CITY_CODE");
            if (cityCode is null || !cityIdByCode.TryGetValue(cityCode, out var cityId)) continue; // needs a resolvable city
            var name = Str(row, "AREA_NAME") ?? code;
            seen.Add(code);
            if (existing.TryGetValue(code, out var area)) area.ApplyOracle(name, cityId);
            else _areas.Add(Area.FromOracle(code, name, cityId));
            up++;
        }
        var del = 0;
        foreach (var a in existing.Values.Where(a => a.Source == RecordSource.Oracle && !seen.Contains(a.SourceCode!)))
        { _areas.Remove(a); del++; }
        return (up, del);
    }

    /// <summary>Mirrors REPRESENTATIVE → <see cref="Representative"/> by Oracle REP_CODE. Removed reps are deactivated
    /// (not deleted) because labs/commissions reference them.</summary>
    private async Task<(int, int)> MirrorRepsAsync(IReadOnlyList<OracleRow> rows, CancellationToken ct)
    {
        if (rows.Count == 0) return (0, 0);
        var existing = (await _repsRepo.GetAllAsync(ct)).Where(r => r.SourceCode != null)
            .ToDictionary(r => r.SourceCode!, StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var up = 0;
        foreach (var row in rows)
        {
            var code = Str(row, "REP_CODE");
            if (string.IsNullOrWhiteSpace(code)) continue;
            var name = Str(row, "REP_NAME") ?? code;
            seen.Add(code);
            if (existing.TryGetValue(code, out var rep)) rep.ApplyOracle(name);
            else _repsRepo.Add(Representative.FromOracle(code, name));
            up++;
        }
        var deact = 0;
        foreach (var rep in existing.Values.Where(r => r.Source == RecordSource.Oracle && !seen.Contains(r.SourceCode!) && r.IsActive))
        { rep.Deactivate(); deact++; }
        return (up, deact);
    }

    /// <summary>
    /// Mirrors LAB (+ default LAB_REP, + category/geography by code) → <see cref="Laboratory"/> by lab code.
    /// Oracle owns master fields (name, geography, category, address, marketing rep); the app keeps lifecycle.
    /// Manual labs are never touched; Oracle labs removed upstream are set Inactive (never hard-deleted).
    /// Domain events are cleared so a bulk sync doesn't trigger board-scheduling/notifications per lab.
    /// </summary>
    private async Task<(int, int)> MirrorLabsAsync(IReadOnlyList<OracleRow> rows, CancellationToken ct)
    {
        if (rows.Count == 0) return (0, 0);
        var govName = (await _refs.GetByTypeAsync(RefType.Governorate, ct)).ToDictionary(r => r.Code, r => r.NameEn, StringComparer.OrdinalIgnoreCase);
        var catName = (await _refs.GetByTypeAsync(RefType.LabCategory, ct)).ToDictionary(r => r.Code, r => r.NameEn, StringComparer.OrdinalIgnoreCase);
        var cityName = (await _cities.GetAllAsync(ct)).Where(c => c.SourceCode != null).ToDictionary(c => c.SourceCode!, c => c.Name, StringComparer.OrdinalIgnoreCase);
        var areaName = (await _areas.GetAllAsync(ct)).Where(a => a.SourceCode != null).ToDictionary(a => a.SourceCode!, a => a.Name, StringComparer.OrdinalIgnoreCase);
        var repIdByCode = (await _repsRepo.GetAllAsync(ct)).Where(r => r.SourceCode != null).ToDictionary(r => r.SourceCode!, r => r.Id, StringComparer.OrdinalIgnoreCase);

        var existing = (await _labs.GetAllAsync(ct)).ToDictionary(l => l.Code.Value, StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var up = 0;
        foreach (var row in rows)
        {
            var raw = Str(row, "LAB_CODE");
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var code = raw.Trim().ToUpperInvariant();
            var name = Str(row, "LAB_NAME") ?? code;
            var gov = Lookup(govName, Str(row, "GOVERNCATE"));
            var city = Lookup(cityName, Str(row, "CITY"));
            var area = Lookup(areaName, Str(row, "AREA"));
            var category = Lookup(catName, Str(row, "CATEGORY_ID"));
            var address = Str(row, "ADDRESS");
            RepresentativeId? collectorRepId = null;
            var repCode = Str(row, "COLLECTOR_REP_CODE");
            if (repCode is not null && repIdByCode.TryGetValue(repCode, out var rid)) collectorRepId = rid;

            seen.Add(code);
            if (existing.TryGetValue(code, out var lab))
            {
                if (lab.Source == RecordSource.Manual) continue; // protect manual labs
                lab.ApplyOracleMaster(name, category, null, gov, city, area, address, collectorRepId);
            }
            else
            {
                lab = Laboratory.FromOracle(LabCode.Create(code), name);
                lab.ApplyOracleMaster(name, category, null, gov, city, area, address, collectorRepId);
                _labs.Add(lab);
            }
            lab.ClearDomainEvents();
            up++;
        }
        var deact = 0;
        foreach (var lab in existing.Values.Where(l =>
            l.Source == RecordSource.Oracle && !seen.Contains(l.Code.Value) && l.Status != LaboratoryStatus.Inactive))
        {
            lab.ChangeStatus(LaboratoryStatus.Inactive);
            lab.ClearDomainEvents();
            deact++;
        }
        return (up, deact);
    }

    private static string? Lookup(Dictionary<string, string> map, string? code) =>
        !string.IsNullOrWhiteSpace(code) && map.TryGetValue(code, out var v) ? v : null;

    private static string? Str(OracleRow row, string col) =>
        row.Values.TryGetValue(col, out var v) && v is not null ? Convert.ToString(v) : null;
    private static int Int(OracleRow row, string col) =>
        row.Values.TryGetValue(col, out var v) && v is not null ? Convert.ToInt32(v) : 0;
    private static decimal Dec(OracleRow row, string col) =>
        row.Values.TryGetValue(col, out var v) && v is not null ? Convert.ToDecimal(v) : 0m;

    /// <summary>
    /// Runs ONLY the TestStats feed over an explicit inclusive date range and upserts the results into
    /// <c>TestStatistic</c> (add-to-existing, idempotent by date+code). Drives the nightly "yesterday" job and
    /// the Test Statistics page button. Does not touch the shared config's last-sync timestamp, so the general
    /// hourly sync's due-gate is unaffected.
    /// </summary>
    public async Task<OracleSyncResult> RunTestStatsAsync(DateOnly from, DateOnly to, bool manual, CancellationToken ct)
    {
        var config = await _configRepo.GetAsync(ct);
        if (config is null || !config.Enabled)
            return new OracleSyncResult(false, "disabled", 0, 0);
        if (to < from) (from, to) = (to, from);

        // Half-open window for the SQL: [from 00:00, (to + 1 day) 00:00).
        var window = new OracleDateWindow(
            from.ToDateTime(TimeOnly.MinValue),
            to.AddDays(1).ToDateTime(TimeOnly.MinValue));
        var rows = await _reader.ExecuteAsync("TestStats", window, ct);

        var upserted = await UpsertTestStatsAsync(rows, from, to, ct);
        await _db.SaveChangesAsync(ct); // stat mutations audited by the interceptor (JOBS-002)

        _logger.LogInformation("TestStats sync ({Mode}) {From:yyyy-MM-dd}..{To:yyyy-MM-dd}: {Rows} rows, {Upserted} upserted",
            manual ? "manual" : "scheduled", from, to, rows.Count, upserted);

        return new OracleSyncResult(true, "ok", LabsUpserted: 0, StatsUpserted: upserted,
            Upserts: new Dictionary<string, int> { ["TestStats"] = upserted });
    }

    /// <summary>
    /// Upserts per-test daily statistics (date, test code) from the TestStats feed. Bulk-loads the existing rows
    /// in the range once (one query, not one-per-row) so a multi-year backfill stays a single scan + in-memory diff.
    /// </summary>
    private async Task<int> UpsertTestStatsAsync(IReadOnlyList<OracleRow> rows, DateOnly from, DateOnly to, CancellationToken ct)
    {
        if (rows.Count == 0) return 0;
        var existing = (await _testStats.GetRangeAsync(from, to, ct))
            .ToDictionary(s => (s.Date, s.TestCode, s.TestType));
        var seen = new HashSet<(DateOnly, string, int)>();
        var upserted = 0;
        foreach (var row in rows)
        {
            var v = row.Values;
            if (!v.TryGetValue("THE_DATE", out var dateObj) || dateObj is null)
                continue;
            var code = (v.TryGetValue("TEST_CODE", out var codeObj) ? Convert.ToString(codeObj) : null)?.Trim();
            if (string.IsNullOrWhiteSpace(code))
                continue;

            var date = DateOnly.FromDateTime(Convert.ToDateTime(dateObj));
            var testType = v.TryGetValue("TEST_TYPE", out var typeObj) && typeObj is not null ? Convert.ToInt32(typeObj) : 0;
            var count = v.TryGetValue("TEST_COUNT", out var cntObj) && cntObj is not null ? Convert.ToInt32(cntObj) : 0;
            var incomeAmount = v.TryGetValue("TEST_INCOME", out var incObj) && incObj is not null ? Convert.ToDecimal(incObj) : 0m;
            var income = new Money(incomeAmount < 0m ? 0m : incomeAmount);
            var testCode = code.ToUpperInvariant();

            var key = (date, testCode, testType);
            if (existing.TryGetValue(key, out var stat))
            {
                stat.SetCount(count);
                stat.SetIncome(income);
            }
            else
            {
                stat = TestStatistic.For(date, testCode, testType);
                stat.SetCount(count);
                stat.SetIncome(income);
                _testStats.Add(stat);
                existing[key] = stat; // guard against duplicate keys in one batch
            }
            seen.Add(key);
            upserted++;
        }

        // Clear any rows in the window Oracle no longer reports — crucially the legacy pre-type rows keyed by
        // (date, code, type=0) that merged two tests under one code. Re-syncing a window replaces it wholesale,
        // so a collided code splits into its per-type rows with no double-counting or stale leftovers.
        foreach (var stale in existing.Values.Where(s => !seen.Contains((s.Date, s.TestCode, s.TestType))))
            _testStats.Remove(stale);

        return upserted;
    }

    /// <summary>
    /// Runs ONLY the LabStats feed over an explicit inclusive date range and upserts the results into
    /// <c>DailyLabStatistic</c> (add-to-existing, idempotent by date+lab code). Drives the nightly "yesterday"
    /// job and the Lab Statistics page button. Like the test-stats path, it leaves the shared config's
    /// last-sync timestamp untouched so the general hourly sync's due-gate is unaffected.
    /// </summary>
    public async Task<OracleSyncResult> RunLabStatsAsync(DateOnly from, DateOnly to, bool manual, CancellationToken ct)
    {
        var config = await _configRepo.GetAsync(ct);
        if (config is null || !config.Enabled)
            return new OracleSyncResult(false, "disabled", 0, 0);
        if (to < from) (from, to) = (to, from);

        var window = new OracleDateWindow(
            from.ToDateTime(TimeOnly.MinValue),
            to.AddDays(1).ToDateTime(TimeOnly.MinValue));
        var rows = await _reader.ExecuteAsync("LabStats", window, ct);

        var upserted = await UpsertLabStatsAsync(rows, from, to, ct);
        await _db.SaveChangesAsync(ct); // stat mutations audited by the interceptor (JOBS-002)

        // Re-derive every lab's lifecycle status from the (now updated) full statistics history.
        var restatused = await DeriveLabStatusesAsync(ct);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("LabStats sync ({Mode}) {From:yyyy-MM-dd}..{To:yyyy-MM-dd}: {Rows} rows, {Upserted} upserted, {Restatused} labs restatused",
            manual ? "manual" : "scheduled", from, to, rows.Count, upserted, restatused);

        return new OracleSyncResult(true, "ok", LabsUpserted: upserted, StatsUpserted: 0,
            Upserts: new Dictionary<string, int> { ["LabStats"] = upserted, ["LabStatus"] = restatused });
    }

    /// <summary>
    /// Runs the DetailedStats feed over an inclusive date range and replaces the synced transaction-level rows for
    /// that window (delete the range, then bulk-insert the freshly read lines). Idempotent per window.
    /// </summary>
    public async Task<OracleSyncResult> RunDetailedStatsAsync(DateOnly from, DateOnly to, bool manual, CancellationToken ct)
    {
        var config = await _configRepo.GetAsync(ct);
        if (config is null || !config.Enabled)
            return new OracleSyncResult(false, "disabled", 0, 0);
        if (to < from) (from, to) = (to, from);

        var window = new OracleDateWindow(
            from.ToDateTime(TimeOnly.MinValue),
            to.AddDays(1).ToDateTime(TimeOnly.MinValue));
        var rows = await _reader.ExecuteAsync("DetailedStats", window, ct);

        // Window replace: clear the range, then insert the freshly read lines.
        await _detailed.DeleteRangeAsync(from, to, ct);
        var mapped = new List<DetailedRegistration>(rows.Count);
        foreach (var row in rows)
        {
            if (!row.Values.TryGetValue("REG_DT", out var dObj) || dObj is null) continue;
            var date = DateOnly.FromDateTime(Convert.ToDateTime(dObj));
            mapped.Add(DetailedRegistration.Create(
                date, Str(row, "LAB_CODE"), Str(row, "ACC_NO"), Str(row, "PATIENT_NAME"),
                Str(row, "TEST_CODE"), Int(row, "TEST_TYPE"), Str(row, "TEST_NAME"),
                Dec(row, "PATIENT_FEE"), Dec(row, "INSURANCE_FEE")));
        }
        _detailed.AddRange(mapped);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("DetailedStats sync ({Mode}) {From:yyyy-MM-dd}..{To:yyyy-MM-dd}: {Rows} lines",
            manual ? "manual" : "scheduled", from, to, mapped.Count);
        return new OracleSyncResult(true, "ok", LabsUpserted: 0, StatsUpserted: mapped.Count,
            Upserts: new Dictionary<string, int> { ["DetailedStats"] = mapped.Count });
    }

    /// <summary>
    /// Re-derives every lab's lifecycle status from its statistics history (per the operator's rules):
    /// no records → Pending; a single record within the last 7 days → Interactive; any data in the last
    /// 7 days → Active; else data within 30 days → Inactive; else (only older data) → Stopped. Overwrites
    /// the current status regardless of what it was. Domain events are cleared to avoid flooding the outbox
    /// on a bulk change across ~13k labs. Returns the number of labs whose status actually changed.
    /// </summary>
    private async Task<int> DeriveLabStatusesAsync(CancellationToken ct)
    {
        var today = _clock.CairoToday;
        var last7 = today.AddDays(-7);
        var last30 = today.AddDays(-30);

        // One grouped scan of the stats table keyed by (uppercased) lab code, matching Laboratory.Code.
        var stats = await _db.DailyLabStatistics
            .GroupBy(s => s.LabCode)
            .Select(g => new
            {
                LabCode = g.Key,
                Total = g.Count(),
                Cnt7 = g.Count(x => x.Date >= last7),
                Has30 = g.Any(x => x.Date >= last30),
            })
            .ToDictionaryAsync(x => x.LabCode, ct);

        var changed = 0;
        foreach (var lab in await _labs.GetAllAsync(ct))
        {
            LaboratoryStatus target;
            if (!stats.TryGetValue(lab.Code.Value, out var s) || s.Total == 0)
                target = LaboratoryStatus.Pending;
            else if (s.Cnt7 > 0)
                target = s.Total == 1 ? LaboratoryStatus.Interactive : LaboratoryStatus.Active;
            else if (s.Has30)
                target = LaboratoryStatus.Inactive;
            else
                target = LaboratoryStatus.Stopped;

            if (lab.Status != target)
            {
                lab.ChangeStatus(target);
                lab.ClearDomainEvents();
                changed++;
            }
        }
        return changed;
    }

    /// <summary>
    /// Upserts per-lab daily statistics (date, lab code) from the LabStats feed. Aggregates the doctor-grained
    /// Oracle rows to the lab grain in memory, then bulk-loads the existing rows in the range once (one query,
    /// not one-per-row) so a multi-year backfill stays a single scan + in-memory diff.
    /// </summary>
    private async Task<int> UpsertLabStatsAsync(IReadOnlyList<OracleRow> rows, DateOnly from, DateOnly to, CancellationToken ct)
    {
        if (rows.Count == 0) return 0;

        // Fold Oracle rows to (date, lab code): the query is already lab-grained, but guard against any
        // duplicate keys (e.g. duplicate doctor names) by summing.
        var agg = new Dictionary<(DateOnly, string), (int reg, int test, decimal income)>();
        foreach (var row in rows)
        {
            var v = row.Values;
            if (!v.TryGetValue("THE_DATE", out var dateObj) || dateObj is null)
                continue;
            var code = (v.TryGetValue("LAB_CODE", out var codeObj) ? Convert.ToString(codeObj) : null)?.Trim();
            if (string.IsNullOrWhiteSpace(code))
                continue;

            var date = DateOnly.FromDateTime(Convert.ToDateTime(dateObj));
            var labCode = code.ToUpperInvariant();
            var reg = v.TryGetValue("REG_COUNT", out var rc) && rc is not null ? Convert.ToInt32(rc) : 0;
            var test = v.TryGetValue("TEST_COUNT", out var tc) && tc is not null ? Convert.ToInt32(tc) : 0;
            var inc = v.TryGetValue("INCOME", out var ic) && ic is not null ? Convert.ToDecimal(ic) : 0m;

            var key = (date, labCode);
            var cur = agg.TryGetValue(key, out var x) ? x : default;
            agg[key] = (cur.reg + reg, cur.test + test, cur.income + (inc < 0m ? 0m : inc));
        }

        var existing = (await _labStats.GetRangeAsync(from, to, ct))
            .ToDictionary(s => (s.Date, s.LabCode));
        var upserted = 0;
        foreach (var ((date, labCode), val) in agg)
        {
            var income = new Money(val.income);
            if (existing.TryGetValue((date, labCode), out var stat))
                stat.Set(val.reg, val.test, income);
            else
            {
                stat = DailyLabStatistic.For(date, labCode);
                stat.Set(val.reg, val.test, income);
                _labStats.Add(stat);
            }
            upserted++;
        }

        // Clear any (date, lab code) rows Oracle no longer reports for this window. After the doctor→lab resolution
        // collapses duplicate lab codes onto one (MIN), the dropped codes' prior rows would otherwise linger and
        // inflate totals; likewise a lab that stops appearing on a day should not keep a stale row. Re-syncing a
        // window replaces it wholesale (mirrors the TestStats upsert).
        foreach (var stale in existing.Values.Where(s => !agg.ContainsKey((s.Date, s.LabCode))))
            _labStats.Remove(stale);

        return upserted;
    }
}
