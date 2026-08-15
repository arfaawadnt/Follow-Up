namespace FollowUp.Application.Common.Models;

/// <summary>
/// A bounded page of results (SRS NFR-PERF-2: max page size 1000; truncation is always declared, never
/// silent). <see cref="Truncated"/> is true when more rows existed than were returned.
/// </summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize, bool Truncated)
{
    public const int MaxPageSize = 1000;

    public static PagedResult<T> Create(IReadOnlyList<T> items, int total, int page, int pageSize)
    {
        var truncated = total > page * pageSize;
        return new PagedResult<T>(items, total, page, pageSize, truncated);
    }

    public static PagedResult<T> Empty(int page, int pageSize) =>
        new(Array.Empty<T>(), 0, page, pageSize, false);
}

/// <summary>Common list query parameters (paging, search, sort) shared by query requests.</summary>
public record ListQuery
{
    private int _pageSize = 50;
    public int Page { get; init; } = 1;

    public int PageSize
    {
        get => _pageSize;
        init => _pageSize = value is < 1 or > PagedResult<object>.MaxPageSize
            ? Math.Clamp(value, 1, PagedResult<object>.MaxPageSize)
            : value;
    }

    public string? Search { get; init; }
    public string? SortBy { get; init; }
    public bool SortDescending { get; init; }

    public int Skip => (Math.Max(Page, 1) - 1) * PageSize;
}
