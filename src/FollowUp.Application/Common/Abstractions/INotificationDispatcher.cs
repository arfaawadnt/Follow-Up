using FollowUp.Domain.Notifications;

namespace FollowUp.Application.Common.Abstractions;

/// <summary>
/// Sends one rendered notification over an external channel (Mail/WhatsApp). Used by the initial fan-out AND
/// by the delivery-retry job, so the per-channel send lives in exactly one place (finding M-13). Implemented in
/// Infrastructure over the concrete email/WhatsApp gateways.
/// </summary>
public interface INotificationDispatcher
{
    Task SendAsync(NotificationChannel channel, string recipient, string eventKey,
        string? subject, string? body, IReadOnlyList<string> parameters, CancellationToken ct);
}
