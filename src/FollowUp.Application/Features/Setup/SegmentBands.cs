namespace FollowUp.Application.Features.Setup;

/// <summary>
/// Pure segment-band matching for the income-based auto-assignment. A band is a segment code plus an inclusive upper
/// income bound; a null upper bound is the unbounded top tier (e.g. VIP). A lab's monthly achieved income lands in
/// the first band — ordered by upper bound ascending — whose upper bound is greater than or equal to the income.
/// This makes contiguous tiers like C≤3000, B≤6000, A≤10000, VIP&gt;10000 resolve every amount (including fractional
/// EGP) to exactly one segment with no gaps.
/// </summary>
public static class SegmentBands
{
    public static string? Match(IEnumerable<(string Code, decimal? UpperBound)> bands, decimal income)
    {
        foreach (var band in bands.OrderBy(b => b.UpperBound ?? decimal.MaxValue))
            if (band.UpperBound is null || income <= band.UpperBound.Value)
                return band.Code;
        return null;
    }
}
