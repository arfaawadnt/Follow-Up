namespace FollowUp.Domain.Common;

/// <summary>
/// Marks the consistency boundary of a cluster of entities/value objects. Only aggregate roots
/// are addressed by repositories; invariants spanning the cluster are enforced here.
/// </summary>
/// <typeparam name="TId">The strongly-typed identifier of the aggregate root.</typeparam>
public abstract class AggregateRoot<TId> : Entity<TId>
    where TId : notnull
{
    protected AggregateRoot(TId id) : base(id) { }

    protected AggregateRoot() { }
}
