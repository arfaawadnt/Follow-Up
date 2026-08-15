namespace FollowUp.Infrastructure.Persistence.Outbox;

/// <summary>
/// A persisted domain event awaiting dispatch (architect: Outbox pattern). Written in the SAME transaction
/// as the aggregate change, so a committed state change and its published events never diverge. A background
/// dispatcher publishes unprocessed rows and stamps <see cref="ProcessedAt"/>.
/// </summary>
public sealed class OutboxMessage
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Type { get; init; } = null!;      // domain event CLR type (assembly-qualified name is not used; short name)
    public string Content { get; init; } = null!;   // JSON payload
    public DateTimeOffset OccurredOn { get; init; }
    public DateTimeOffset? ProcessedAt { get; set; }
    public string? Error { get; set; }
    public int Attempts { get; set; }
}
