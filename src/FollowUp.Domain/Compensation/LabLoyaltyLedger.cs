using FollowUp.Domain.Common;
using FollowUp.Domain.Laboratories;

namespace FollowUp.Domain.Compensation;

public readonly record struct LabLoyaltyLedgerId(Guid Value)
{
    public static LabLoyaltyLedgerId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}

/// <summary>
/// A lab's loyalty position for one month (SRS FR-12, Workflows §9): points earned against target and the
/// resulting tier. One row per (lab, year-month). Points/tier are computed by the loyalty engine from the
/// compensation config — never entered by hand.
/// </summary>
public sealed class LabLoyaltyLedger : AggregateRoot<LabLoyaltyLedgerId>
{
    private LabLoyaltyLedger() { } // EF

    private LabLoyaltyLedger(LabLoyaltyLedgerId id, LaboratoryId labId, YearMonth period)
        : base(id)
    {
        LaboratoryId = labId;
        Period = period;
    }

    public LaboratoryId LaboratoryId { get; private set; }
    public YearMonth Period { get; private set; }
    public int Target { get; private set; }
    public int Achieved { get; private set; }
    public int Points { get; private set; }
    public string? Tier { get; private set; }
    public DateTimeOffset? ComputedAt { get; private set; }

    public static LabLoyaltyLedger For(LaboratoryId labId, YearMonth period) =>
        new(LabLoyaltyLedgerId.New(), labId, period);

    /// <summary>Records the engine-computed result for the period (server-authoritative).</summary>
    public void Record(int target, int achieved, int points, string? tier, DateTimeOffset when)
    {
        if (target < 0 || achieved < 0 || points < 0)
            throw new DomainException("Loyalty figures cannot be negative.");
        Target = target;
        Achieved = achieved;
        Points = points;
        Tier = tier;
        ComputedAt = when;
    }
}
