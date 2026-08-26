using FollowUp.Application.Common.Abstractions;
using FollowUp.Application.Common.Models;
using FollowUp.Application.Features.Audit;
using FollowUp.Application.Features.Auth;
using FollowUp.Application.Features.Setup;
using FollowUp.Application.Features.UserAdmin.Queries;
using FollowUp.Domain.Identity;
using FollowUp.Domain.Reference;
using Microsoft.EntityFrameworkCore;

namespace FollowUp.Infrastructure.Persistence.Queries;

internal sealed class UserAdminQueries : IUserAdminQueries
{
    private readonly FollowUpDbContext _db;
    private readonly IClock _clock;
    public UserAdminQueries(FollowUpDbContext db, IClock clock) { _db = db; _clock = clock; }

    public async Task<PagedResult<UserListItemDto>> SearchUsersAsync(ListQuery query, CancellationToken ct)
    {
        var q = _db.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Search))
            q = q.Where(u => EF.Functions.ILike(u.Username, $"%{query.Search.Trim()}%"));

        var total = await q.CountAsync(ct);
        var now = _clock.UtcNow;
        var rows = await (from u in q
                          join r in _db.Roles.AsNoTracking() on u.RoleId equals r.Id
                          orderby u.Username
                          select new { u.Id, u.Username, u.DisplayName, RoleName = r.Name, u.Language, Role = r, u.Email, u.IsActive, u.LockedUntil })
                         .Skip(query.Skip).Take(query.PageSize).ToListAsync(ct);

        var items = rows.Select(u => new UserListItemDto(
            u.Id.Value, u.Username, u.DisplayName, u.RoleName, u.Language, u.Role.EffectivePrivileges.Count,
            u.Email, u.IsActive, u.LockedUntil.HasValue && u.LockedUntil > now)).ToList();
        return PagedResult<UserListItemDto>.Create(items, total, query.Page, query.PageSize);
    }

    public async Task<IReadOnlyList<UserLookupDto>> LookupUsersAsync(string? search, CancellationToken ct)
    {
        var q = _db.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
            q = q.Where(u => EF.Functions.ILike(u.Username, $"%{search.Trim()}%"));
        return await q.OrderBy(u => u.Username).Take(50)
            .Select(u => new UserLookupDto(u.Id.Value, u.Username)).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<RoleDto>> GetRolesAsync(CancellationToken ct)
    {
        var roles = await _db.Roles.AsNoTracking().OrderBy(r => r.Name).ToListAsync(ct);
        return roles.Select(r => new RoleDto(
            r.Id.Value, r.Name, r.Privileges.ToList(), r.DefaultLanguage, r.DefaultTheme, r.IsBuiltIn,
            new RoleScopeDto(r.Scope.Branches.ToList(), r.Scope.Governorates.ToList(), r.Scope.Cities.ToList(),
                r.Scope.Areas.ToList(), r.Scope.Categories.ToList(), r.Scope.Segments.ToList()))).ToList();
    }
}

internal sealed class SetupQueries : ISetupQueries
{
    private readonly FollowUpDbContext _db;
    public SetupQueries(FollowUpDbContext db) => _db = db;

    public async Task<IReadOnlyList<RefItemDto>> GetRefItemsAsync(string? type, CancellationToken ct)
    {
        var q = _db.RefItems.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(type))
        {
            var refType = Domain.Common.Enumeration.FromName<RefType>(type);
            q = q.Where(x => x.Type == refType);
        }
        var rows = await q.OrderBy(x => x.SortOrder).ThenBy(x => x.NameEn).ToListAsync(ct);
        return rows.Select(x => new RefItemDto(x.Id.Value, x.Type.Name, x.Code, x.NameEn, x.NameAr, x.SortOrder)).ToList();
    }

    public async Task<IReadOnlyList<CityDto>> GetCitiesAsync(CancellationToken ct) =>
        await _db.Cities.AsNoTracking().OrderBy(c => c.Name)
            .Select(c => new CityDto(c.Id.Value, c.Name, c.Governorate)).ToListAsync(ct);

    public async Task<IReadOnlyList<AreaDto>> GetAreasAsync(CancellationToken ct)
    {
        var rows = await _db.Areas.AsNoTracking().OrderBy(a => a.Name).ToListAsync(ct);
        return rows.Select(a => new AreaDto(
            a.Id.Value, a.Name, a.CityId.Value, a.TransportationRequired,
            a.TransferReps.Select(r => r.Value).ToList())).ToList();
    }
}

internal sealed class AuditQueries : IAuditQueries
{
    private readonly FollowUpDbContext _db;
    public AuditQueries(FollowUpDbContext db) => _db = db;

    public async Task<PagedResult<AuditRowDto>> SearchAsync(AuditSearchCriteria criteria, CancellationToken ct)
    {
        var q = _db.AuditEntries.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(criteria.Entity)) q = q.Where(a => a.Entity == criteria.Entity);
        if (!string.IsNullOrWhiteSpace(criteria.Actor)) q = q.Where(a => a.Actor == criteria.Actor);
        if (!string.IsNullOrWhiteSpace(criteria.Action)) q = q.Where(a => a.Action == criteria.Action);
        if (criteria.From is { } from) q = q.Where(a => a.OccurredAt >= new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));
        if (criteria.To is { } to) q = q.Where(a => a.OccurredAt <= new DateTimeOffset(to.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero));

        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(a => a.OccurredAt)
            .Skip(criteria.Skip).Take(criteria.PageSize)
            .Select(a => new AuditRowDto(a.Id.Value, a.OccurredAt, a.Actor, a.Entity, a.EntityId, a.Action, a.BeforeJson, a.AfterJson, a.CorrelationId))
            .ToListAsync(ct);
        return PagedResult<AuditRowDto>.Create(items, total, criteria.Page, criteria.PageSize);
    }
}

internal sealed class SessionQueries : ISessionQueries
{
    private readonly FollowUpDbContext _db;
    public SessionQueries(FollowUpDbContext db) => _db = db;

    public async Task<IReadOnlyList<SessionDto>> GetForUserAsync(AppUserId userId, CancellationToken ct) =>
        await _db.Sessions.AsNoTracking().Where(s => s.UserId == userId)
            .OrderByDescending(s => s.IssuedAt)
            .Select(s => new SessionDto(s.Id.Value, s.IssuedAt, s.LastSeenAt, s.ExpiresAt, s.RevokedAt != null, s.Ip))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<AdminSessionDto>> GetAllAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var rows = await (from s in _db.Sessions.AsNoTracking()
                          join u in _db.Users.AsNoTracking() on s.UserId equals u.Id
                          orderby s.IssuedAt descending
                          select new { s.Id, u.Username, s.Ip, s.UserAgent, s.IssuedAt, s.LastSeenAt, s.ExpiresAt, s.RevokedAt })
                         .Take(500).ToListAsync(ct);
        return rows.Select(r =>
        {
            var end = r.RevokedAt ?? r.LastSeenAt;
            var status = r.RevokedAt != null ? "Revoked" : r.ExpiresAt < now ? "Expired" : "Active";
            return new AdminSessionDto(r.Id.Value, r.Username, r.Ip, r.UserAgent, r.IssuedAt, r.RevokedAt, r.LastSeenAt,
                (long)Math.Max(0, (end - r.IssuedAt).TotalSeconds), status);
        }).ToList();
    }
}
