using FollowUp.Domain.Common;

namespace FollowUp.Domain.Identity;

/// <summary>
/// The six-dimension organizational scope (SRS Authorization Layer 3). Each dimension is a set of
/// allowed values where <c>"*"</c> means "all" and an <b>empty</b> set means "deny all" (fail-closed).
/// Evaluated server-side on every record-bearing operation; never trusted from the token.
/// </summary>
public sealed class OrgScope : ValueObject
{
    public const string Wildcard = "*";

    public IReadOnlySet<string> Branches { get; }
    public IReadOnlySet<string> Governorates { get; }
    public IReadOnlySet<string> Cities { get; }
    public IReadOnlySet<string> Areas { get; }
    public IReadOnlySet<string> Categories { get; }
    public IReadOnlySet<string> Segments { get; }

    private OrgScope(IEnumerable<string> branches, IEnumerable<string> governorates, IEnumerable<string> cities,
        IEnumerable<string> areas, IEnumerable<string> categories, IEnumerable<string> segments)
    {
        Branches = Norm(branches);
        Governorates = Norm(governorates);
        Cities = Norm(cities);
        Areas = Norm(areas);
        Categories = Norm(categories);
        Segments = Norm(segments);
    }

    private static HashSet<string> Norm(IEnumerable<string> values) =>
        new(values?.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()) ?? Array.Empty<string>(),
            StringComparer.OrdinalIgnoreCase);

    public static OrgScope Create(IEnumerable<string> branches, IEnumerable<string> governorates,
        IEnumerable<string> cities, IEnumerable<string> areas, IEnumerable<string> categories,
        IEnumerable<string> segments) =>
        new(branches, governorates, cities, areas, categories, segments);

    /// <summary>Global scope — everything allowed across all six dimensions.</summary>
    public static OrgScope Global => new(
        new[] { Wildcard }, new[] { Wildcard }, new[] { Wildcard },
        new[] { Wildcard }, new[] { Wildcard }, new[] { Wildcard });

    /// <summary>Fully closed scope — denies everything (the fail-safe default for a missing role).</summary>
    public static OrgScope Deny => new(
        Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(),
        Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());

    private static bool DimensionAllows(IReadOnlySet<string> allowed, string? value)
    {
        if (allowed.Contains(Wildcard)) return true;
        if (allowed.Count == 0) return false;         // empty = deny all
        if (string.IsNullOrWhiteSpace(value)) return false;
        return allowed.Contains(value);
    }

    /// <summary>
    /// Whether a record with the given dimension values falls within this scope. A null dimension on the
    /// record is allowed only when that dimension is wildcarded.
    /// </summary>
    public bool Allows(string? branch, string? governorate, string? city, string? area,
        string? category, string? segment) =>
        DimensionAllows(Branches, branch) &&
        DimensionAllows(Governorates, governorate) &&
        DimensionAllows(Cities, city) &&
        DimensionAllows(Areas, area) &&
        DimensionAllows(Categories, category) &&
        DimensionAllows(Segments, segment);

    /// <summary>
    /// True when this scope is entirely contained by <paramref name="other"/> — used by the
    /// anti-amplification guard (BR-12): nobody may grant scope breadth they do not themselves hold.
    /// </summary>
    public bool IsWithin(OrgScope other) =>
        DimWithin(Branches, other.Branches) && DimWithin(Governorates, other.Governorates) &&
        DimWithin(Cities, other.Cities) && DimWithin(Areas, other.Areas) &&
        DimWithin(Categories, other.Categories) && DimWithin(Segments, other.Segments);

    private static bool DimWithin(IReadOnlySet<string> inner, IReadOnlySet<string> outer)
    {
        if (outer.Contains(Wildcard)) return true;
        if (inner.Contains(Wildcard)) return false;   // inner is broader than a non-wildcard outer
        return inner.All(outer.Contains);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        foreach (var s in All()) yield return s;
    }

    private IEnumerable<string> All()
    {
        static IEnumerable<string> Tagged(string tag, IReadOnlySet<string> set) =>
            set.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).Select(x => $"{tag}:{x}");
        return Tagged("b", Branches).Concat(Tagged("g", Governorates)).Concat(Tagged("c", Cities))
            .Concat(Tagged("a", Areas)).Concat(Tagged("cat", Categories)).Concat(Tagged("s", Segments));
    }
}
