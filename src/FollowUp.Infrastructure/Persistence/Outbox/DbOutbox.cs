using System.Text.Json;
using FollowUp.Application.Common.Abstractions;
using FollowUp.Domain.Common;

namespace FollowUp.Infrastructure.Persistence.Outbox;

/// <summary>
/// Explicit enqueue path for <see cref="IOutbox"/>. Most events are collected automatically from aggregates
/// by the save interceptor; this is for events raised outside an aggregate. Written in the current DbContext
/// so it commits atomically with the surrounding change.
/// </summary>
public sealed class DbOutbox : IOutbox
{
    private readonly FollowUpDbContext _db;
    public DbOutbox(FollowUpDbContext db) => _db = db;

    public void Enqueue(IDomainEvent domainEvent) =>
        _db.OutboxMessages.Add(new OutboxMessage
        {
            Type = domainEvent.GetType().Name,
            Content = JsonSerializer.Serialize(domainEvent, domainEvent.GetType()),
            OccurredOn = domainEvent.OccurredOn,
        });
}
