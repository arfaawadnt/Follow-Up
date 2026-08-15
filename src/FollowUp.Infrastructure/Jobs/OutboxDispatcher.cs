using System.Text.Json;
using FollowUp.Application.Common.Messaging;
using FollowUp.Domain.Common;
using FollowUp.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FollowUp.Infrastructure.Jobs;

/// <summary>
/// Drains the outbox (architect: Outbox pattern). Unprocessed messages are published as MediatR notifications
/// — any registered notification handler (e.g. the notification pipeline) reacts — then stamped processed.
/// Failures are recorded with a bounded attempt count so a poison message doesn't wedge the queue (JOBS-006).
/// </summary>
public sealed class OutboxDispatcher
{
    private const int BatchSize = 100;
    private const int MaxAttempts = 5;

    private readonly FollowUpDbContext _db;
    private readonly IPublisher _publisher;
    private readonly ILogger<OutboxDispatcher> _logger;

    public OutboxDispatcher(FollowUpDbContext db, IPublisher publisher, ILogger<OutboxDispatcher> logger)
    {
        _db = db;
        _publisher = publisher;
        _logger = logger;
    }

    public async Task<int> DispatchAsync(CancellationToken ct = default)
    {
        var messages = await _db.OutboxMessages
            .Where(m => m.ProcessedAt == null && m.Attempts < MaxAttempts)
            .OrderBy(m => m.OccurredOn)
            .Take(BatchSize)
            .ToListAsync(ct);

        var dispatched = 0;
        foreach (var message in messages)
        {
            try
            {
                var type = ResolveType(message.Type);
                if (type is not null && JsonSerializer.Deserialize(message.Content, type) is IDomainEvent domainEvent)
                    await _publisher.Publish(new DomainEventNotification(domainEvent), ct);

                message.ProcessedAt = DateTimeOffset.UtcNow;
                dispatched++;
            }
            catch (Exception ex)
            {
                message.Attempts++;
                message.Error = ex.Message;
                _logger.LogWarning(ex, "Outbox message {Id} ({Type}) failed (attempt {Attempts})", message.Id, message.Type, message.Attempts);
            }
        }

        if (messages.Count > 0) await _db.SaveChangesAsync(ct);
        return dispatched;
    }

    private static Type? ResolveType(string shortName) =>
        AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => a.FullName?.StartsWith("FollowUp.Domain", StringComparison.Ordinal) == true)
            .SelectMany(a => a.GetTypes())
            .FirstOrDefault(t => t.Name == shortName && typeof(IDomainEvent).IsAssignableFrom(t));
}
