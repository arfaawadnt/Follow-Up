using FollowUp.Domain.Common;

namespace FollowUp.Domain.Compensation;

/// <summary>
/// Domain service that turns achievement-vs-target into loyalty points/tier and commission/bonus figures
/// using the data-driven <see cref="CompensationConfig"/> (SRS FR-12, BR-6/BR-9). All money is computed here
/// server-side; client-supplied amounts are never used.
/// </summary>
public sealed class CompensationCalculator
{
    private readonly CompensationConfig _config;

    public CompensationCalculator(CompensationConfig config) => _config = config;

    public static decimal AchievementPercent(decimal achieved, decimal target) =>
        target <= 0 ? 0 : decimal.Round(achieved / target * 100m, 2);

    /// <summary>Loyalty result for a lab: points and tier name for its achievement.</summary>
    public (int Points, string? Tier) ComputeLoyalty(decimal achieved, decimal target)
    {
        var pct = AchievementPercent(achieved, target);
        var tier = _config.TierFor(pct);
        return (tier?.Points ?? 0, tier?.Name);
    }

    /// <summary>Commission result for a rep: commission on achieved volume, plus a flat bonus past the threshold.</summary>
    public (Money Commission, Money Bonus) ComputeCommission(decimal achieved, decimal target, Money baseSalary)
    {
        var commission = new Money(achieved * (_config.CommissionRatePercent / 100m));
        var pct = AchievementPercent(achieved, target);
        var bonus = pct >= _config.BonusThresholdPercent ? _config.BonusAmount : Money.Zero;
        return (commission, bonus);
    }
}
