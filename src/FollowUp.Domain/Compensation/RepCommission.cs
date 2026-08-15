using FollowUp.Domain.Common;
using FollowUp.Domain.Representatives;

namespace FollowUp.Domain.Compensation;

public readonly record struct RepCommissionId(Guid Value)
{
    public static RepCommissionId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}

/// <summary>
/// A representative's monthly payout (SRS FR-12, BR-9). Payout = base salary + commission + bonus, all
/// <b>recomputed server-side</b> from targets vs achieved — client-supplied amounts are ignored. One row
/// per (rep, year-month).
/// </summary>
public sealed class RepCommission : AggregateRoot<RepCommissionId>
{
    private RepCommission() { } // EF

    private RepCommission(RepCommissionId id, RepresentativeId repId, YearMonth period)
        : base(id)
    {
        RepresentativeId = repId;
        Period = period;
    }

    public RepresentativeId RepresentativeId { get; private set; }
    public YearMonth Period { get; private set; }

    public decimal Target { get; private set; }
    public decimal Achieved { get; private set; }
    public Money BaseSalary { get; private set; }
    public Money Commission { get; private set; }
    public Money Bonus { get; private set; }
    public DateTimeOffset? ComputedAt { get; private set; }

    /// <summary>Total payout — always derived, never stored independently (single source of truth).</summary>
    public Money Total => BaseSalary + Commission + Bonus;

    public static RepCommission For(RepresentativeId repId, YearMonth period) =>
        new(RepCommissionId.New(), repId, period);

    /// <summary>
    /// Applies a server-side recomputation (BR-9). The application layer computes these figures from the
    /// compensation config + monthly volumes and calls this; the aggregate rejects negative money.
    /// </summary>
    public void Recompute(decimal target, decimal achieved, Money baseSalary, Money commission, Money bonus, DateTimeOffset when)
    {
        if (baseSalary < Money.Zero || commission < Money.Zero || bonus < Money.Zero)
            throw new DomainException("Payout components cannot be negative.");
        Target = target;
        Achieved = achieved;
        BaseSalary = baseSalary;
        Commission = commission;
        Bonus = bonus;
        ComputedAt = when;
    }
}
