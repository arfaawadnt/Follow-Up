using FollowUp.Application.Common.Abstractions;
using FollowUp.Application.Common.Exceptions;
using FollowUp.Domain.Identity;

namespace FollowUp.Application.Common.Security;

/// <summary>
/// Enforces anti-amplification on all user/role grants (SRS BR-12 / NFR-SEC-8): no actor may confer a
/// privilege or a scope breadth they do not themselves hold. Checked against the caller's *effective*
/// privileges and scope (re-read from the DB each request).
/// </summary>
public static class AntiAmplificationGuard
{
    /// <summary>Ensures every requested privilege is one the caller effectively holds.</summary>
    public static void EnsurePrivilegesWithinGrant(this ICurrentUser caller, IEnumerable<string> requested)
    {
        var effectiveRequested = Privileges.Expand(requested);
        var missing = effectiveRequested.Where(p => !caller.Has(p)).ToArray();
        if (missing.Length > 0)
            throw new ForbiddenException(
                $"You cannot grant privileges you do not hold: {string.Join(", ", missing)}.");
    }

    /// <summary>Ensures the requested scope is contained within the caller's own scope.</summary>
    public static void EnsureScopeWithinGrant(this ICurrentUser caller, OrgScope requested)
    {
        if (!requested.IsWithin(caller.Scope))
            throw new ForbiddenException("You cannot grant an organizational scope broader than your own.");
    }
}
