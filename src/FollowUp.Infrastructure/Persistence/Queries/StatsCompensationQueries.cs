using FollowUp.Application.Features.Compensation;
using FollowUp.Application.Features.LabStats;
using FollowUp.Application.Features.TestCatalogue;
using FollowUp.Domain.Common;
using FollowUp.Domain.Identity;
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
            .Select(l => new { l.Code, l.Name, l.Category, l.Segment, l.Governorate, l.City, l.Area }).ToListAsync(ct))
            .GroupBy(l => l.Code.Value).ToDictionary(g => g.Key, g => g.First());
        return rows.Select(s =>
        {
            labInfo.TryGetValue(s.LabCode, out var l);
            return new LabStatDto(s.Date, s.LabCode, l?.Name, l?.Category, l?.Segment, l?.Governorate, l?.City, l?.Area,
                s.Registrations, s.TestCount, s.Income.Amount);
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

    public async Task<IReadOnlyList<TestGroupDto>> GetGroupsAsync(CancellationToken ct) =>
        await _db.TestGroups.AsNoTracking().OrderBy(g => g.NameEn)
            .Select(g => new TestGroupDto(g.Id.Value, g.Code, g.NameEn, g.NameAr)).ToListAsync(ct);

    public async Task<IReadOnlyList<TestSetupDto>> GetSetupsAsync(CancellationToken ct)
    {
        var rows = await _db.TestSetups.AsNoTracking().OrderBy(s => s.NameEn).ToListAsync(ct);
        return rows.Select(s => new TestSetupDto(s.Id.Value, s.Code, s.NameEn, s.NameAr, s.GroupId != null ? s.GroupId.Value.Value : (Guid?)null)).ToList();
    }

    public async Task<IReadOnlyList<TestStatDto>> GetTestStatsAsync(DateOnly from, DateOnly to, CancellationToken ct)
    {
        var rows = await _db.TestStatistics.AsNoTracking().Where(t => t.Date >= from && t.Date <= to)
            .OrderBy(t => t.Date).ThenBy(t => t.TestCode).ToListAsync(ct);

        // Enrich with test setup names and parent group names by code (in memory — small catalogue).
        var setups = (await _db.TestSetups.AsNoTracking()
            .Select(s => new { s.Code, s.NameEn, s.GroupId }).ToListAsync(ct))
            .GroupBy(s => s.Code).ToDictionary(g => g.Key, g => g.First());
        var groups = (await _db.TestGroups.AsNoTracking()
            .Select(g => new { g.Id, g.NameEn }).ToListAsync(ct))
            .ToDictionary(g => g.Id, g => g.NameEn);

        return rows.Select(t =>
        {
            setups.TryGetValue(t.TestCode, out var s);
            string? groupName = null;
            if (s?.GroupId is { } gid) groups.TryGetValue(gid, out groupName);
            return new TestStatDto(t.Date, t.TestCode, s?.NameEn, groupName, t.Count, t.Income.Amount);
        }).ToList();
    }
}

internal sealed class CompensationQueries : ICompensationQueries
{
    private readonly FollowUpDbContext _db;
    public CompensationQueries(FollowUpDbContext db) => _db = db;

    public async Task<IReadOnlyList<LoyaltyLedgerDto>> GetLedgersAsync(int period, OrgScope scope, CancellationToken ct)
    {
        var ym = YearMonth.FromCode(period);
        var scopedLabs = _db.Laboratories.ApplyScope(scope).Select(l => l.Id);
        return await _db.LoyaltyLedgers.AsNoTracking()
            .Where(x => x.Period == ym && scopedLabs.Contains(x.LaboratoryId))
            .Select(x => new LoyaltyLedgerDto(x.LaboratoryId.Value, x.Period.Code, x.Target, x.Achieved, x.Points, x.Tier))
            .ToListAsync(ct);
    }

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
        // Scope the ledger read (SRS SCOPE-READ), mirroring GetLedgersAsync: an out-of-scope labId yields an
        // empty history rather than leaking another scope's target/points/tier figures.
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
        // when its attribution falls within scope. Reps carry Branch/Governorate/City/Area (not the lab-only
        // Category/Segment dimensions, which are therefore wildcarded), so the isolation rule is the same
        // OrgScope.Allows used everywhere else; a rep with no attribution is visible only to a global-scoped
        // caller (fail-closed, matching OrgScope.Allows' null semantics). One row per active in-scope rep.
        var ym = YearMonth.FromCode(period);
        var reps = (await _db.Representatives.AsNoTracking().Where(r => r.IsActive).ToListAsync(ct))
            .Where(r => scope.Allows(r.Branch, r.Governorate, r.City, r.Area, OrgScope.Wildcard, OrgScope.Wildcard))
            .ToList();
        var comms = (await _db.Commissions.AsNoTracking().Where(x => x.Period == ym).ToListAsync(ct))
            .ToDictionary(x => x.RepresentativeId);
        return reps.Select(r =>
        {
            comms.TryGetValue(r.Id, out var c);
            return new CommissionDto(r.Id.Value, r.FullName, r.Type.Name, r.GoalType ?? r.GoalDuration.Name, period,
                c?.Target ?? r.Target.Amount, c?.Achieved ?? 0m, c?.BaseSalary.Amount ?? r.Salary.Amount,
                c?.Commission.Amount ?? 0m, c?.Bonus.Amount ?? 0m, c?.Total.Amount ?? r.Salary.Amount, false);
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
