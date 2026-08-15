using FollowUp.Domain.Audit;
using FollowUp.Domain.Common;
using FollowUp.Domain.Compensation;
using FollowUp.Domain.Complaints;
using FollowUp.Domain.Identity;
using FollowUp.Domain.Laboratories;
using FollowUp.Domain.Marketing;
using FollowUp.Domain.Notifications;
using FollowUp.Domain.Operations;
using FollowUp.Domain.Reference;
using FollowUp.Domain.Representatives;
using FollowUp.Domain.Signatures;
using FollowUp.Domain.Statistics;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace FollowUp.Infrastructure.Persistence;

/// <summary>Generic converter for any <see cref="Enumeration"/> — persists the stable string <c>Name</c>.</summary>
public sealed class EnumerationConverter<T> : ValueConverter<T, string> where T : Enumeration
{
    public EnumerationConverter() : base(e => e.Name, s => Enumeration.FromName<T>(s)) { }
}

/// <summary>Converts a <see cref="Money"/> value object to/from a <c>numeric(18,2)</c> column.</summary>
public sealed class MoneyConverter : ValueConverter<Money, decimal>
{
    public MoneyConverter() : base(m => m.Amount, d => new Money(d)) { }
}

/// <summary>Converts <see cref="YearMonth"/> to/from its compact <c>yyyymm</c> integer code.</summary>
public sealed class YearMonthConverter : ValueConverter<YearMonth, int>
{
    public YearMonthConverter() : base(ym => ym.Code, code => YearMonth.FromCode(code)) { }
}

// ---- Strongly-typed id converters (record struct wrapping Guid <-> uuid) ----

public sealed class AppUserIdConverter : ValueConverter<AppUserId, Guid> { public AppUserIdConverter() : base(x => x.Value, v => new AppUserId(v)) { } }
public sealed class RoleIdConverter : ValueConverter<RoleId, Guid> { public RoleIdConverter() : base(x => x.Value, v => new RoleId(v)) { } }
public sealed class UserSessionIdConverter : ValueConverter<UserSessionId, Guid> { public UserSessionIdConverter() : base(x => x.Value, v => new UserSessionId(v)) { } }
public sealed class AuditEntryIdConverter : ValueConverter<AuditEntryId, Guid> { public AuditEntryIdConverter() : base(x => x.Value, v => new AuditEntryId(v)) { } }
public sealed class ElectronicSignatureIdConverter : ValueConverter<ElectronicSignatureId, Guid> { public ElectronicSignatureIdConverter() : base(x => x.Value, v => new ElectronicSignatureId(v)) { } }
public sealed class LaboratoryIdConverter : ValueConverter<LaboratoryId, Guid> { public LaboratoryIdConverter() : base(x => x.Value, v => new LaboratoryId(v)) { } }
public sealed class ContactPersonIdConverter : ValueConverter<ContactPersonId, Guid> { public ContactPersonIdConverter() : base(x => x.Value, v => new ContactPersonId(v)) { } }
public sealed class RepresentativeIdConverter : ValueConverter<RepresentativeId, Guid> { public RepresentativeIdConverter() : base(x => x.Value, v => new RepresentativeId(v)) { } }
public sealed class DailyVisitIdConverter : ValueConverter<DailyVisitId, Guid> { public DailyVisitIdConverter() : base(x => x.Value, v => new DailyVisitId(v)) { } }
public sealed class VisitHistoryIdConverter : ValueConverter<VisitHistoryId, Guid> { public VisitHistoryIdConverter() : base(x => x.Value, v => new VisitHistoryId(v)) { } }
public sealed class OutsourceSampleIdConverter : ValueConverter<OutsourceSampleId, Guid> { public OutsourceSampleIdConverter() : base(x => x.Value, v => new OutsourceSampleId(v)) { } }
public sealed class SampleTrackingIdConverter : ValueConverter<SampleTrackingId, Guid> { public SampleTrackingIdConverter() : base(x => x.Value, v => new SampleTrackingId(v)) { } }
public sealed class MarketingVisitIdConverter : ValueConverter<MarketingVisitId, Guid> { public MarketingVisitIdConverter() : base(x => x.Value, v => new MarketingVisitId(v)) { } }
public sealed class ComplaintIdConverter : ValueConverter<ComplaintId, Guid> { public ComplaintIdConverter() : base(x => x.Value, v => new ComplaintId(v)) { } }
public sealed class MonthlySampleIdConverter : ValueConverter<MonthlySampleId, Guid> { public MonthlySampleIdConverter() : base(x => x.Value, v => new MonthlySampleId(v)) { } }
public sealed class DailyLabStatisticIdConverter : ValueConverter<DailyLabStatisticId, Guid> { public DailyLabStatisticIdConverter() : base(x => x.Value, v => new DailyLabStatisticId(v)) { } }
public sealed class TestStatisticIdConverter : ValueConverter<TestStatisticId, Guid> { public TestStatisticIdConverter() : base(x => x.Value, v => new TestStatisticId(v)) { } }
public sealed class TestGroupIdConverter : ValueConverter<TestGroupId, Guid> { public TestGroupIdConverter() : base(x => x.Value, v => new TestGroupId(v)) { } }
public sealed class TestSetupIdConverter : ValueConverter<TestSetupId, Guid> { public TestSetupIdConverter() : base(x => x.Value, v => new TestSetupId(v)) { } }
public sealed class LabLoyaltyLedgerIdConverter : ValueConverter<LabLoyaltyLedgerId, Guid> { public LabLoyaltyLedgerIdConverter() : base(x => x.Value, v => new LabLoyaltyLedgerId(v)) { } }
public sealed class RepCommissionIdConverter : ValueConverter<RepCommissionId, Guid> { public RepCommissionIdConverter() : base(x => x.Value, v => new RepCommissionId(v)) { } }
public sealed class RefItemIdConverter : ValueConverter<RefItemId, Guid> { public RefItemIdConverter() : base(x => x.Value, v => new RefItemId(v)) { } }
public sealed class CityIdConverter : ValueConverter<CityId, Guid> { public CityIdConverter() : base(x => x.Value, v => new CityId(v)) { } }
public sealed class AreaIdConverter : ValueConverter<AreaId, Guid> { public AreaIdConverter() : base(x => x.Value, v => new AreaId(v)) { } }
public sealed class NotificationTemplateIdConverter : ValueConverter<NotificationTemplateId, Guid> { public NotificationTemplateIdConverter() : base(x => x.Value, v => new NotificationTemplateId(v)) { } }
public sealed class NotificationPreferenceIdConverter : ValueConverter<NotificationPreferenceId, Guid> { public NotificationPreferenceIdConverter() : base(x => x.Value, v => new NotificationPreferenceId(v)) { } }
public sealed class SystemNotificationIdConverter : ValueConverter<SystemNotificationId, Guid> { public SystemNotificationIdConverter() : base(x => x.Value, v => new SystemNotificationId(v)) { } }
public sealed class NotificationDeliveryLogIdConverter : ValueConverter<NotificationDeliveryLogId, Guid> { public NotificationDeliveryLogIdConverter() : base(x => x.Value, v => new NotificationDeliveryLogId(v)) { } }
