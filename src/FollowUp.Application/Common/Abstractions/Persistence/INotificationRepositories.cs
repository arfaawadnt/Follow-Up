using FollowUp.Domain.Identity;
using FollowUp.Domain.Notifications;

namespace FollowUp.Application.Common.Abstractions.Persistence;

/// <summary>Aggregate repository for <see cref="SystemNotification"/> (in-app feed).</summary>
public interface ISystemNotificationRepository
{
    Task<SystemNotification?> GetByIdAsync(SystemNotificationId id, CancellationToken ct);
    Task<IReadOnlyList<SystemNotification>> GetUnreadForUserAsync(AppUserId userId, CancellationToken ct);
    void Add(SystemNotification notification);
}

/// <summary>Aggregate repository for <see cref="NotificationPreference"/>.</summary>
public interface INotificationPreferenceRepository
{
    Task<IReadOnlyList<NotificationPreference>> GetForUserAsync(AppUserId userId, CancellationToken ct);
    Task<NotificationPreference?> GetAsync(AppUserId userId, string eventKey, CancellationToken ct);
    void Add(NotificationPreference preference);
}

/// <summary>Aggregate repository for <see cref="NotificationDeliveryLog"/>.</summary>
public interface INotificationDeliveryLogRepository
{
    Task<NotificationDeliveryLog?> GetByIdAsync(NotificationDeliveryLogId id, CancellationToken ct);
    void Add(NotificationDeliveryLog log);
}

/// <summary>Aggregate repository for the singleton <see cref="Domain.Integration.OracleConfig"/>.</summary>
public interface IOracleConfigRepository
{
    Task<Domain.Integration.OracleConfig?> GetAsync(CancellationToken ct);
    void Add(Domain.Integration.OracleConfig config);
}
