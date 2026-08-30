using FollowUp.Application.Common.Abstractions;
using FollowUp.Domain.Common;
using FollowUp.Domain.Compensation;
using FollowUp.Domain.Identity;
using FollowUp.Domain.Notifications;
using FollowUp.Domain.Reference;
using Microsoft.EntityFrameworkCore;

namespace FollowUp.Infrastructure.Persistence.Seeding;

/// <summary>
/// Idempotent seed of the baseline data a fresh install needs (SRS): the four seeded roles at global scope,
/// a built-in admin login, the six bilingual notification templates, a placeholder compensation config
/// (docs/ASSUMPTIONS.md A3), and a starter set of reference items. Safe to run on every startup — each block
/// only writes when its table is empty.
/// </summary>
public sealed class DatabaseSeeder
{
    private readonly FollowUpDbContext _db;
    private readonly IPasswordHasher _hasher;

    public DatabaseSeeder(FollowUpDbContext db, IPasswordHasher hasher)
    {
        _db = db;
        _hasher = hasher;
    }

    /// <summary>Seeds baseline data. Returns the admin username if a new admin account was created.</summary>
    public async Task<string?> SeedAsync(string adminPassword, CancellationToken ct = default)
    {
        string? createdAdmin = null;

        Role adminRole;
        if (!await _db.Roles.AnyAsync(ct))
        {
            adminRole = Role.Create("Admin", Privileges.All, "en", "light", OrgScope.Global, isBuiltIn: true);
            var opsManager = Role.Create("OperationsManager", new[]
            {
                Privileges.ViewDashboard, Privileges.VerifyDailyFollowup, Privileges.ResolveComplaints,
                Privileges.ViewReports, Privileges.ViewLabLocation, Privileges.SampleTracking,
                Privileges.OutsourceSamples, Privileges.SetupRefs,
            }, "en", "light", OrgScope.Global);
            var collector = Role.Create("Collector", new[] { Privileges.ViewDashboard }, "en", "light", OrgScope.Global);
            var marketing = Role.Create("Marketing", new[] { Privileges.ViewDashboard }, "en", "light", OrgScope.Global);
            _db.Roles.AddRange(adminRole, opsManager, collector, marketing);
        }
        else
        {
            adminRole = await _db.Roles.FirstAsync(r => r.Name == "Admin", ct);
        }

        if (!await _db.Users.AnyAsync(ct))
        {
            var admin = AppUser.Create("admin", _hasher.Hash(adminPassword), adminRole.Id);
            admin.SetProfile("admin@megalab.local", null);
            admin.MarkAsBuiltIn(); // protected from deletion/demotion (IDN-6)
            _db.Users.Add(admin);
            createdAdmin = "admin";
        }

        if (!await _db.NotificationTemplates.AnyAsync(ct))
            _db.NotificationTemplates.AddRange(Templates());

        if (!await _db.CompensationConfigs.AnyAsync(ct))
            _db.CompensationConfigs.Add(CompensationConfig.Create(
                commissionRatePercent: 5m, bonusThresholdPercent: 100m, bonusAmount: new Money(500m),
                tiers: new[]
                {
                    new LoyaltyTier("Bronze", 50m, 100),
                    new LoyaltyTier("Silver", 80m, 250),
                    new LoyaltyTier("Gold", 100m, 500),
                }));

        if (!await _db.RefItems.AnyAsync(ct))
            _db.RefItems.AddRange(ReferenceItems());

        // Segments are configurable reference data (RefType.Segment). Back-fill the A/B/C defaults on any
        // database that predates the feature so existing labs' segments stay valid and are editable.
        if (!await _db.RefItems.AnyAsync(r => r.Type == RefType.Segment, ct))
        {
            var s = 0;
            _db.RefItems.AddRange(new[] { "A", "B", "C" }.Select(x => RefItem.Create(RefType.Segment, x, x, null, s++)));
        }

        await _db.SaveChangesAsync(ct);
        return createdAdmin;
    }

    private static IEnumerable<NotificationTemplate> Templates() => new[]
    {
        NotificationTemplate.Create("complaint.logged",
            "New complaint {reference}", "شكوى جديدة {reference}",
            "A complaint {reference} was logged for {lab}.", "تم تسجيل الشكوى {reference} للمعمل {lab}."),
        NotificationTemplate.Create("complaint.resolved",
            "Complaint {reference} resolved", "تم حل الشكوى {reference}",
            "Complaint {reference} has been resolved.", "تم حل الشكوى {reference}."),
        NotificationTemplate.Create("visit.missed",
            "Missed visit for {lab}", "زيارة فائتة للمعمل {lab}",
            "The scheduled visit for {lab} on {date} was missed.", "لم تتم الزيارة المجدولة للمعمل {lab} بتاريخ {date}."),
        NotificationTemplate.Create("marketing.scheduled",
            "Marketing visit scheduled for {lab}", "زيارة تسويقية مجدولة للمعمل {lab}",
            "A marketing visit for {lab} is scheduled on {date}.", "تم جدولة زيارة تسويقية للمعمل {lab} بتاريخ {date}."),
        NotificationTemplate.Create("pace.alert",
            "Pace alert for {rep}", "تنبيه أداء للمندوب {rep}",
            "{rep} is below the expected attainment pace.", "أداء المندوب {rep} أقل من المستهدف المتوقع."),
        NotificationTemplate.Create("contact.birthday",
            "Birthday reminder: {contact}", "تذكير بعيد ميلاد {contact}",
            "Today is {contact}'s birthday at {lab}.", "اليوم هو عيد ميلاد {contact} في المعمل {lab}."),
    };

    private static IEnumerable<RefItem> ReferenceItems()
    {
        var i = 0;
        RefItem Gov(string code, string en, string ar) => RefItem.Create(RefType.Governorate, code, en, ar, i++);
        RefItem Cat(string code, string en, string ar) => RefItem.Create(RefType.ComplaintCategory, code, en, ar, i++);
        return new[]
        {
            Gov("CAI", "Cairo", "القاهرة"),
            Gov("GIZ", "Giza", "الجيزة"),
            Gov("ALX", "Alexandria", "الإسكندرية"),
            Cat("TAT", "Turnaround time", "زمن الاستجابة"),
            Cat("QLT", "Result quality", "جودة النتائج"),
            Cat("SVC", "Service", "الخدمة"),
        };
    }
}
