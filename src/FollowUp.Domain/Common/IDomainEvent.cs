namespace FollowUp.Domain.Common;

/// <summary>
/// Marker for something that happened in the domain and other parts of the system may react to.
/// Kept free of any framework type (no MediatR) so the Domain layer has zero external dependencies;
/// the Application layer adapts these to its dispatch mechanism.
/// </summary>
public interface IDomainEvent
{
    /// <summary>When the event occurred (UTC).</summary>
    DateTimeOffset OccurredOn { get; }
}

/// <summary>
/// Non-generic access to an entity's raised domain events, so infrastructure (the outbox interceptor) can
/// collect them without knowing the entity's id type.
/// </summary>
public interface IHasDomainEvents
{
    IReadOnlyCollection<IDomainEvent> DomainEvents { get; }
    void ClearDomainEvents();
}
