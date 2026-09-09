using FluentAssertions;
using FollowUp.Application.Common.Abstractions;
using FollowUp.Domain.Emailing;
using FollowUp.Infrastructure.Emailing;
using Hangfire;
using Hangfire.Common;
using Xunit;

namespace FollowUp.IntegrationTests;

/// <summary>
/// Finding MSG-009: a stats-email subscription and its Hangfire recurring job are a dual write, so a rolled-back
/// create can leave a recurring job whose subscription never persisted. When that orphaned job fires and finds
/// no subscription it must remove itself, rather than run a daily no-op forever.
/// </summary>
public sealed class StatsEmailSelfHealTests
{
    private sealed class NotFoundRunner : IStatsEmailRunner
    {
        public Task<StatsEmailRunResult> RunAsync(StatsEmailSubscriptionId id, CancellationToken ct) =>
            Task.FromResult(new StatsEmailRunResult(false, 0, 0, "not-found"));
    }

    private sealed class SentRunner : IStatsEmailRunner
    {
        public Task<StatsEmailRunResult> RunAsync(StatsEmailSubscriptionId id, CancellationToken ct) =>
            Task.FromResult(new StatsEmailRunResult(true, 1, 0, "ok"));
    }

    private sealed class RecordingJobManager : IRecurringJobManager
    {
        public readonly List<string> Removed = new();
        public void AddOrUpdate(string recurringJobId, Job job, string cronExpression, RecurringJobOptions options) { }
        public void RemoveIfExists(string recurringJobId) => Removed.Add(recurringJobId);
        public void Trigger(string recurringJobId) { }
    }

    [Fact]
    public async Task An_orphaned_schedule_removes_itself_when_its_subscription_is_gone()
    {
        var jobs = new RecordingJobManager();
        var id = Guid.NewGuid();

        await new StatsEmailJobRunner(new NotFoundRunner(), jobs).RunAsync(id, CancellationToken.None);

        jobs.Removed.Should().ContainSingle().Which.Should().Be($"stats-email-{id}");
    }

    [Fact]
    public async Task A_live_subscription_run_leaves_its_schedule_intact()
    {
        var jobs = new RecordingJobManager();

        await new StatsEmailJobRunner(new SentRunner(), jobs).RunAsync(Guid.NewGuid(), CancellationToken.None);

        jobs.Removed.Should().BeEmpty("a subscription that still exists must keep its recurring job");
    }
}
