using FollowUp.Application.Common.Abstractions.Persistence;
using FollowUp.Domain.Common;
using FollowUp.Domain.Compensation;
using FollowUp.Domain.Integration;
using FollowUp.Domain.Laboratories;
using FollowUp.Domain.Notifications;
using FollowUp.Domain.Reference;
using FollowUp.Domain.Representatives;
using FollowUp.Domain.Signatures;
using FollowUp.Domain.Statistics;
using Microsoft.EntityFrameworkCore;

namespace FollowUp.Infrastructure.Persistence.Repositories;

// ---- Statistics / catalogue ----

internal sealed class DailyLabStatisticRepository : IDailyLabStatisticRepository
{
    private readonly FollowUpDbContext _db;
    public DailyLabStatisticRepository(FollowUpDbContext db) => _db = db;
    public Task<DailyLabStatistic?> GetAsync(DateOnly date, string labCode, CancellationToken ct) =>
        _db.DailyLabStatistics.FirstOrDefaultAsync(x => x.Date == date && x.LabCode == labCode, ct);
    public async Task<IReadOnlyList<DailyLabStatistic>> GetRangeAsync(DateOnly from, DateOnly to, CancellationToken ct) =>
        await _db.DailyLabStatistics.Where(x => x.Date >= from && x.Date <= to).ToListAsync(ct);
    public void Add(DailyLabStatistic stat) => _db.DailyLabStatistics.Add(stat);
}

internal sealed class TestStatisticRepository : ITestStatisticRepository
{
    private readonly FollowUpDbContext _db;
    public TestStatisticRepository(FollowUpDbContext db) => _db = db;
    public Task<TestStatistic?> GetAsync(DateOnly date, string testCode, CancellationToken ct) =>
        _db.TestStatistics.FirstOrDefaultAsync(x => x.Date == date && x.TestCode == testCode, ct);
    public async Task<IReadOnlyList<TestStatistic>> GetRangeAsync(DateOnly from, DateOnly to, CancellationToken ct) =>
        await _db.TestStatistics.Where(x => x.Date >= from && x.Date <= to).ToListAsync(ct);
    public void Add(TestStatistic stat) => _db.TestStatistics.Add(stat);
}

internal sealed class TestGroupRepository : ITestGroupRepository
{
    private readonly FollowUpDbContext _db;
    public TestGroupRepository(FollowUpDbContext db) => _db = db;
    public Task<TestGroup?> GetByIdAsync(TestGroupId id, CancellationToken ct) =>
        _db.TestGroups.FirstOrDefaultAsync(x => x.Id == id, ct);
    public Task<TestGroup?> GetByCodeAsync(string code, CancellationToken ct) =>
        _db.TestGroups.FirstOrDefaultAsync(x => x.Code == code, ct);
    public async Task<IReadOnlyList<TestGroup>> GetAllAsync(CancellationToken ct) =>
        await _db.TestGroups.ToListAsync(ct);
    public void Add(TestGroup group) => _db.TestGroups.Add(group);
    public void Remove(TestGroup group) => _db.TestGroups.Remove(group);
}

internal sealed class TestSetupRepository : ITestSetupRepository
{
    private readonly FollowUpDbContext _db;
    public TestSetupRepository(FollowUpDbContext db) => _db = db;
    public Task<TestSetup?> GetByIdAsync(TestSetupId id, CancellationToken ct) =>
        _db.TestSetups.FirstOrDefaultAsync(x => x.Id == id, ct);
    public Task<TestSetup?> GetByCodeAsync(string code, int testType, CancellationToken ct) =>
        _db.TestSetups.FirstOrDefaultAsync(x => x.Code == code && x.TestType == testType, ct);
    public async Task<IReadOnlyList<TestSetup>> GetByGroupAsync(TestGroupId groupId, CancellationToken ct) =>
        await _db.TestSetups.Where(x => x.GroupId == groupId).ToListAsync(ct);
    public async Task<IReadOnlyList<TestSetup>> GetAllAsync(CancellationToken ct) =>
        await _db.TestSetups.ToListAsync(ct);
    public void Add(TestSetup setup) => _db.TestSetups.Add(setup);
    public void Remove(TestSetup setup) => _db.TestSetups.Remove(setup);
}

// ---- Compensation ----

internal sealed class LabLoyaltyLedgerRepository : ILabLoyaltyLedgerRepository
{
    private readonly FollowUpDbContext _db;
    public LabLoyaltyLedgerRepository(FollowUpDbContext db) => _db = db;
    public Task<LabLoyaltyLedger?> GetAsync(LaboratoryId labId, YearMonth period, CancellationToken ct) =>
        _db.LoyaltyLedgers.FirstOrDefaultAsync(x => x.LaboratoryId == labId && x.Period == period, ct);
    public void Add(LabLoyaltyLedger ledger) => _db.LoyaltyLedgers.Add(ledger);
}

internal sealed class RepCommissionRepository : IRepCommissionRepository
{
    private readonly FollowUpDbContext _db;
    public RepCommissionRepository(FollowUpDbContext db) => _db = db;
    public Task<RepCommission?> GetAsync(RepresentativeId repId, YearMonth period, CancellationToken ct) =>
        _db.Commissions.FirstOrDefaultAsync(x => x.RepresentativeId == repId && x.Period == period, ct);
    public void Add(RepCommission commission) => _db.Commissions.Add(commission);
}

internal sealed class CompensationConfigRepository : ICompensationConfigRepository
{
    private readonly FollowUpDbContext _db;
    public CompensationConfigRepository(FollowUpDbContext db) => _db = db;
    public Task<CompensationConfig?> GetAsync(CancellationToken ct) =>
        _db.CompensationConfigs.FirstOrDefaultAsync(x => x.Id == CompensationConfig.SingletonId, ct);
    public void Add(CompensationConfig config) => _db.CompensationConfigs.Add(config);
}

// ---- Reference ----

internal sealed class RefItemRepository : IRefItemRepository
{
    private readonly FollowUpDbContext _db;
    public RefItemRepository(FollowUpDbContext db) => _db = db;
    public Task<RefItem?> GetByIdAsync(RefItemId id, CancellationToken ct) =>
        _db.RefItems.FirstOrDefaultAsync(x => x.Id == id, ct);
    public Task<bool> ExistsAsync(RefType type, string code, CancellationToken ct) =>
        _db.RefItems.AnyAsync(x => x.Type == type && x.Code.ToLower() == code.ToLower(), ct);
    public async Task<IReadOnlyList<RefItem>> GetByTypeAsync(RefType type, CancellationToken ct) =>
        await _db.RefItems.Where(x => x.Type == type).ToListAsync(ct);
    public void Add(RefItem item) => _db.RefItems.Add(item);
    public void Remove(RefItem item) => _db.RefItems.Remove(item);
}

internal sealed class CityRepository : ICityRepository
{
    private readonly FollowUpDbContext _db;
    public CityRepository(FollowUpDbContext db) => _db = db;
    public Task<City?> GetByIdAsync(CityId id, CancellationToken ct) =>
        _db.Cities.FirstOrDefaultAsync(x => x.Id == id, ct);
    public async Task<IReadOnlyList<City>> GetAllAsync(CancellationToken ct) =>
        await _db.Cities.ToListAsync(ct);
    public void Add(City city) => _db.Cities.Add(city);
    public void Remove(City city) => _db.Cities.Remove(city);
}

internal sealed class AreaRepository : IAreaRepository
{
    private readonly FollowUpDbContext _db;
    public AreaRepository(FollowUpDbContext db) => _db = db;
    public Task<Area?> GetByIdAsync(AreaId id, CancellationToken ct) =>
        _db.Areas.FirstOrDefaultAsync(x => x.Id == id, ct);
    public async Task<IReadOnlyList<Area>> GetAllAsync(CancellationToken ct) =>
        await _db.Areas.ToListAsync(ct);
    public void Add(Area area) => _db.Areas.Add(area);
    public void Remove(Area area) => _db.Areas.Remove(area);
}

internal sealed class AppSettingRepository : IAppSettingRepository
{
    private readonly FollowUpDbContext _db;
    public AppSettingRepository(FollowUpDbContext db) => _db = db;
    public Task<AppSetting?> GetAsync(string key, CancellationToken ct) =>
        _db.Settings.FirstOrDefaultAsync(x => x.Id == key, ct);
    public void Add(AppSetting setting) => _db.Settings.Add(setting);
}

// ---- Notifications / integration / signatures ----

internal sealed class SystemNotificationRepository : ISystemNotificationRepository
{
    private readonly FollowUpDbContext _db;
    public SystemNotificationRepository(FollowUpDbContext db) => _db = db;
    public Task<SystemNotification?> GetByIdAsync(SystemNotificationId id, CancellationToken ct) =>
        _db.SystemNotifications.FirstOrDefaultAsync(x => x.Id == id, ct);
    public async Task<IReadOnlyList<SystemNotification>> GetUnreadForUserAsync(Domain.Identity.AppUserId userId, CancellationToken ct) =>
        await _db.SystemNotifications.Where(x => x.RecipientUserId == userId && x.ReadAt == null).ToListAsync(ct);
    public void Add(SystemNotification notification) => _db.SystemNotifications.Add(notification);
}

internal sealed class NotificationPreferenceRepository : INotificationPreferenceRepository
{
    private readonly FollowUpDbContext _db;
    public NotificationPreferenceRepository(FollowUpDbContext db) => _db = db;
    public async Task<IReadOnlyList<NotificationPreference>> GetForUserAsync(Domain.Identity.AppUserId userId, CancellationToken ct) =>
        await _db.NotificationPreferences.Where(x => x.UserId == userId).ToListAsync(ct);
    public Task<NotificationPreference?> GetAsync(Domain.Identity.AppUserId userId, string eventKey, CancellationToken ct) =>
        _db.NotificationPreferences.FirstOrDefaultAsync(x => x.UserId == userId && x.EventKey == eventKey, ct);
    public void Add(NotificationPreference preference) => _db.NotificationPreferences.Add(preference);
}

internal sealed class NotificationDeliveryLogRepository : INotificationDeliveryLogRepository
{
    private readonly FollowUpDbContext _db;
    public NotificationDeliveryLogRepository(FollowUpDbContext db) => _db = db;
    public Task<NotificationDeliveryLog?> GetByIdAsync(NotificationDeliveryLogId id, CancellationToken ct) =>
        _db.DeliveryLogs.FirstOrDefaultAsync(x => x.Id == id, ct);
    public void Add(NotificationDeliveryLog log) => _db.DeliveryLogs.Add(log);
}

internal sealed class OracleConfigRepository : IOracleConfigRepository
{
    private readonly FollowUpDbContext _db;
    public OracleConfigRepository(FollowUpDbContext db) => _db = db;
    public Task<OracleConfig?> GetAsync(CancellationToken ct) =>
        _db.OracleConfigs.FirstOrDefaultAsync(x => x.Id == OracleConfig.SingletonId, ct);
    public void Add(OracleConfig config) => _db.OracleConfigs.Add(config);
}

internal sealed class ElectronicSignatureRepository : IElectronicSignatureRepository
{
    private readonly FollowUpDbContext _db;
    public ElectronicSignatureRepository(FollowUpDbContext db) => _db = db;
    public void Add(ElectronicSignature signature) => _db.Signatures.Add(signature);
    public Task<ElectronicSignature?> GetLatestAsync(string module, string recordId, CancellationToken ct) =>
        _db.Signatures.Where(x => x.Module == module && x.RecordId == recordId)
            .OrderByDescending(x => x.SignedAt).FirstOrDefaultAsync(ct);
}
