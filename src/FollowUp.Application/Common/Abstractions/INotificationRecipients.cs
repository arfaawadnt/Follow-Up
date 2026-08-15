namespace FollowUp.Application.Common.Abstractions;

/// <summary>A user who should receive a notification, with the contact details a channel needs.</summary>
public sealed record NotificationRecipient(Guid UserId, string Language, string? Email, string? Phone);

/// <summary>
/// Resolves who receives an event's notification (SRS FR-16). Recipients are the active users whose role
/// grants a relevant privilege (a pragmatic, deterministic recipient rule — see docs/ASSUMPTIONS.md).
/// Implemented in Infrastructure.
/// </summary>
public interface INotificationRecipients
{
    Task<IReadOnlyList<NotificationRecipient>> ForPrivilegeAsync(string privilege, CancellationToken ct);
}
