using FollowUp.Application.Common.Abstractions;
using FollowUp.Application.Common.Exceptions;
using FollowUp.Domain.Laboratories;
using FollowUp.Domain.Representatives;

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
            lab.Branch, lab.Governorate, lab.City, lab.Area, lab.Category, lab.Segment);
        if (!allowed)
            throw new ForbiddenException("This laboratory is outside your organizational scope.");
    }

    /// <summary>
    /// Ensures a representative is within the caller's org scope (finding CPN-3). Reps carry
    /// Branch/Governorate/City/Area; the lab-only Category/Segment dimensions are wildcarded, matching the
    /// read-side rule in GetCommissionsAsync (CPN-2). Unattributed reps are in scope only for a global caller.
    /// </summary>
    public static void EnsureInScope(this ICurrentUser user, Representative rep)
    {
        var allowed = user.Scope.Allows(rep.Branch, rep.Governorate, rep.City, rep.Area,
            FollowUp.Domain.Identity.OrgScope.Wildcard, FollowUp.Domain.Identity.OrgScope.Wildcard);
        if (!allowed)
            throw new ForbiddenException("This representative is outside your organizational scope.");
    }

    /// <summary>Ensures the caller's scope permits the given hierarchy (used before creating a record).</summary>
    public static void EnsureHierarchyInScope(this ICurrentUser user,
        string? branch, string? governorate, string? city, string? area, string? category, string? segment)
    {
        if (!user.Scope.Allows(branch, governorate, city, area, category, segment))
            throw new ForbiddenException("The target location is outside your organizational scope.");
    }

    /// <summary>
    /// For rep-linked accounts, ensures the record is owned by the caller's representative (SRS Layer 3
    /// ownership). Office/supervisor accounts (not rep-linked) are not constrained by ownership.
    /// </summary>
    public static void EnsureOwnedIfRepLinked(this ICurrentUser user, RepresentativeId? ownerRepId)
    {
        if (user.RepresentativeId is { } mine && ownerRepId != mine)
            throw new ForbiddenException("This record is not assigned to you.");
    }

    /// <summary>Ensures the caller's area scope permits the given area (SRS FR-8 area allow-list).</summary>
    public static void EnsureAreaInScope(this ICurrentUser user, string area)
    {
        var areas = user.Scope.Areas;
        if (!areas.Contains(FollowUp.Domain.Identity.OrgScope.Wildcard) && !areas.Contains(area))
            throw new ForbiddenException("This area is outside your organizational scope.");
    }
}
