using FollowUp.Application.Common.Abstractions;
using FollowUp.Application.Common.Abstractions.Persistence;
using FollowUp.Domain.Notifications;
using FollowUp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FollowUp.Infrastructure.Notifications;

internal sealed class NotificationTemplateRepository : INotificationTemplateRepository
{
    private readonly FollowUpDbContext _db;
    public NotificationTemplateRepository(FollowUpDbContext db) => _db = db;

    public Task<NotificationTemplate?> GetByEventKeyAsync(string eventKey, CancellationToken ct) =>
        _db.NotificationTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.EventKey == eventKey, ct);
}

/// <summary>
/// Resolves recipients as the active users whose role grants the given privilege (privilege expansion is
/// computed on the materialized roles, since the jsonb privilege set can't be queried in SQL).
/// </summary>
internal sealed class NotificationRecipients : INotificationRecipients
{
    private readonly FollowUpDbContext _db;
    public NotificationRecipients(FollowUpDbContext db) => _db = db;

    public async Task<IReadOnlyList<NotificationRecipient>> ForPrivilegeAsync(string privilege, CancellationToken ct)
    {
        var roles = await _db.Roles.AsNoTracking().ToListAsync(ct);
        var roleIds = roles.Where(r => r.Has(privilege)).Select(r => r.Id).ToList();
        if (roleIds.Count == 0) return Array.Empty<NotificationRecipient>();

        var users = await _db.Users.AsNoTracking()
            .Where(u => u.IsActive && roleIds.Contains(u.RoleId))
            .ToListAsync(ct);

        return users.Select(u => new NotificationRecipient(u.Id.Value, u.Language, u.Email, u.Phone)).ToList();
    }
}

/// <summary>No-op realtime notifier for jobs/tests without a SignalR hub; the API overrides with the real one.</summary>
public sealed class NullRealtimeNotifier : IRealtimeNotifier
{
    public Task DataChangedAsync(string entityType, CancellationToken ct = default) => Task.CompletedTask;
    public Task NotifyUserAsync(Guid userId, string title, CancellationToken ct = default) => Task.CompletedTask;
}
