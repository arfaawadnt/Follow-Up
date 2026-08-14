namespace FollowUp.Domain.Common;

/// <summary>Convenience base that stamps <see cref="OccurredOn"/> at construction.</summary>
public abstract record DomainEvent : IDomainEvent
{
    public DateTimeOffset OccurredOn { get; init; } = DateTimeOffset.UtcNow;
}
