using System.Text.Json;
using FollowUp.Application.Common.Abstractions;
using FollowUp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FollowUp.Infrastructure.Jobs;

/// <summary>
/// Re-sends notification deliveries that failed a previous attempt, from the content stored on the delivery log
/// (finding M-13 / MSG-004). Previously the retry was dead — <c>ShouldRetry</c> was never called and nothing
/// reprocessed the log. Runs on a recurring schedule and is bounded by the attempt ceiling so a permanently
/// failing recipient can't loop forever.
/// </summary>
public sealed class NotificationDeliveryRetryRunner
{
    private const int MaxAttempts = 5;
    private const int BatchSize = 100;

    private readonly FollowUpDbContext _db;
    private readonly INotificationDispatcher _dispatcher;
    private readonly IClock _clock;
    private readonly ILogger<NotificationDeliveryRetryRunner> _logger;

    public NotificationDeliveryRetryRunner(FollowUpDbContext db, INotificationDispatcher dispatcher,
        IClock clock, ILogger<NotificationDeliveryRetryRunner> logger)
    {
        _db = db; _dispatcher = dispatcher; _clock = clock; _logger = logger;
    }

    public async Task<int> RunAsync(CancellationToken ct = default)
    {
        var pending = await _db.DeliveryLogs
            .Where(l => l.Status != "Sent" && l.Attempts < MaxAttempts)
            .OrderBy(l => l.QueuedAt)
            .Take(BatchSize)
            .ToListAsync(ct);

        var resent = 0;
        foreach (var log in pending)
        {
            var parameters = string.IsNullOrEmpty(log.ParametersJson)
                ? Array.Empty<string>()
                : JsonSerializer.Deserialize<string[]>(log.ParametersJson) ?? Array.Empty<string>();
            try
            {
                await _dispatcher.SendAsync(log.Channel, log.Recipient, log.EventKey, log.Subject, log.Body, parameters, ct);
                log.MarkSent(_clock.UtcNow);
                resent++;
            }
            catch (Exception ex)
            {
                log.MarkFailed(ex.Message, _clock.UtcNow);
                _logger.LogWarning(ex, "Notification delivery retry to {Recipient} on {Channel} failed",
                    log.Recipient, log.Channel.Name);
            }
        }

        if (pending.Count > 0) await _db.SaveChangesAsync(ct);
        return resent;
    }
}
