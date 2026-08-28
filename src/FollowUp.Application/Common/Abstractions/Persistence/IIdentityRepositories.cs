using FollowUp.Domain.Identity;
using FollowUp.Domain.Representatives;

namespace FollowUp.Application.Common.Abstractions.Persistence;

/// <summary>Aggregate repository for <see cref="AppUser"/> (write side; ADR-0005).</summary>
public interface IAppUserRepository
{
    Task<AppUser?> GetByIdAsync(AppUserId id, CancellationToken ct);
    Task<AppUser?> GetByUsernameAsync(string username, CancellationToken ct);
    Task<bool> UsernameExistsAsync(string username, CancellationToken ct);
    Task<bool> AnyLinkedToRepAsync(RepresentativeId repId, CancellationToken ct);
    void Add(AppUser user);
    void Remove(AppUser user);
}

/// <summary>Aggregate repository for <see cref="Role"/> (write side; ADR-0005).</summary>
public interface IRoleRepository
{
    Task<Role?> GetByIdAsync(RoleId id, CancellationToken ct);
    Task<Role?> GetByNameAsync(string name, CancellationToken ct);
    Task<bool> IsInUseAsync(RoleId id, CancellationToken ct);
    void Add(Role role);
    void Remove(Role role);
}

/// <summary>Aggregate repository for <see cref="UserSession"/>.</summary>
public interface IUserSessionRepository
{
    Task<UserSession?> GetByIdAsync(UserSessionId id, CancellationToken ct);
    Task<UserSession?> GetActiveByTokenHashAsync(string tokenHash, CancellationToken ct);
    /// <summary>The user's non-revoked sessions — used to evict them on a password change (IDN-5).</summary>
    Task<IReadOnlyList<UserSession>> GetActiveByUserAsync(AppUserId userId, CancellationToken ct);
    void Add(UserSession session);
}
