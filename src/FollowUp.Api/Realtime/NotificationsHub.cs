using FollowUp.Application.Common.Abstractions;
using Microsoft.AspNetCore.SignalR;

namespace FollowUp.Api.Realtime;

/// <summary>
/// Real-time hub for data-change hints and in-app notifications (ADR-0003). Every connection is authenticated
/// (the token arrives via the access-token query, validated by the token-auth middleware); each connection
/// joins a per-user group so pushes are scoped to the recipient. Messages are hints — the client re-fetches
/// through the normal scope-enforced query path, which stays the system of record.
/// </summary>
public sealed class NotificationsHub : Hub
{
    private readonly ICurrentUser _currentUser;
    public NotificationsHub(ICurrentUser currentUser) => _currentUser = currentUser;

    public override async Task OnConnectedAsync()
    {
        if (!_currentUser.IsAuthenticated)
        {
            Context.Abort();
            return;
        }
        await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(_currentUser.UserId.Value));
        await base.OnConnectedAsync();
    }

    public static string UserGroup(Guid userId) => $"user:{userId:N}";
}
