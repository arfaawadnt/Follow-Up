using System.Text.Json;
using FollowUp.Application.Common.Abstractions;
using FollowUp.Domain.Audit;
using FollowUp.Domain.Common;
using FollowUp.Infrastructure.Persistence.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace FollowUp.Infrastructure.Persistence.Interceptors;

/// <summary>
/// The persistence cross-cutting core, applied atomically inside each SaveChanges:
/// (1) stamps IAuditable provenance, (2) writes an immutable <see cref="AuditEntry"/> for every state change
/// (SRS FR-20 / NFR-AUD-1 — automated mutations included, closing JOBS-002), and (3) moves aggregate domain
/// events into the <see cref="OutboxMessage"/> table in the same transaction (architect: Outbox pattern).
/// </summary>
public sealed class AuditAndOutboxInterceptor : SaveChangesInterceptor
{
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;

    public AuditAndOutboxInterceptor(ICurrentUser currentUser, IClock clock)
    {
        _currentUser = currentUser;
        _clock = clock;
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context is not null) Process(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not null) Process(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Process(DbContext context)
    {
        var now = _clock.UtcNow;
        var actor = _currentUser.IsAuthenticated ? _currentUser.Username : "system";
        var correlationId = _currentUser.CorrelationId;

        // Snapshot the tracked aggregate changes BEFORE we add audit/outbox rows (so they aren't re-processed).
        // UserSession is excluded — last-seen touches and session bookkeeping are not business audit events.
        var entries = context.ChangeTracker.Entries()
            .Where(e => e.Entity is not AuditEntry and not OutboxMessage and not Domain.Identity.UserSession)
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToList();

        var auditEntries = new List<AuditEntry>();
        var outboxMessages = new List<OutboxMessage>();

        foreach (var entry in entries)
        {
            StampProvenance(entry, actor, now);

            // Audit only aggregate roots / auditable entities (skip owned-type child rows).
            if (entry.Entity is IHasDomainEvents)
                auditEntries.Add(BuildAuditEntry(entry, actor, now, correlationId));

            if (entry.Entity is IHasDomainEvents aggregate)
            {
                foreach (var domainEvent in aggregate.DomainEvents)
                    outboxMessages.Add(ToOutbox(domainEvent));
                aggregate.ClearDomainEvents();
            }
        }

        if (auditEntries.Count > 0) context.Set<AuditEntry>().AddRange(auditEntries);
        if (outboxMessages.Count > 0) context.Set<OutboxMessage>().AddRange(outboxMessages);
    }

    private static void StampProvenance(EntityEntry entry, string actor, DateTimeOffset now)
    {
        if (entry.Entity is not IAuditable) return;
        if (entry.State == EntityState.Added)
        {
            entry.Property(nameof(IAuditable.CreatedAt)).CurrentValue = now;
            if (entry.Property(nameof(IAuditable.CreatedBy)).CurrentValue is null or "")
                entry.Property(nameof(IAuditable.CreatedBy)).CurrentValue = actor;
        }
        else if (entry.State == EntityState.Modified)
        {
            entry.Property(nameof(IAuditable.UpdatedAt)).CurrentValue = now;
            entry.Property(nameof(IAuditable.UpdatedBy)).CurrentValue = actor;
        }
    }

    private static AuditEntry BuildAuditEntry(EntityEntry entry, string actor, DateTimeOffset now, string? correlationId)
    {
        var entity = entry.Entity.GetType().Name;
        var idProp = entry.Properties.FirstOrDefault(p => p.Metadata.IsPrimaryKey());
        var entityId = idProp?.CurrentValue?.ToString() ?? string.Empty;
        var action = entry.State switch
        {
            EntityState.Added => "Create",
            EntityState.Modified => "Update",
            EntityState.Deleted => "Delete",
            _ => "Unknown",
        };

        string? before = entry.State is EntityState.Modified or EntityState.Deleted ? Serialize(entry, original: true) : null;
        string? after = entry.State is EntityState.Added or EntityState.Modified ? Serialize(entry, original: false) : null;

        return AuditEntry.Record(now, actor, entity, entityId, action, before, after, correlationId);
    }

    private static string Serialize(EntityEntry entry, bool original)
    {
        var dict = new Dictionary<string, object?>();
        foreach (var p in entry.Properties)
        {
            if (p.Metadata.IsPrimaryKey()) continue;
            dict[p.Metadata.Name] = original ? p.OriginalValue : p.CurrentValue;
        }
        return JsonSerializer.Serialize(dict);
    }

    private static OutboxMessage ToOutbox(IDomainEvent domainEvent) => new()
    {
        Type = domainEvent.GetType().Name,
        Content = JsonSerializer.Serialize(domainEvent, domainEvent.GetType()),
        OccurredOn = domainEvent.OccurredOn,
    };
}
