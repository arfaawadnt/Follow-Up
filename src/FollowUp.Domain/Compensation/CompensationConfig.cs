using FollowUp.Domain.Common;

namespace FollowUp.Domain.Compensation;

/// <summary>A loyalty tier: labs achieving at least <see cref="MinAchievementPercent"/> of target earn <see cref="Points"/> and this tier name.</summary>
public sealed class LoyaltyTier : ValueObject
{
    public string Name { get; }
    public decimal MinAchievementPercent { get; }
    public int Points { get; }

    public LoyaltyTier(string name, decimal minAchievementPercent, int points)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("Tier name is required.");
        if (minAchievementPercent < 0) throw new DomainException("Tier threshold cannot be negative.");
        if (minAchievementPercent > CompensationConfig.MaxAchievementPercent) // CPN-17: guard absurd thresholds
            throw new DomainException($"Tier threshold cannot exceed {CompensationConfig.MaxAchievementPercent}%.");
        if (points < 0) throw new DomainException("Tier points cannot be negative.");
        Name = name.Trim();
        MinAchievementPercent = minAchievementPercent;
        Points = points;
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Name;
        yield return MinAchievementPercent;
        yield return Points;
    }
}

/// <summary>
/// Configuration that defines the loyalty and commission formulas (SRS FR-12). Data-driven so the formulas
/// are not hardcoded; the engines read this to compute ledgers and payouts. A single configuration record.
/// The concrete tier thresholds/rates are seeded as placeholders (see docs/ASSUMPTIONS.md A3).
/// </summary>
public sealed class CompensationConfig : AggregateRoot<string>, IVersioned, IAuditable
{
    private readonly List<LoyaltyTier> _loyaltyTiers = new();

    private CompensationConfig() { } // EF

    private CompensationConfig(string id) : base(id) { }

    public const string SingletonId = "default";

    /// <summary>Sanity ceiling for achievement-based percentages (CPN-17): a lab/rep can exceed 100% of target,
    /// but a threshold beyond 10× target is a data-entry error, not a real one. Interim guard pending a
    /// dedicated Percentage value object.</summary>
    public const decimal MaxAchievementPercent = 1000m;

    /// <summary>Percent of achieved value paid as commission.</summary>
    /// <summary>Optimistic-concurrency token (Postgres xmin); concurrent edits conflict (409). Finding CPN-9.</summary>
    public uint RowVersion { get; private set; }

    public decimal CommissionRatePercent { get; private set; }

    /// <summary>Achievement percent at/above which the flat bonus is awarded.</summary>
    public decimal BonusThresholdPercent { get; private set; }

    public Money BonusAmount { get; private set; }
    public IReadOnlyList<LoyaltyTier> LoyaltyTiers => _loyaltyTiers.OrderByDescending(t => t.MinAchievementPercent).ToList();

    public DateTimeOffset CreatedAt { get; private set; }
    public string CreatedBy { get; private set; } = null!;
    public DateTimeOffset? UpdatedAt { get; private set; }
    public string? UpdatedBy { get; private set; }

    public static CompensationConfig Create(decimal commissionRatePercent, decimal bonusThresholdPercent,
        Money bonusAmount, IEnumerable<LoyaltyTier> tiers)
    {
        // Route through SetCommission so first-time config cannot bypass the negative-rate guard the way the
        // old ctor assignment did — a negative rate then hard-failed every rep's commission recompute (CPN-5).
        var cfg = new CompensationConfig(SingletonId);
        cfg.SetCommission(commissionRatePercent, bonusThresholdPercent, bonusAmount);
        cfg.SetTiers(tiers);
        return cfg;
    }

    public void SetTiers(IEnumerable<LoyaltyTier> tiers)
    {
        // Guard the loyalty formula's inputs (CPN-6): an empty set zeroes points/nulls the tier for every lab on
        // the next recalc, and duplicate names or thresholds make TierFor order-dependent and nondeterministic.
        var list = tiers?.ToList() ?? throw new DomainException("At least one loyalty tier is required.");
        if (list.Count == 0)
            throw new DomainException("At least one loyalty tier is required.");
        if (list.Select(t => t.Name.ToLowerInvariant()).Distinct().Count() != list.Count)
            throw new DomainException("Loyalty tier names must be unique.");
        if (list.Select(t => t.MinAchievementPercent).Distinct().Count() != list.Count)
            throw new DomainException("Loyalty tier thresholds must be unique.");

        _loyaltyTiers.Clear();
        _loyaltyTiers.AddRange(list);
    }

    public void SetCommission(decimal ratePercent, decimal bonusThresholdPercent, Money bonusAmount)
    {
        if (ratePercent < 0 || bonusThresholdPercent < 0)
            throw new DomainException("Commission rates cannot be negative.");
        // Upper bounds (CPN-17): a commission rate is a fraction of a base, so it cannot exceed 100%; the bonus
        // threshold is an achievement percent (over-achievement allowed) but is sanity-capped against typos.
        if (ratePercent > 100)
            throw new DomainException("Commission rate cannot exceed 100%.");
        if (bonusThresholdPercent > MaxAchievementPercent)
            throw new DomainException($"Bonus threshold cannot exceed {MaxAchievementPercent}%.");
        if (bonusAmount.Amount < 0)
            throw new DomainException("Bonus amount cannot be negative.");
        CommissionRatePercent = ratePercent;
        BonusThresholdPercent = bonusThresholdPercent;
        BonusAmount = bonusAmount;
    }

    /// <summary>Selects the loyalty tier for a given achievement percent (highest qualifying tier).</summary>
    public LoyaltyTier? TierFor(decimal achievementPercent) =>
        _loyaltyTiers.Where(t => achievementPercent >= t.MinAchievementPercent)
            .OrderByDescending(t => t.MinAchievementPercent)
            .FirstOrDefault();
}
