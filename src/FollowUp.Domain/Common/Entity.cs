namespace FollowUp.Domain.Common;

/// <summary>
/// Base class for domain entities. Identity-based equality and a private domain-event buffer.
/// </summary>
/// <typeparam name="TId">The strongly-typed identifier of the entity.</typeparam>
public abstract class Entity<TId> : IEquatable<Entity<TId>>
    where TId : notnull
{
    private readonly List<IDomainEvent> _domainEvents = new();

    protected Entity(TId id) => Id = id;

    // Required by EF Core materialization; not for domain use.
#pragma warning disable CS8618
    protected Entity() { }
#pragma warning restore CS8618

    public TId Id { get; protected init; }

    /// <summary>Domain events raised by this entity, awaiting dispatch after the transaction commits.</summary>
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected void Raise(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    public void ClearDomainEvents() => _domainEvents.Clear();

    public bool Equals(Entity<TId>? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (GetType() != other.GetType()) return false;
        return EqualityComparer<TId>.Default.Equals(Id, other.Id);
    }

    public override bool Equals(object? obj) => obj is Entity<TId> other && Equals(other);

    public override int GetHashCode() => EqualityComparer<TId>.Default.GetHashCode(Id);

    public static bool operator ==(Entity<TId>? left, Entity<TId>? right) => Equals(left, right);

    public static bool operator !=(Entity<TId>? left, Entity<TId>? right) => !Equals(left, right);
}
