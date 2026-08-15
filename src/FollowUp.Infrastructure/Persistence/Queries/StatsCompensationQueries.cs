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
        return rows.Select(s => new LabStatDto(s.Date, s.LabCode, s.Registrations, s.TestCount, s.Income.Amount)).ToList();
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

    public async Task<IReadOnlyList<TestStatDto>> GetTestStatsAsync(DateOnly from, DateOnly to, CancellationToken ct) =>
        await _db.TestStatistics.AsNoTracking().Where(t => t.Date >= from && t.Date <= to)
            .OrderBy(t => t.Date).ThenBy(t => t.TestCode)
            .Select(t => new TestStatDto(t.Date, t.TestCode, t.Count)).ToListAsync(ct);
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

    public async Task<IReadOnlyList<LoyaltyLedgerDto>> GetLabLedgerAsync(Guid labId, CancellationToken ct) =>
        await _db.LoyaltyLedgers.AsNoTracking()
            .Where(x => x.LaboratoryId == new Domain.Laboratories.LaboratoryId(labId))
            .OrderByDescending(x => x.Period)
            .Select(x => new LoyaltyLedgerDto(x.LaboratoryId.Value, x.Period.Code, x.Target, x.Achieved, x.Points, x.Tier))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<CommissionDto>> GetCommissionsAsync(int period, OrgScope scope, CancellationToken ct)
    {
        // Commissions are org-wide aggregates (SCOPE-READ decision: documented as org-wide, not lab-scoped).
        var ym = YearMonth.FromCode(period);
        var rows = await _db.Commissions.AsNoTracking().Where(x => x.Period == ym).ToListAsync(ct);
        return rows.Select(x => new CommissionDto(
            x.RepresentativeId.Value, x.Period.Code, x.Target, x.Achieved,
            x.BaseSalary.Amount, x.Commission.Amount, x.Bonus.Amount, x.Total.Amount)).ToList();
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
