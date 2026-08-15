namespace FollowUp.Domain.Common;

/// <summary>
/// A calendar month (year + month), the natural key for monthly rollups, loyalty ledgers and commissions.
/// Stored compactly as <c>year*100 + month</c> (e.g. 202608).
/// </summary>
public readonly record struct YearMonth : IComparable<YearMonth>
{
    public int Year { get; }
    public int Month { get; }

    public YearMonth(int year, int month)
    {
        if (month is < 1 or > 12) throw new DomainException("Month must be between 1 and 12.");
        if (year is < 2000 or > 2100) throw new DomainException("Year is out of the supported range.");
        Year = year;
        Month = month;
    }

    public static YearMonth From(DateOnly date) => new(date.Year, date.Month);
    public static YearMonth FromCode(int code) => new(code / 100, code % 100);

    /// <summary>Compact integer form <c>yyyymm</c> for persistence and indexing.</summary>
    public int Code => Year * 100 + Month;

    public int CompareTo(YearMonth other) => Code.CompareTo(other.Code);
    public override string ToString() => $"{Year:0000}-{Month:00}";

    public static bool operator >(YearMonth a, YearMonth b) => a.Code > b.Code;
    public static bool operator <(YearMonth a, YearMonth b) => a.Code < b.Code;
    public static bool operator >=(YearMonth a, YearMonth b) => a.Code >= b.Code;
    public static bool operator <=(YearMonth a, YearMonth b) => a.Code <= b.Code;
}
