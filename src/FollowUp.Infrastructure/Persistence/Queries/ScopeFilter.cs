using FollowUp.Domain.Common;
using FollowUp.Domain.Identity;
using FollowUp.Domain.Laboratories;

namespace FollowUp.Infrastructure.Persistence.Queries;

/// <summary>
/// Pushes the six-dimension org scope into SQL (SRS NFR-PERF-3): for each dimension, a wildcard means no
/// filter, an empty set means deny-all, and otherwise an <c>IN (...)</c>. Applied to any query rooted on
/// <see cref="Laboratory"/> (other record types join to it for their dimensions).
/// </summary>
internal static class ScopeFilter
{
    public static IQueryable<Laboratory> ApplyScope(this IQueryable<Laboratory> query, OrgScope scope)
    {
        query = Dim(query, scope.Branches, static l => l.Branch);
        query = Dim(query, scope.Governorates, static l => l.Governorate);
        query = Dim(query, scope.Cities, static l => l.City);
        query = Dim(query, scope.Areas, static l => l.Area);
        query = Dim(query, scope.Categories, static l => l.Category);

        if (!scope.Segments.Contains(OrgScope.Wildcard))
        {
            if (scope.Segments.Count == 0)
                return query.Where(_ => false);
            var segs = scope.Segments.Select(n => Enumeration.FromName<Segment>(n)).ToList();
            query = query.Where(l => segs.Contains(l.Segment));
        }
        return query;
    }

    private static IQueryable<Laboratory> Dim(
        IQueryable<Laboratory> query, IReadOnlySet<string> allowed, System.Linq.Expressions.Expression<Func<Laboratory, string?>> selector)
    {
        if (allowed.Contains(OrgScope.Wildcard)) return query;
        if (allowed.Count == 0) return query.Where(_ => false);

        var values = allowed.ToList();
        // Build: l => l.<dim> != null && values.Contains(l.<dim>)
        var param = selector.Parameters[0];
        var member = selector.Body;
        var notNull = System.Linq.Expressions.Expression.NotEqual(member, System.Linq.Expressions.Expression.Constant(null, typeof(string)));
        var containsCall = System.Linq.Expressions.Expression.Call(
            typeof(Enumerable), nameof(Enumerable.Contains), new[] { typeof(string) },
            System.Linq.Expressions.Expression.Constant(values), member);
        var body = System.Linq.Expressions.Expression.AndAlso(notNull, containsCall);
        var predicate = System.Linq.Expressions.Expression.Lambda<Func<Laboratory, bool>>(body, param);
        return query.Where(predicate);
    }

    /// <summary>True when a scope permits a given (already-materialized) set of lab dimensions.</summary>
    public static bool Allows(this OrgScope scope, Laboratory lab) =>
        scope.Allows(lab.Branch, lab.Governorate, lab.City, lab.Area, lab.Category, lab.Segment.Name);
}
