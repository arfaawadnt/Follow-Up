using System.Text.Json;
using FollowUp.Application.Common.Messaging;
using FollowUp.Domain.Common;
using FollowUp.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FollowUp.Infrastructure.Jobs;

/// <summary>
/// Drains the outbox (architect: Outbox pattern). Unprocessed messages are published as MediatR notifications
/// — any registered notification handler reacts — then stamped processed. Each message is processed in its OWN
/// scope and transaction, so a failing handler's partial writes roll back together with the processed stamp;
/// a later retry re-runs cleanly and cannot duplicate side effects (finding M-9 / ST-6). Only the bounded
/// attempt count is persisted on failure, so a poison message can't wedge the queue (JOBS-006).
/// </summary>
public sealed class OutboxDispatcher
{
    private const int BatchSize = 100;
    private const int MaxAttempts = 5;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OutboxDispatcher> _logger;

    public OutboxDispatcher(IServiceScopeFactory scopeFactory, ILogger<OutboxDispatcher> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<int> DispatchAsync(CancellationToken ct = default)
    {
        List<Guid> ids;
        using (var readScope = _scopeFactory.CreateScope())
        {
            var db = readScope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
            ids = await db.OutboxMessages
                .Where(m => m.ProcessedAt == null && m.Attempts < MaxAttempts)
                .OrderBy(m => m.OccurredOn)
                .Take(BatchSize)
                .Select(m => m.Id)
                .ToListAsync(ct);
        }

        var dispatched = 0;
        foreach (var id in ids)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
            var publisher = scope.ServiceProvider.GetRequiredService<IPublisher>();

            var message = await db.OutboxMessages.FirstOrDefaultAsync(m => m.Id == id, ct);
            if (message is null || message.ProcessedAt is not null) continue; // already handled by a prior run

            Exception? failure = null;
            await using (var tx = await db.Database.BeginTransactionAsync(ct))
            {
                try
                {
                    var type = ResolveType(message.Type);
                    if (type is not null && JsonSerializer.Deserialize(message.Content, type) is IDomainEvent domainEvent)
                        await publisher.Publish(new DomainEventNotification(domainEvent), ct);

                    message.ProcessedAt = DateTimeOffset.UtcNow;
                    await db.SaveChangesAsync(ct);
                    await tx.CommitAsync(ct);
                    dispatched++;
                }
                catch (Exception ex)
                {
                    // Roll back the handler's partial writes together with the processed stamp, so a retry re-runs
                    // cleanly. The failure is recorded below, after the transaction is disposed.
                    await tx.RollbackAsync(ct);
                    failure = ex;
                }
            }

            if (failure is not null)
            {
                // Record only the bounded attempt count, in a fresh (non-transactional) state — the rolled-back
                // handler writes are discarded so the message is retried, never silently half-applied.
                db.ChangeTracker.Clear();
                var failed = await db.OutboxMessages.FirstOrDefaultAsync(m => m.Id == id, ct);
                if (failed is not null)
                {
                    failed.Attempts++;
                    failed.Error = failure.Message;
                    await db.SaveChangesAsync(ct);
                }
                _logger.LogWarning(failure, "Outbox message {Id} ({Type}) failed (attempt {Attempts})",
                    id, message.Type, message.Attempts + 1);
            }
        }

        return dispatched;
    }

    private static Type? ResolveType(string shortName) =>
        AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => a.FullName?.StartsWith("FollowUp.Domain", StringComparison.Ordinal) == true)
            .SelectMany(a => a.GetTypes())
            .FirstOrDefault(t => t.Name == shortName && typeof(IDomainEvent).IsAssignableFrom(t));
}
