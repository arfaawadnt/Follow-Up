using FollowUp.Domain.Common;

namespace FollowUp.Domain.Audit;

public readonly record struct AuditEntryId(Guid Value)
{
    public static AuditEntryId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}

/// <summary>
/// An append-only audit record (SRS FR-20). Every state change writes exactly one entry, atomically with
/// the change. The table is immutable at the database (triggers refuse UPDATE/DELETE/TRUNCATE); the only
/// deletion path is the bounded retention purge, which audits itself first. There are therefore no mutating
/// methods here — an entry is created once and never changes.
/// </summary>
public sealed class AuditEntry : AggregateRoot<AuditEntryId>
{
    private AuditEntry() { } // EF

    private AuditEntry(AuditEntryId id, DateTimeOffset occurredAt, string actor, string entity,
        string entityId, string action, string? beforeJson, string? afterJson, string? correlationId)
        : base(id)
    {
        OccurredAt = occurredAt;
        Actor = actor;
        Entity = entity;
        EntityId = entityId;
        Action = action;
        BeforeJson = beforeJson;
        AfterJson = afterJson;
        CorrelationId = correlationId;
    }

    public DateTimeOffset OccurredAt { get; private set; }
    public string Actor { get; private set; } = null!;
    public string Entity { get; private set; } = null!;
    public string EntityId { get; private set; } = null!;
    public string Action { get; private set; } = null!;
    public string? BeforeJson { get; private set; }
    public string? AfterJson { get; private set; }
    public string? CorrelationId { get; private set; }

    public static AuditEntry Record(DateTimeOffset occurredAt, string actor, string entity, string entityId,
        string action, string? beforeJson, string? afterJson, string? correlationId)
    {
        if (string.IsNullOrWhiteSpace(entity)) throw new DomainException("Audit entity is required.");
        if (string.IsNullOrWhiteSpace(action)) throw new DomainException("Audit action is required.");
        return new AuditEntry(AuditEntryId.New(), occurredAt, actor ?? "system", entity, entityId ?? string.Empty,
            action, beforeJson, afterJson, correlationId);
    }
}
