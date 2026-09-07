using System.Linq.Expressions;
using FollowUp.Domain.Common;
using FollowUp.Domain.Identity;
using FollowUp.Domain.Laboratories;
using FollowUp.Domain.Representatives;

namespace FollowUp.Infrastructure.Persistence.Queries;

/// <summary>
/// Pushes the six-dimension org scope into SQL (SRS NFR-PERF-3): for each dimension, a wildcard means no
/// filter, an empty set means deny-all, and otherwise an <c>IN (...)</c>. Applied to any query rooted on
/// <see cref="Laboratory"/> (other record types join to it for their dimensions), and to the
/// <see cref="Representative"/> directory on its four geographic dimensions (finding B-5).
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
        query = Dim(query, scope.Segments, static l => l.Segment);
        return query;
    }

    /// <summary>
    /// Scopes the representative directory. Reps carry only geographic attribution, so scope is enforced on
    /// Branch/Governorate/City/Area alone — never the lab-only Category/Segment — matching
    /// <c>ScopeGuard.EnsureInScope(Representative)</c>. An unattributed rep (null dimension) is visible only to a
    /// caller who is wildcard on that dimension, exactly as the write-side guard behaves.
    /// </summary>
    public static IQueryable<Representative> ApplyScope(this IQueryable<Representative> query, OrgScope scope)
    {
        query = Dim(query, scope.Branches, static r => r.Branch);
        query = Dim(query, scope.Governorates, static r => r.Governorate);
        query = Dim(query, scope.Cities, static r => r.City);
        query = Dim(query, scope.Areas, static r => r.Area);
        return query;
    }

    private static IQueryable<T> Dim<T>(
        IQueryable<T> query, IReadOnlySet<string> allowed, Expression<Func<T, string?>> selector)
    {
        if (allowed.Contains(OrgScope.Wildcard)) return query;
        if (allowed.Count == 0) return query.Where(_ => false);

        var values = allowed.ToList();
        // Build: x => x.<dim> != null && values.Contains(x.<dim>)
        var param = selector.Parameters[0];
        var member = selector.Body;
        var notNull = Expression.NotEqual(member, Expression.Constant(null, typeof(string)));
        var containsCall = Expression.Call(
            typeof(Enumerable), nameof(Enumerable.Contains), new[] { typeof(string) },
            Expression.Constant(values), member);
        var body = Expression.AndAlso(notNull, containsCall);
        var predicate = Expression.Lambda<Func<T, bool>>(body, param);
        return query.Where(predicate);
    }

    /// <summary>True when a scope permits a given (already-materialized) set of lab dimensions.</summary>
    public static bool Allows(this OrgScope scope, Laboratory lab) =>
        scope.Allows(lab.Branch, lab.Governorate, lab.City, lab.Area, lab.Category, lab.Segment);
}
