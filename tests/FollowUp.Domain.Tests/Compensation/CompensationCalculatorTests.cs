using FluentAssertions;
using FollowUp.Domain.Common;
using FollowUp.Domain.Compensation;

namespace FollowUp.Domain.Tests.Compensation;

public class CompensationCalculatorTests
{
    private static CompensationConfig Config() => CompensationConfig.Create(
        commissionRatePercent: 5m, bonusThresholdPercent: 100m, bonusAmount: new Money(500m),
        tiers: new[]
        {
            new LoyaltyTier("Bronze", 50m, 100),
            new LoyaltyTier("Silver", 80m, 250),
            new LoyaltyTier("Gold", 100m, 500),
        });

    [Fact]
    public void Loyalty_points_and_tier_follow_achievement()
    {
        var calc = new CompensationCalculator(Config());

        calc.ComputeLoyalty(achieved: 90, target: 100).Should().Be((250, "Silver"));
        calc.ComputeLoyalty(achieved: 120, target: 100).Should().Be((500, "Gold"));
        calc.ComputeLoyalty(achieved: 10, target: 100).Should().Be((0, (string?)null));
    }

    [Fact]
    public void Commission_is_rate_times_achieved_plus_threshold_bonus()
    {
        var calc = new CompensationCalculator(Config());

        // Below threshold: 5% of 1000 = 50, no bonus.
        calc.ComputeCommission(achieved: 1000, target: 2000, baseSalary: new Money(3000))
            .Should().Be((new Money(50m), Money.Zero));

        // At/above threshold: 5% of 2000 = 100, plus 500 bonus.
        calc.ComputeCommission(achieved: 2000, target: 2000, baseSalary: new Money(3000))
            .Should().Be((new Money(100m), new Money(500m)));
    }

    [Fact]
    public void Zero_target_yields_zero_achievement_not_divide_by_zero()
    {
        CompensationCalculator.AchievementPercent(achieved: 500, target: 0).Should().Be(0);
    }
}
