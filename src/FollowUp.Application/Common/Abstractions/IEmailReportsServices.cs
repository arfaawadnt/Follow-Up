using FollowUp.Domain.Emailing;

namespace FollowUp.Application.Common.Abstractions;

/// <summary>
/// Registers/removes a per-subscription daily Hangfire recurring schedule (implemented in Infrastructure so the
/// Application layer stays free of Hangfire). Called by the subscription command handlers and at startup.
/// </summary>
public interface IStatsEmailScheduler
{
    /// <summary>Adds or updates the recurring schedule for a subscription (removes it when disabled).</summary>
    void Schedule(StatsEmailSubscription subscription);
    /// <summary>Removes a subscription's recurring schedule.</summary>
    void Unschedule(StatsEmailSubscriptionId id);
    /// <summary>Re-registers every enabled subscription's schedule (startup reconciliation).</summary>
    Task SyncAllAsync(CancellationToken ct);
}

public sealed record StatsEmailRunResult(bool Sent, int Recipients, int Failures, string Status);

/// <summary>
/// Renders a subscription's statistics reports for its window and emails them to its recipients. Used by the
/// nightly Hangfire job and the "Send now" command. Never throws for a single failed recipient.
/// </summary>
public interface IStatsEmailRunner
{
    Task<StatsEmailRunResult> RunAsync(StatsEmailSubscriptionId id, CancellationToken ct);
}
