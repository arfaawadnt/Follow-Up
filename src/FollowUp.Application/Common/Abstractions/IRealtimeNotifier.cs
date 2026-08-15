namespace FollowUp.Application.Common.Abstractions;

/// <summary>
/// Pushes real-time hints to connected clients (ADR-0003; Workflows §2.1 "every action broadcasts a refetch
/// signal"). Implemented by SignalR in the API; a no-op elsewhere (jobs/tests without a hub). Messages are
/// hints only — the client re-fetches through the normal scope-enforced query path.
/// </summary>
public interface IRealtimeNotifier
{
    /// <summary>Broadcasts a "something changed, refetch" hint (entity-type label) to all connected clients.</summary>
    Task DataChangedAsync(string entityType, CancellationToken ct = default);

    /// <summary>Pushes an in-app notification to a single user's group.</summary>
    Task NotifyUserAsync(Guid userId, string title, CancellationToken ct = default);
}
