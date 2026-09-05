using System.Reflection;
using FollowUp.Domain.Audit;
using FollowUp.Domain.Compensation;
using FollowUp.Domain.Complaints;
using FollowUp.Domain.Emailing;
using FollowUp.Domain.Identity;
using FollowUp.Domain.Integration;
using FollowUp.Domain.Laboratories;
using FollowUp.Domain.Marketing;
using FollowUp.Domain.Notifications;
using FollowUp.Domain.Operations;
using FollowUp.Domain.Reference;
using FollowUp.Domain.Representatives;
using FollowUp.Domain.Signatures;
using FollowUp.Domain.Statistics;
using Microsoft.EntityFrameworkCore;

namespace FollowUp.Infrastructure.Persistence;

/// <summary>
/// The EF Core unit of work (ADR-0005: the DbContext *is* the UoW — no wrapper). Maps all 31 tables via
/// per-aggregate <c>IEntityTypeConfiguration</c>s and registers strongly-typed-id / Enumeration / value-object
/// converters as conventions so configurations stay declarative.
/// </summary>
public sealed class FollowUpDbContext : DbContext
{
    public FollowUpDbContext(DbContextOptions<FollowUpDbContext> options) : base(options) { }

    // Identity / audit / signatures
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<UserSession> Sessions => Set<UserSession>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
    public DbSet<ElectronicSignature> Signatures => Set<ElectronicSignature>();

    // Core business
    public DbSet<Laboratory> Laboratories => Set<Laboratory>();
    public DbSet<Representative> Representatives => Set<Representative>();

    // Operations
    public DbSet<DailyVisit> DailyVisits => Set<DailyVisit>();
    public DbSet<VisitHistory> VisitHistory => Set<VisitHistory>();
    public DbSet<OutsourceSample> OutsourceSamples => Set<OutsourceSample>();
    public DbSet<SampleTracking> SampleTracking => Set<SampleTracking>();
    public DbSet<MarketingVisit> MarketingVisits => Set<MarketingVisit>();
    public DbSet<Complaint> Complaints => Set<Complaint>();

    // Statistics / catalogue
    public DbSet<MonthlySample> MonthlySamples => Set<MonthlySample>();
    public DbSet<DailyLabStatistic> DailyLabStatistics => Set<DailyLabStatistic>();
    public DbSet<TestStatistic> TestStatistics => Set<TestStatistic>();
    public DbSet<DetailedRegistration> DetailedRegistrations => Set<DetailedRegistration>();
    public DbSet<TestGroup> TestGroups => Set<TestGroup>();
    public DbSet<TestSetup> TestSetups => Set<TestSetup>();

    // Compensation
    public DbSet<LabLoyaltyLedger> LoyaltyLedgers => Set<LabLoyaltyLedger>();
    public DbSet<RepCommission> Commissions => Set<RepCommission>();
    public DbSet<CompensationConfig> CompensationConfigs => Set<CompensationConfig>();

    // Reference
    public DbSet<RefItem> RefItems => Set<RefItem>();
    public DbSet<City> Cities => Set<City>();
    public DbSet<Area> Areas => Set<Area>();
    public DbSet<AppSetting> Settings => Set<AppSetting>();

    // Notifications / integration
    public DbSet<NotificationTemplate> NotificationTemplates => Set<NotificationTemplate>();
    public DbSet<NotificationPreference> NotificationPreferences => Set<NotificationPreference>();
    public DbSet<SystemNotification> SystemNotifications => Set<SystemNotification>();
    public DbSet<NotificationDeliveryLog> DeliveryLogs => Set<NotificationDeliveryLog>();
    public DbSet<OracleConfig> OracleConfigs => Set<OracleConfig>();
    public DbSet<SmtpConfig> SmtpConfigs => Set<SmtpConfig>();
    public DbSet<StatsEmailSubscription> StatsEmailSubscriptions => Set<StatsEmailSubscription>();

    // Infrastructure
    public DbSet<Outbox.OutboxMessage> OutboxMessages => Set<Outbox.OutboxMessage>();
    public DbSet<Idempotency.IdempotencyRecord> IdempotencyRecords => Set<Idempotency.IdempotencyRecord>();

    protected override void ConfigureConventions(ModelConfigurationBuilder c)
    {
        // Value objects
        c.Properties<Domain.Common.Money>().HaveConversion<MoneyConverter>().HavePrecision(18, 2);
        c.Properties<Domain.Common.YearMonth>().HaveConversion<YearMonthConverter>();

        // Enumerations (persist stable Name)
        c.Properties<VisitStatus>().HaveConversion<EnumerationConverter<VisitStatus>>().HaveMaxLength(32);
        c.Properties<OutsourceStatus>().HaveConversion<EnumerationConverter<OutsourceStatus>>().HaveMaxLength(32);
        c.Properties<ComplaintStatus>().HaveConversion<EnumerationConverter<ComplaintStatus>>().HaveMaxLength(32);
        c.Properties<ComplaintStage>().HaveConversion<EnumerationConverter<ComplaintStage>>().HaveMaxLength(32);
        c.Properties<MarketingVisitStatus>().HaveConversion<EnumerationConverter<MarketingVisitStatus>>().HaveMaxLength(32);
        c.Properties<MarketingPurpose>().HaveConversion<EnumerationConverter<MarketingPurpose>>().HaveMaxLength(32);
        c.Properties<LaboratoryStatus>().HaveConversion<EnumerationConverter<LaboratoryStatus>>().HaveMaxLength(32);
        c.Properties<RepresentativeType>().HaveConversion<EnumerationConverter<RepresentativeType>>().HaveMaxLength(32);
        c.Properties<GoalDuration>().HaveConversion<EnumerationConverter<GoalDuration>>().HaveMaxLength(32);
        c.Properties<SignatureMeaning>().HaveConversion<EnumerationConverter<SignatureMeaning>>().HaveMaxLength(32);
        c.Properties<RefType>().HaveConversion<EnumerationConverter<RefType>>().HaveMaxLength(32);
        c.Properties<NotificationChannel>().HaveConversion<EnumerationConverter<NotificationChannel>>().HaveMaxLength(32);

        // Strongly-typed ids
        c.Properties<AppUserId>().HaveConversion<AppUserIdConverter>();
        c.Properties<RoleId>().HaveConversion<RoleIdConverter>();
        c.Properties<UserSessionId>().HaveConversion<UserSessionIdConverter>();
        c.Properties<AuditEntryId>().HaveConversion<AuditEntryIdConverter>();
        c.Properties<ElectronicSignatureId>().HaveConversion<ElectronicSignatureIdConverter>();
        c.Properties<LaboratoryId>().HaveConversion<LaboratoryIdConverter>();
        c.Properties<ContactPersonId>().HaveConversion<ContactPersonIdConverter>();
        c.Properties<RepresentativeId>().HaveConversion<RepresentativeIdConverter>();
        c.Properties<DailyVisitId>().HaveConversion<DailyVisitIdConverter>();
        c.Properties<VisitHistoryId>().HaveConversion<VisitHistoryIdConverter>();
        c.Properties<OutsourceSampleId>().HaveConversion<OutsourceSampleIdConverter>();
        c.Properties<SampleTrackingId>().HaveConversion<SampleTrackingIdConverter>();
        c.Properties<MarketingVisitId>().HaveConversion<MarketingVisitIdConverter>();
        c.Properties<ComplaintId>().HaveConversion<ComplaintIdConverter>();
        c.Properties<MonthlySampleId>().HaveConversion<MonthlySampleIdConverter>();
        c.Properties<DailyLabStatisticId>().HaveConversion<DailyLabStatisticIdConverter>();
        c.Properties<DetailedRegistrationId>().HaveConversion<DetailedRegistrationIdConverter>();
        c.Properties<TestStatisticId>().HaveConversion<TestStatisticIdConverter>();
        c.Properties<TestGroupId>().HaveConversion<TestGroupIdConverter>();
        c.Properties<TestSetupId>().HaveConversion<TestSetupIdConverter>();
        c.Properties<LabLoyaltyLedgerId>().HaveConversion<LabLoyaltyLedgerIdConverter>();
        c.Properties<RepCommissionId>().HaveConversion<RepCommissionIdConverter>();
        c.Properties<RefItemId>().HaveConversion<RefItemIdConverter>();
        c.Properties<CityId>().HaveConversion<CityIdConverter>();
        c.Properties<AreaId>().HaveConversion<AreaIdConverter>();
        c.Properties<NotificationTemplateId>().HaveConversion<NotificationTemplateIdConverter>();
        c.Properties<NotificationPreferenceId>().HaveConversion<NotificationPreferenceIdConverter>();
        c.Properties<SystemNotificationId>().HaveConversion<SystemNotificationIdConverter>();
        c.Properties<NotificationDeliveryLogId>().HaveConversion<NotificationDeliveryLogIdConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
        base.OnModelCreating(modelBuilder);
    }
}
