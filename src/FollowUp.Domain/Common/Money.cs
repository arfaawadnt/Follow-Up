namespace FollowUp.Domain.Common;

/// <summary>
/// A monetary amount stored at fixed <c>numeric(18,2)</c> precision (SRS data rules). All payout and
/// commission figures are recomputed server-side (BR-9); this type guarantees consistent scale/rounding.
/// Currency is single-tenant implicit and therefore not modeled.
/// </summary>
public readonly struct Money : IEquatable<Money>, IComparable<Money>
{
    public decimal Amount { get; }

    public Money(decimal amount) => Amount = decimal.Round(amount, 2, MidpointRounding.ToEven);

    public static readonly Money Zero = new(0m);

    public static Money operator +(Money a, Money b) => new(a.Amount + b.Amount);
    public static Money operator -(Money a, Money b) => new(a.Amount - b.Amount);
    public static Money operator *(Money a, decimal factor) => new(a.Amount * factor);

    public bool Equals(Money other) => Amount == other.Amount;
    public override bool Equals(object? obj) => obj is Money m && Equals(m);
    public override int GetHashCode() => Amount.GetHashCode();
    public int CompareTo(Money other) => Amount.CompareTo(other.Amount);
    public override string ToString() => Amount.ToString("0.00");

    public static bool operator ==(Money a, Money b) => a.Equals(b);
    public static bool operator !=(Money a, Money b) => !a.Equals(b);
    public static bool operator >(Money a, Money b) => a.CompareTo(b) > 0;
    public static bool operator <(Money a, Money b) => a.CompareTo(b) < 0;
    public static bool operator >=(Money a, Money b) => a.CompareTo(b) >= 0;
    public static bool operator <=(Money a, Money b) => a.CompareTo(b) <= 0;
}
