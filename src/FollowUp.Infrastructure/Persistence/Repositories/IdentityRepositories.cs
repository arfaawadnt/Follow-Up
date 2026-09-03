using FollowUp.Application.Common.Abstractions.Persistence;
using FollowUp.Domain.Identity;
using FollowUp.Domain.Representatives;
using Microsoft.EntityFrameworkCore;

namespace FollowUp.Infrastructure.Persistence.Repositories;

internal sealed class AppUserRepository : IAppUserRepository
{
    private readonly FollowUpDbContext _db;
    public AppUserRepository(FollowUpDbContext db) => _db = db;

    public Task<AppUser?> GetByIdAsync(AppUserId id, CancellationToken ct) =>
        _db.Users.FirstOrDefaultAsync(x => x.Id == id, ct);
    public Task<AppUser?> GetByUsernameAsync(string username, CancellationToken ct) =>
        _db.Users.FirstOrDefaultAsync(x => x.Username.ToLower() == username.ToLower(), ct);
    public Task<bool> UsernameExistsAsync(string username, CancellationToken ct) =>
        _db.Users.AnyAsync(x => x.Username.ToLower() == username.ToLower(), ct);
    public Task<bool> AnyLinkedToRepAsync(RepresentativeId repId, CancellationToken ct) =>
        _db.Users.AnyAsync(x => x.RepresentativeId == repId, ct);
    public void Add(AppUser user) => _db.Users.Add(user);
    public void Remove(AppUser user) => _db.Users.Remove(user);
}

internal sealed class RoleRepository : IRoleRepository
{
    private readonly FollowUpDbContext _db;
    public RoleRepository(FollowUpDbContext db) => _db = db;

    public Task<Role?> GetByIdAsync(RoleId id, CancellationToken ct) =>
        _db.Roles.FirstOrDefaultAsync(x => x.Id == id, ct);
    public Task<Role?> GetByNameAsync(string name, CancellationToken ct) =>
        _db.Roles.FirstOrDefaultAsync(x => x.Name.ToLower() == name.ToLower(), ct);
    public Task<bool> IsInUseAsync(RoleId id, CancellationToken ct) =>
        _db.Users.AnyAsync(x => x.RoleId == id, ct);
    public void Add(Role role) => _db.Roles.Add(role);
    public void Remove(Role role) => _db.Roles.Remove(role);
}

internal sealed class UserSessionRepository : IUserSessionRepository
{
    private readonly FollowUpDbContext _db;
    public UserSessionRepository(FollowUpDbContext db) => _db = db;

    public Task<UserSession?> GetByIdAsync(UserSessionId id, CancellationToken ct) =>
        _db.Sessions.FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyList<UserSession>> GetActiveByUserAsync(AppUserId userId, CancellationToken ct) =>
        await _db.Sessions.Where(x => x.UserId == userId && x.RevokedAt == null).ToListAsync(ct);
    public Task<UserSession?> GetActiveByTokenHashAsync(string tokenHash, CancellationToken ct) =>
        _db.Sessions.FirstOrDefaultAsync(x => x.TokenHash == tokenHash && x.RevokedAt == null, ct);
    public void Add(UserSession session) => _db.Sessions.Add(session);
}
