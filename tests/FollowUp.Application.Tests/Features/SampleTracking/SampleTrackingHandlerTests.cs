using FluentAssertions;
using FollowUp.Application.Common.Exceptions;
using FollowUp.Application.Features.SampleTracking;
using FollowUp.Application.Tests.Common;
using FollowUp.Domain.Identity;

namespace FollowUp.Application.Tests.Features.SampleTracking;

public class SampleTrackingHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 15, 9, 0, 0, TimeSpan.FromHours(2));
    private static readonly DateOnly Today = new(2026, 8, 15);

    [Fact]
    public async Task Data_entry_then_advance_runs_the_pipeline_in_order()
    {
        var repo = new FakeSampleTrackingRepository();
        var user = new FakeCurrentUser { Privileges = new HashSet<string> { Privileges.SampleTracking } };
        var entry = new RecordSampleDataEntryHandler(repo, user, new FakeClock(Now));

        var id = await entry.Handle(new RecordSampleDataEntryCommand("Zone-1", Today, 30), CancellationToken.None);

        var advance = new AdvanceSampleTrackingHandler(repo, user, new FakeClock(Now));
        await advance.Handle(new AdvanceSampleTrackingCommand(id, "Review"), CancellationToken.None);
        await advance.Handle(new AdvanceSampleTrackingCommand(id, "Sort"), CancellationToken.None);

        repo.Store.Should().ContainSingle();
        repo.Store[0].IsComplete.Should().BeTrue();
    }

    [Fact]
    public async Task Rejects_area_outside_scope()
    {
        var repo = new FakeSampleTrackingRepository();
        var scope = OrgScope.Create(new[] { "*" }, new[] { "*" }, new[] { "*" },
            areas: new[] { "Zone-2" }, categories: new[] { "*" }, segments: new[] { "*" });
        var user = new FakeCurrentUser { Privileges = new HashSet<string> { Privileges.SampleTracking }, Scope = scope };
        var handler = new RecordSampleDataEntryHandler(repo, user, new FakeClock(Now));

        var act = () => handler.Handle(new RecordSampleDataEntryCommand("Zone-1", Today, 30), CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>();
    }
}
