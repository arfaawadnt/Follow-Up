using FluentAssertions;
using FollowUp.Domain.Common;
using FollowUp.Domain.Laboratories;
using FollowUp.Domain.Operations;

namespace FollowUp.Domain.Tests.Operations;

public class OutsourceSampleTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 15, 10, 0, 0, TimeSpan.FromHours(2));

    [Fact]
    public void Advances_collected_to_sent_to_received()
    {
        var os = OutsourceSample.Create(LaboratoryId.New(), new DateOnly(2026, 8, 15), "External Lab", 3);
        os.Status.Should().Be(OutsourceStatus.Collected);

        os.AdvanceTo(OutsourceStatus.Sent, Now);
        os.Status.Should().Be(OutsourceStatus.Sent);

        os.AdvanceTo(OutsourceStatus.Received, Now.AddHours(1));
        os.Status.Should().Be(OutsourceStatus.Received);
    }

    [Fact]
    public void Cannot_skip_a_state()
    {
        var os = OutsourceSample.Create(LaboratoryId.New(), new DateOnly(2026, 8, 15), "External Lab", 3);
        var act = () => os.AdvanceTo(OutsourceStatus.Received, Now);
        act.Should().Throw<IllegalStateTransitionException>();
    }
}

public class SampleTrackingTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 15, 9, 0, 0, TimeSpan.FromHours(2));

    [Fact]
    public void Pipeline_must_run_in_order()
    {
        var t = SampleTracking.Open("Zone-1", new DateOnly(2026, 8, 15));

        var reviewBeforeEntry = () => t.RecordReview("u", Now);
        reviewBeforeEntry.Should().Throw<DomainException>();

        t.RecordDataEntry(20, "entry-user", Now);
        t.RecordReview("review-user", Now.AddMinutes(30));
        t.RecordSort("sort-user", Now.AddHours(1));

        t.IsComplete.Should().BeTrue();
    }
}
