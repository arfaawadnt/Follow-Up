using FluentAssertions;
using FollowUp.Application.Common.Abstractions;
using FollowUp.Application.Common.Abstractions.Persistence;
using FollowUp.Application.Features.EmailReports;
using FollowUp.Application.Tests.Common;
using FollowUp.Domain.Emailing;
using FollowUp.Domain.Identity;

namespace FollowUp.Application.Tests.Features.EmailReports;

/// <summary>
/// Finding B-7 / MSG-001: a stats email subscription must be rendered under the creating admin's org scope, so
/// a scoped admin's report can never egress company-wide data. This test proves the creator's scope is captured.
/// </summary>
public class CreateStatsEmailSubscriptionScopeTests
{
    private sealed class FakeSubRepo : IStatsEmailSubscriptionRepository
    {
        public readonly List<StatsEmailSubscription> Store = new();
        public Task<IReadOnlyList<StatsEmailSubscription>> GetAllAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<StatsEmailSubscription>>(Store);
        public Task<StatsEmailSubscription?> GetByIdAsync(StatsEmailSubscriptionId id, CancellationToken ct) => Task.FromResult(Store.FirstOrDefault(s => s.Id == id));
        public void Add(StatsEmailSubscription s) => Store.Add(s);
        public void Remove(StatsEmailSubscription s) => Store.Remove(s);
    }

    private sealed class NoopScheduler : IStatsEmailScheduler
    {
        public void Schedule(StatsEmailSubscription s) { }
        public void Unschedule(StatsEmailSubscriptionId id) { }
        public Task SyncAllAsync(CancellationToken ct) => Task.CompletedTask;
    }

    [Fact]
    public async Task Subscription_captures_the_creators_org_scope()
    {
        var repo = new FakeSubRepo();
        var giza = OrgScope.Create(new[] { "*" }, new[] { "Giza" }, new[] { "*" }, new[] { "*" }, new[] { "*" }, new[] { "*" });
        var handler = new CreateStatsEmailSubscriptionHandler(repo, new NoopScheduler(), new FakeCurrentUser { Scope = giza });

        var input = new StatsEmailSubscriptionInput(
            Name: "Giza Daily", IncludeLabStats: true, IncludeTestStats: false, IncludeAreaStats: false,
            IncludeNoLab: false, FiltersJson: null, UserIds: Array.Empty<Guid>(),
            Emails: new[] { "ops@giza.local" }, SendHour: 7, SendMinute: 0, WindowDays: 1, Enabled: true);

        await handler.Handle(new CreateStatsEmailSubscriptionCommand(input), CancellationToken.None);

        repo.Store.Should().ContainSingle();
        repo.Store[0].Scope.Should().Be(giza, "the report must be rendered under the creating admin's scope");
    }
}
