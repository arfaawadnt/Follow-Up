using FluentAssertions;
using FollowUp.Domain.Common;
using FollowUp.Domain.Compensation;
using FollowUp.Domain.Integration;
using FollowUp.Domain.Notifications;
using FollowUp.Domain.Identity;
using FollowUp.Domain.Representatives;

namespace FollowUp.Domain.Tests.Compensation;

public class RepCommissionTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 1, 0, 0, 0, TimeSpan.FromHours(2));

    [Fact]
    public void Total_is_base_plus_commission_plus_bonus()
    {
        var c = RepCommission.For(RepresentativeId.New(), new YearMonth(2026, 8));
        c.Recompute(1000m, 1200m, new Money(5000m), new Money(600m), new Money(250m), Now);

        c.Total.Should().Be(new Money(5850m));
    }

    [Fact]
    public void Negative_components_are_rejected()
    {
        var c = RepCommission.For(RepresentativeId.New(), new YearMonth(2026, 8));
        var act = () => c.Recompute(1000m, 1200m, new Money(-1m), Money.Zero, Money.Zero, Now);
        act.Should().Throw<DomainException>();
    }
}

public class CompensationConfigTests
{
    [Fact]
    public void TierFor_selects_highest_qualifying_tier()
    {
        var cfg = CompensationConfig.Create(5m, 100m, new Money(500m), new[]
        {
            new LoyaltyTier("Bronze", 50m, 100),
            new LoyaltyTier("Silver", 80m, 250),
            new LoyaltyTier("Gold", 100m, 500),
        });

        cfg.TierFor(90m)!.Name.Should().Be("Silver");
        cfg.TierFor(100m)!.Name.Should().Be("Gold");
        cfg.TierFor(10m).Should().BeNull();
    }
}

public class OracleConfigTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 15, 12, 0, 0, TimeSpan.FromHours(2));

    [Fact]
    public void Rejects_non_select_queries()
    {
        var act = () => AllowListedQuery.Create("Bad", "DELETE FROM labs");
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Fingerprint_detects_tampering()
    {
        var q = AllowListedQuery.Create("Labs", "SELECT code, name FROM labs");
        q.Matches("SELECT code, name FROM labs").Should().BeTrue();
        q.Matches("SELECT code, name FROM labs WHERE 1=1").Should().BeFalse();
    }

    [Fact]
    public void Is_due_only_when_enabled_and_interval_elapsed()
    {
        var cfg = OracleConfig.Create(enabled: true, intervalHours: 24);
        cfg.IsDue(Now).Should().BeTrue();                       // never synced

        cfg.RecordSyncResult("ok", Now);
        cfg.IsDue(Now.AddHours(1)).Should().BeFalse();
        cfg.IsDue(Now.AddHours(25)).Should().BeTrue();

        cfg.Configure(enabled: false, intervalHours: 24);
        cfg.IsDue(Now.AddDays(2)).Should().BeFalse();           // disabled
    }
}

public class NotificationPreferenceTests
{
    [Fact]
    public void Default_is_system_on_others_off()
    {
        var pref = NotificationPreference.Default(AppUserId.New(), "complaint.logged");

        pref.Allows(NotificationChannel.System).Should().BeTrue();
        pref.Allows(NotificationChannel.Mail).Should().BeFalse();
        pref.Allows(NotificationChannel.WhatsApp).Should().BeFalse();
    }
}
