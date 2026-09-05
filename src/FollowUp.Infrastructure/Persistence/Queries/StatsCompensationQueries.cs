using FollowUp.Application.Features.AreaStats;
using FollowUp.Application.Features.Compensation;
using FollowUp.Application.Features.DetailedStats;
using FollowUp.Application.Features.LabStats;
using FollowUp.Application.Features.TestCatalogue;
using FollowUp.Domain.Common;
using FollowUp.Domain.Identity;
using FollowUp.Domain.Reference;
using Microsoft.EntityFrameworkCore;

namespace FollowUp.Infrastructure.Persistence.Queries;

internal sealed class LabStatsQueries : ILabStatsQueries
{
    private readonly FollowUpDbContext _db;
    public LabStatsQueries(FollowUpDbContext db) => _db = db;

    public async Task<IReadOnlyList<LabStatDto>> ListAsync(DateOnly from, DateOnly to, OrgScope scope, CancellationToken ct)
    {
        var q = _db.DailyLabStatistics.AsNoTracking().Where(s => s.Date >= from && s.Date <= to);

        // Scope by lab code (stats key on code). Skip when scope is global.
        if (!IsGlobal(scope))
        {
            var allowedCodes = (await _db.Laboratories.ApplyScope(scope).Select(l => l.Code).ToListAsync(ct))
                .Select(c => c.Value).ToList();
            q = q.Where(s => allowedCodes.Contains(s.LabCode));
        }

        var rows = await q.OrderBy(s => s.Date).ThenBy(s => s.LabCode).ToListAsync(ct);

        // Enrich with lab profile (name/segment/location) by code — for the pivot rows.
        var labInfo = (await _db.Laboratories.AsNoTracking()
            .Select(l => new { l.Code, l.Name, l.Category, l.Segment, l.Governorate, l.City, l.Area, l.Status }).ToListAsync(ct))
            .GroupBy(l => l.Code.Value).ToDictionary(g => g.Key, g => g.First());
        return rows.Select(s =>
        {
            labInfo.TryGetValue(s.LabCode, out var l);
            return new LabStatDto(s.Date, s.LabCode, l?.Name, l?.Category, l?.Segment, l?.Governorate, l?.City, l?.Area,
                l?.Status?.Name, s.Registrations, s.TestCount, s.Income.Amount);
        }).ToList();
    }

    private static bool IsGlobal(OrgScope s) =>
        s.Branches.Contains(OrgScope.Wildcard) && s.Governorates.Contains(OrgScope.Wildcard) &&
        s.Cities.Contains(OrgScope.Wildcard) && s.Areas.Contains(OrgScope.Wildcard) &&
        s.Categories.Contains(OrgScope.Wildcard) && s.Segments.Contains(OrgScope.Wildcard);
}

/// <summary>
/// Reads daily lab statistics over a range and rolls them up to the geography grain (date, governorate, city,
/// area) by joining each lab's stamped location. Mirrors <see cref="LabStatsQueries"/>'s scope filtering and
/// code→lab enrichment, then aggregates in memory so the page receives one row per (date, gov, city, area)
/// instead of one per (date, lab). No dedicated area-statistics table exists — the rollup is derived from the
/// same <c>daily_lab_statistic</c> rows the nightly lab-stats sync maintains.
/// </summary>
internal sealed class AreaStatsQueries : IAreaStatsQueries
{
    private readonly FollowUpDbContext _db;
    public AreaStatsQueries(FollowUpDbContext db) => _db = db;

    public async Task<IReadOnlyList<AreaStatDto>> ListAsync(DateOnly from, DateOnly to, OrgScope scope, CancellationToken ct)
    {
        var q = _db.DailyLabStatistics.AsNoTracking().Where(s => s.Date >= from && s.Date <= to);

        // Scope by lab code (stats key on code). Skip when scope is global.
        if (!IsGlobal(scope))
        {
            var allowedCodes = (await _db.Laboratories.ApplyScope(scope).Select(l => l.Code).ToListAsync(ct))
                .Select(c => c.Value).ToList();
            q = q.Where(s => allowedCodes.Contains(s.LabCode));
        }

        // Materialize the stat rows before touching Income.Amount — Money is a converted scalar, so .Amount is
        // only reachable in memory (mirrors LabStatsQueries).
        var rows = await q.ToListAsync(ct);

        // Resolve each lab's stamped geography by code (the geography lives on the lab, not the stats row).
        var geoByCode = (await _db.Laboratories.AsNoTracking()
                .Select(l => new { l.Code, l.Governorate, l.City, l.Area }).ToListAsync(ct))
            .GroupBy(l => l.Code.Value).ToDictionary(g => g.Key, g => g.First());

        // Operator-maintained real names (independent of Oracle sync): governorate by RefItem name, area by name.
        var govRealName = (await _db.RefItems.AsNoTracking()
                .Where(r => r.Type == RefType.Governorate && r.RealName != null)
                .Select(r => new { r.NameEn, r.RealName }).ToListAsync(ct))
            .GroupBy(r => r.NameEn).ToDictionary(g => g.Key, g => g.First().RealName, StringComparer.OrdinalIgnoreCase);
        var areaRealName = (await _db.Areas.AsNoTracking()
                .Where(a => a.RealName != null)
                .Select(a => new { a.Name, a.RealName }).ToListAsync(ct))
            .GroupBy(a => a.Name).ToDictionary(g => g.Key, g => g.First().RealName, StringComparer.OrdinalIgnoreCase);

        // Aggregate to (date, governorate, city, area). Unmapped labs fall into a null bucket the page renders as "—".
        var agg = new Dictionary<(DateOnly, string?, string?, string?), (int test, decimal income)>();
        foreach (var s in rows)
        {
            geoByCode.TryGetValue(s.LabCode, out var g);
            var key = (s.Date, g?.Governorate, g?.City, g?.Area);
            var cur = agg.TryGetValue(key, out var x) ? x : default;
            agg[key] = (cur.test + s.TestCount, cur.income + s.Income.Amount);
        }

        string? GovReal(string? name) => name != null && govRealName.TryGetValue(name, out var v) ? v : null;
        string? AreaReal(string? name) => name != null && areaRealName.TryGetValue(name, out var v) ? v : null;
        return agg
            .Select(kv => new AreaStatDto(kv.Key.Item1, kv.Key.Item2, kv.Key.Item3, kv.Key.Item4,
                GovReal(kv.Key.Item2), AreaReal(kv.Key.Item4), kv.Value.test, kv.Value.income))
            .OrderBy(d => d.Date).ThenBy(d => d.Governorate).ThenBy(d => d.Area)
            .ToList();
    }

    private static bool IsGlobal(OrgScope s) =>
        s.Branches.Contains(OrgScope.Wildcard) && s.Governorates.Contains(OrgScope.Wildcard) &&
        s.Cities.Contains(OrgScope.Wildcard) && s.Areas.Contains(OrgScope.Wildcard) &&
        s.Categories.Contains(OrgScope.Wildcard) && s.Segments.Contains(OrgScope.Wildcard);
}

/// <summary>
/// Reads synced transaction-level registration lines over a range and enriches each with its lab's stamped
/// profile (governorate/city/area/category/branch/name) by lab code. Rows whose lab code is null (no lab) keep a
/// null profile and are excluded when the caller's scope is not global. The page does the grouping/subtotals and
/// the geography/category/branch filtering client-side (mirrors the other stats pages).
/// </summary>
internal sealed class DetailedStatsQueries : IDetailedStatsQueries
{
    private readonly FollowUpDbContext _db;
    public DetailedStatsQueries(FollowUpDbContext db) => _db = db;

    public async Task<IReadOnlyList<DetailedStatDto>> ListAsync(DateOnly from, DateOnly to, OrgScope scope, CancellationToken ct)
    {
        var q = _db.DetailedRegistrations.AsNoTracking().Where(s => s.Date >= from && s.Date <= to);

        if (!IsGlobal(scope))
        {
            var allowedCodes = (await _db.Laboratories.ApplyScope(scope).Select(l => l.Code).ToListAsync(ct))
                .Select(c => c.Value).ToList();
            q = q.Where(s => s.LabCode != null && allowedCodes.Contains(s.LabCode));
        }

        var rows = await q.OrderBy(s => s.Date).ToListAsync(ct);

        var labInfo = (await _db.Laboratories.AsNoTracking()
                .Select(l => new { l.Code, l.Name, l.Category, l.Branch, l.Governorate, l.City, l.Area }).ToListAsync(ct))
            .GroupBy(l => l.Code.Value).ToDictionary(g => g.Key, g => g.First());

        return rows.Select(s =>
        {
            var l = s.LabCode != null && labInfo.TryGetValue(s.LabCode, out var found) ? found : null;
            return new DetailedStatDto(s.Date, l?.Governorate, l?.City, l?.Area, l?.Category, l?.Branch,
                s.LabCode, l?.Name, s.AccNo, s.PatientName, s.TestCode, s.TestType, s.TestName, s.PatientFee + s.InsuranceFee);
        }).ToList();
    }

    private static bool IsGlobal(OrgScope s) =>
        s.Branches.Contains(OrgScope.Wildcard) && s.Governorates.Contains(OrgScope.Wildcard) &&
        s.Cities.Contains(OrgScope.Wildcard) && s.Areas.Contains(OrgScope.Wildcard) &&
        s.Categories.Contains(OrgScope.Wildcard) && s.Segments.Contains(OrgScope.Wildcard);
}

internal sealed class TestCatalogueQueries : ITestCatalogueQueries
{
    private readonly FollowUpDbContext _db;
    public TestCatalogueQueries(FollowUpDbContext db) => _db = db;

    public async Task<IReadOnlyList<TestGroupDto>> GetGroupsAsync(CancellationToken ct)
    {
        var rows = await _db.TestGroups.AsNoTracking().OrderBy(g => g.NameEn).ToListAsync(ct);
        return rows.Select(g => new TestGroupDto(g.Id.Value, g.Code, g.NameEn, g.NameAr, g.Source.ToString())).ToList();
    }

    public async Task<IReadOnlyList<TestSetupDto>> GetSetupsAsync(CancellationToken ct)
    {
        var groups = (await _db.TestGroups.AsNoTracking().Select(g => new { g.Id, g.Code, g.NameEn }).ToListAsync(ct))
            .ToDictionary(g => g.Id, g => (g.Code, g.NameEn));
        var rows = await _db.TestSetups.AsNoTracking().OrderBy(s => s.NameEn).ToListAsync(ct);
        return rows.Select(s =>
        {
            string? groupCode = null, groupName = null;
            if (s.GroupId is { } gid && groups.TryGetValue(gid, out var g)) { groupCode = g.Code; groupName = g.NameEn; }
            return new TestSetupDto(s.Id.Value, s.Code, s.NameEn, s.NameAr,
                s.GroupId != null ? s.GroupId.Value.Value : (Guid?)null,
                s.TestType, s.Cost.Amount, groupCode, groupName, s.Source.ToString());
        }).ToList();
    }

    public async Task<IReadOnlyList<TestStatDto>> GetTestStatsAsync(DateOnly from, DateOnly to, CancellationToken ct)
    {
        var rows = await _db.TestStatistics.AsNoTracking().Where(t => t.Date >= from && t.Date <= to)
            .OrderBy(t => t.Date).ThenBy(t => t.TestCode).ThenBy(t => t.TestType).ToListAsync(ct);

        // Enrich with test setup names and parent group names by (code, type) — the catalogue's natural key,
        // since the same code names different tests across types (e.g. 2542 = "T3 - TOTAL" / "Brucella (Latex)").
        var setups = (await _db.TestSetups.AsNoTracking()
            .Select(s => new { s.Code, s.TestType, s.NameEn, s.GroupId }).ToListAsync(ct))
            .ToDictionary(s => (s.Code, s.TestType));
        var groups = (await _db.TestGroups.AsNoTracking()
            .Select(g => new { g.Id, g.NameEn }).ToListAsync(ct))
            .ToDictionary(g => g.Id, g => g.NameEn);

        return rows.Select(t =>
        {
            setups.TryGetValue((t.TestCode, t.TestType), out var s);
            string? groupName = null;
            if (s?.GroupId is { } gid) groups.TryGetValue(gid, out groupName);
            return new TestStatDto(t.Date, t.TestCode, t.TestType, s?.NameEn, groupName, t.Count, t.Income.Amount);
        }).ToList();
    }
}

internal sealed class CompensationQueries : ICompensationQueries
{
    private readonly FollowUpDbContext _db;
    public CompensationQueries(FollowUpDbContext db) => _db = db;

    public async Task<IReadOnlyList<LoyaltyRowDto>> GetLoyaltySummaryAsync(OrgScope scope, bool canSeeEncrypted, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var thisYm = new YearMonth(today.Year, today.Month);
        var labs = await _db.Laboratories.ApplyScope(scope).AsNoTracking()
            .Select(l => new { l.Id, l.Code, l.IsEncrypted, l.Name, l.Branch, l.City, l.MonthlyTarget, l.LoyaltyPoints, l.LoyaltyTier }).ToListAsync(ct);
        var labIds = labs.Select(l => l.Id).ToList();
        var ms = await _db.MonthlySamples.AsNoTracking().Where(m => labIds.Contains(m.LaboratoryId) && m.Period == thisYm)
            .Select(m => new { m.LaboratoryId, m.SampleCount }).ToListAsync(ct);
        var mtd = ms.GroupBy(x => x.LaboratoryId).ToDictionary(g => g.Key, g => g.Sum(x => x.SampleCount));
        return labs.Select(l => new LoyaltyRowDto(l.Id.Value, DisplayCode.For(l.Code.Value, l.IsEncrypted, canSeeEncrypted), l.Name, l.Branch, l.City,
            l.MonthlyTarget, mtd.TryGetValue(l.Id, out var v) ? v : 0, l.LoyaltyPoints, l.LoyaltyTier)).ToList();
    }

    public async Task<IReadOnlyList<LoyaltyLedgerDto>> GetLabLedgerAsync(Guid labId, OrgScope scope, CancellationToken ct)
    {
        // Scope the ledger read (SRS SCOPE-READ): an out-of-scope labId yields an empty history rather than
        // leaking another scope's target/points/tier figures.
        var scopedLabs = _db.Laboratories.ApplyScope(scope).Select(l => l.Id);
        return await _db.LoyaltyLedgers.AsNoTracking()
            .Where(x => x.LaboratoryId == new Domain.Laboratories.LaboratoryId(labId) && scopedLabs.Contains(x.LaboratoryId))
            .OrderByDescending(x => x.Period)
            .Select(x => new LoyaltyLedgerDto(x.LaboratoryId.Value, x.Period.Code, x.Target, x.Achieved, x.Points, x.Tier))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<CommissionDto>> GetCommissionsAsync(int period, OrgScope scope, CancellationToken ct)
    {
        // Scope commissions to the caller's org scope (SRS SCOPE-READ / finding CPN-2): a rep is visible only
        // when its attribution falls within scope. Reps carry only Branch/Governorate/City/Area (never the
        // lab-only Category/Segment dimensions), so scope them by the geographic-only OrgScope.Allows overload
        // — checking Category/Segment here would deny every rep to a Category/Segment-scoped manager. A rep with
        // no geographic attribution is visible only to a geographically-global caller (fail-closed, matching
        // OrgScope.Allows' null semantics). One row per active in-scope rep.
        var ym = YearMonth.FromCode(period);
        var reps = (await _db.Representatives.AsNoTracking().Where(r => r.IsActive).ToListAsync(ct))
            .Where(r => scope.Allows(r.Branch, r.Governorate, r.City, r.Area))
            .ToList();
        var comms = (await _db.Commissions.AsNoTracking().Where(x => x.Period == ym).ToListAsync(ct))
            .ToDictionary(x => x.RepresentativeId);
        return reps.Select(r =>
        {
            comms.TryGetValue(r.Id, out var c);
            return new CommissionDto(r.Id.Value, r.FullName, r.Type.Name, r.GoalType ?? r.GoalDuration.Name, period,
                c?.Target ?? r.Target.Amount, c?.Achieved ?? 0m, c?.BaseSalary.Amount ?? r.Salary.Amount,
                c?.Commission.Amount ?? 0m, c?.Bonus.Amount ?? 0m, c?.Total.Amount ?? r.Salary.Amount);
        }).ToList();
    }

    public async Task<CompensationConfigDto?> GetConfigAsync(CancellationToken ct)
    {
        var cfg = await _db.CompensationConfigs.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == Domain.Compensation.CompensationConfig.SingletonId, ct);
        if (cfg is null) return null;
        return new CompensationConfigDto(
            cfg.CommissionRatePercent, cfg.BonusThresholdPercent, cfg.BonusAmount.Amount,
            cfg.LoyaltyTiers.Select(t => new LoyaltyTierDto(t.Name, t.MinAchievementPercent, t.Points)).ToList());
    }
}
