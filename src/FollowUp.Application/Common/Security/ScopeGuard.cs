using FollowUp.Application.Common.Abstractions;
using FollowUp.Application.Common.Exceptions;
using FollowUp.Domain.Laboratories;

namespace FollowUp.Application.Common.Security;

/// <summary>
/// Record-level authorization (SRS Authorization Layer 3). Handlers call these after loading an aggregate to
/// confirm it falls within the caller's six-dimension org scope (and, for rep-linked accounts, ownership).
/// Fails closed with <see cref="ForbiddenException"/>.
/// </summary>
public static class ScopeGuard
{
    /// <summary>Ensures a laboratory is within the caller's org scope.</summary>
    public static void EnsureInScope(this ICurrentUser user, Laboratory lab)
    {
        var allowed = user.Scope.Allows(
            lab.Branch, lab.Governorate, lab.City, lab.Area, lab.Category, lab.Segment.Name);
        if (!allowed)
            throw new ForbiddenException("This laboratory is outside your organizational scope.");
    }

    /// <summary>Ensures the caller's scope permits the given hierarchy (used before creating a record).</summary>
    public static void EnsureHierarchyInScope(this ICurrentUser user,
        string? branch, string? governorate, string? city, string? area, string? category, string? segment)
    {
        if (!user.Scope.Allows(branch, governorate, city, area, category, segment))
            throw new ForbiddenException("The target location is outside your organizational scope.");
    }
}
