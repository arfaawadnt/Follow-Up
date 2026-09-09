using FluentAssertions;
using FollowUp.Infrastructure.Jobs;
using FollowUp.Infrastructure.Persistence;
using FollowUp.Infrastructure.Persistence.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FollowUp.IntegrationTests;

/// <summary>
/// Finding M-9 / ST-6: a message whose processing fails must not be marked processed (it stays retryable) and
/// its handler's partial writes must not persist — each message is dispatched in its own transaction, so a
/// failure rolls back and only the bounded attempt count is recorded.
/// </summary>
[Collection("integration")]
public sealed class OutboxRetryTests
{
    private readonly IntegrationFixture _fx;
    public OutboxRetryTests(IntegrationFixture fx) => _fx = fx;

    [SkippableFact]
    public async Task A_failing_message_increments_attempts_and_stays_retryable()
    {
        Skip.IfNot(_fx.DatabaseAvailable, "FOLLOWUP_DB not set.");
        await _fx.ResetAsync();

        Guid msgId;
        using (var scope = _fx.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
            // Valid event type but valid-JSON-of-the-wrong-shape (an array, not the event object), so
            // deserialization throws inside the dispatcher. (The content column is jsonb, so it must be valid JSON.)
            var msg = new OutboxMessage
            {
                Type = "ComplaintLogged",
                Content = "[]",
                OccurredOn = DateTimeOffset.UtcNow,
            };
            db.OutboxMessages.Add(msg);
            await db.SaveChangesAsync();
            msgId = msg.Id;
        }

        using (var scope = _fx.Services.CreateScope())
            (await scope.ServiceProvider.GetRequiredService<OutboxDispatcher>().DispatchAsync())
                .Should().Be(0, "the poison message is not successfully dispatched");

        using (var scope = _fx.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
            var msg = await db.OutboxMessages.FirstAsync(m => m.Id == msgId);
            msg.Attempts.Should().Be(1);
            msg.ProcessedAt.Should().BeNull("a failed message must stay retryable, not be stamped processed");
            msg.Error.Should().NotBeNullOrEmpty();
        }
    }

    [SkippableFact]
    public async Task A_message_stops_being_retried_once_it_reaches_the_attempt_limit()
    {
        // Finding PLT-014: after MaxAttempts (5) a poison message is dead-lettered — excluded from every future
        // batch (and logged at Error) rather than retried forever. Its attempt count caps and it stays unprocessed.
        Skip.IfNot(_fx.DatabaseAvailable, "FOLLOWUP_DB not set.");
        await _fx.ResetAsync();

        Guid msgId;
        using (var scope = _fx.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
            var msg = new OutboxMessage { Type = "ComplaintLogged", Content = "[]", OccurredOn = DateTimeOffset.UtcNow };
            db.OutboxMessages.Add(msg);
            await db.SaveChangesAsync();
            msgId = msg.Id;
        }

        // Run the dispatcher more times than the limit; each run fails and bumps Attempts until it caps.
        for (var i = 0; i < 7; i++)
            using (var scope = _fx.Services.CreateScope())
                await scope.ServiceProvider.GetRequiredService<OutboxDispatcher>().DispatchAsync();

        using (var scope = _fx.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
            var msg = await db.OutboxMessages.FirstAsync(m => m.Id == msgId);
            msg.Attempts.Should().Be(5, "the message stops being retried at MaxAttempts");
            msg.ProcessedAt.Should().BeNull("a dead-lettered message is never marked processed");
        }
    }
}
