using FollowUp.Domain.Common;

namespace FollowUp.Application.Common.Abstractions;

/// <summary>
/// Reliable domain-event publication (architect: Outbox pattern). Events raised by aggregates are enqueued
/// in the same database transaction as the state change and dispatched afterwards, so a committed change and
/// its notifications never diverge. Implemented in Infrastructure.
/// </summary>
public interface IOutbox
{
    /// <summary>Enqueues a domain event for post-commit dispatch, within the current transaction.</summary>
    void Enqueue(IDomainEvent domainEvent);
}
