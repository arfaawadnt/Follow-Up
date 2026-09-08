using FluentAssertions;
using FollowUp.Application.Common.Abstractions;
using FollowUp.Domain.Notifications;
using FollowUp.Infrastructure.Jobs;
using FollowUp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace FollowUp.IntegrationTests;

/// <summary>
/// Finding M-13 / MSG-004: the delivery retry was dead — nothing reprocessed a failed NotificationDeliveryLog.
/// The retry runner now re-sends failed deliveries from the content stored on the log and marks them Sent.
/// </summary>
[Collection("integration")]
public sealed class NotificationRetryTests
{
    private readonly IntegrationFixture _fx;
    public NotificationRetryTests(IntegrationFixture fx) => _fx = fx;

    private sealed class SucceedingDispatcher : INotificationDispatcher
    {
        public int Sent;
        public Task SendAsync(NotificationChannel channel, string recipient, string eventKey,
            string? subject, string? body, IReadOnlyList<string> parameters, CancellationToken ct)
        {
            Sent++;
            return Task.CompletedTask;
        }
    }

    [SkippableFact]
    public async Task Retry_runner_resends_a_failed_delivery_and_marks_it_sent()
    {
        Skip.IfNot(_fx.DatabaseAvailable, "FOLLOWUP_DB not set.");
        await _fx.ResetAsync(); // clears notification_delivery_log

        Guid logId;
        using (var scope = _fx.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
            var log = NotificationDeliveryLog.Queue(NotificationChannel.Mail, "ops@lab.local", "complaint.logged",
                "New complaint", "<p>body</p>", null, DateTimeOffset.UtcNow);
            log.MarkFailed("smtp temporarily down", DateTimeOffset.UtcNow); // Failed, Attempts = 1
            db.DeliveryLogs.Add(log);
            await db.SaveChangesAsync();
            logId = log.Id.Value;
        }

        var dispatcher = new SucceedingDispatcher();
        using (var scope = _fx.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
            var clock = scope.ServiceProvider.GetRequiredService<IClock>();
            var runner = new NotificationDeliveryRetryRunner(db, dispatcher, clock,
                NullLogger<NotificationDeliveryRetryRunner>.Instance);

            var resent = await runner.RunAsync();

            resent.Should().Be(1);
        }

        dispatcher.Sent.Should().Be(1, "the failed delivery is re-sent from its stored content");

        using (var scope = _fx.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
            var log = await db.DeliveryLogs.FirstAsync(l => l.Id == new NotificationDeliveryLogId(logId));
            log.Status.Should().Be("Sent");
            log.Attempts.Should().Be(2); // initial failure + successful retry
        }
    }
}
