using FollowUp.Application.Common.Abstractions;
using FollowUp.Application.Common.Abstractions.Persistence;
using FollowUp.Application.Common.Exceptions;
using FollowUp.Domain.Accounting;

namespace FollowUp.Application.Common.Security;

/// <summary>A caller's rights on one treasury.</summary>
public readonly record struct TreasuryRights(bool View, bool Validate, bool Update)
{
    public static readonly TreasuryRights None = new(false, false, false);
    public static readonly TreasuryRights Full = new(true, true, true);
}

/// <summary>
/// The caller's rights on every treasury, resolved once per request. The built-in administrator role holds every right
/// everywhere; any other role holds exactly what its <see cref="TreasuryGrant"/> rows say (no row = no access). Org-scope
/// on the Branches dimension still applies on top — a grant never widens the caller's scope.
/// </summary>
public sealed class TreasuryAccessMap
{
    private readonly IReadOnlyDictionary<TreasuryId, TreasuryRights> _rights;
    public bool IsAdministrator { get; }

    private TreasuryAccessMap(bool administrator, IReadOnlyDictionary<TreasuryId, TreasuryRights> rights) { IsAdministrator = administrator; _rights = rights; }

    public static TreasuryAccessMap Administrator() => new(true, new Dictionary<TreasuryId, TreasuryRights>());
    public static TreasuryAccessMap FromGrants(IEnumerable<TreasuryGrant> grants) =>
        new(false, grants.ToDictionary(g => g.TreasuryId, g => new TreasuryRights(g.CanView, g.CanValidate, g.CanUpdate)));

    public TreasuryRights For(TreasuryId treasuryId) =>
        IsAdministrator ? TreasuryRights.Full : _rights.GetValueOrDefault(treasuryId, TreasuryRights.None);

    public bool CanView(TreasuryId id) => For(id).View;
    public bool CanValidate(TreasuryId id) => For(id).Validate;
    public bool CanUpdate(TreasuryId id) => For(id).Update;

    public void EnsureView(TreasuryId id) { if (!CanView(id)) throw new ForbiddenException("You have no access to this treasury."); }
    public void EnsureValidate(TreasuryId id) { if (!CanValidate(id)) throw new ForbiddenException("You are not allowed to validate entries of this treasury."); }
    public void EnsureUpdate(TreasuryId id) { if (!CanUpdate(id)) throw new ForbiddenException("You are not allowed to record or update entries of this treasury."); }
}

/// <summary>Resolves the current caller's <see cref="TreasuryAccessMap"/>.</summary>
public interface ITreasuryAccess
{
    Task<TreasuryAccessMap> ResolveAsync(CancellationToken ct);
}

/// <summary>Reads the caller's role (built-in ⇒ administrator) and its treasury grants. Scoped per request.</summary>
public sealed class TreasuryAccess : ITreasuryAccess
{
    private readonly ICurrentUser _user;
    private readonly IRoleRepository _roles;
    private readonly ITreasuryGrantRepository _grants;
    private TreasuryAccessMap? _cached;

    public TreasuryAccess(ICurrentUser user, IRoleRepository roles, ITreasuryGrantRepository grants) { _user = user; _roles = roles; _grants = grants; }

    public async Task<TreasuryAccessMap> ResolveAsync(CancellationToken ct)
    {
        if (_cached is not null) return _cached;
        var role = await _roles.GetByIdAsync(_user.RoleId, ct);
        // The system principal (jobs, seeding) has no role row; it is an administrator by construction.
        if (role is null && _user.Username == "system") return _cached = TreasuryAccessMap.Administrator();
        if (role is { IsBuiltIn: true }) return _cached = TreasuryAccessMap.Administrator();
        return _cached = TreasuryAccessMap.FromGrants(await _grants.GetForRoleAsync(_user.RoleId, ct));
    }
}
