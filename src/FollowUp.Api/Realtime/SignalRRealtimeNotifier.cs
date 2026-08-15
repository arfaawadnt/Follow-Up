using FollowUp.Application.Common.Abstractions;
using Microsoft.AspNetCore.SignalR;

namespace FollowUp.Api.Realtime;

/// <summary>
/// SignalR implementation of <see cref="IRealtimeNotifier"/> (ADR-0003). Broadcasts refetch hints to all
/// clients and pushes per-user notifications to the recipient's group. Payloads are minimal (hints, not data).
/// </summary>
public sealed class SignalRRealtimeNotifier : IRealtimeNotifier
{
    private readonly IHubContext<NotificationsHub> _hub;
    public SignalRRealtimeNotifier(IHubContext<NotificationsHub> hub) => _hub = hub;

    public Task DataChangedAsync(string entityType, CancellationToken ct = default) =>
        _hub.Clients.All.SendAsync("dataChange", entityType, ct);

    public Task NotifyUserAsync(Guid userId, string title, CancellationToken ct = default) =>
        _hub.Clients.Group(NotificationsHub.UserGroup(userId)).SendAsync("notification", title, ct);
}
