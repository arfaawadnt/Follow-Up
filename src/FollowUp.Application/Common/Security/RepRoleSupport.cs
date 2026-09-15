using FollowUp.Application.Common.Abstractions;
using FollowUp.Application.Common.Abstractions.Persistence;
using FollowUp.Application.Common.Exceptions;
using FollowUp.Domain.Representatives;

namespace FollowUp.Application.Common.Security;

/// <summary>
/// Resolves an optional role assignment of a representative to a lab or an area: the rep must exist, be within the
/// caller's org-scope (ADR-0002) and be of the expected type (<c>AreaManager</c> for an area's manager,
/// <c>LabResponsible</c> for a lab's responsible) — the UI pickers are bound to those types and the server enforces the
/// same rule. Returns null when no assignment was supplied.
/// </summary>
public static class RepRoleSupport
{
    public static async Task<RepresentativeId?> ResolveAsync(Guid? repId, RepresentativeType expected,
        string field, IRepresentativeRepository reps, ICurrentUser user, CancellationToken ct)
    {
        if (repId is not { } id) return null;
        var rep = await reps.GetByIdAsync(new RepresentativeId(id), ct) ?? throw new NotFoundException("Representative", id);
        user.EnsureInScope(rep);
        if (rep.Type != expected)
            throw new ValidationException(new Dictionary<string, string[]>
            {
                [field] = new[] { $"The selected representative must be of type {expected.Name}." },
            });
        return rep.Id;
    }
}
