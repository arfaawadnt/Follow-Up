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

public class OutsourceTestLineTests
{
    [Fact]
    public void Net_revenue_is_test_fees_minus_outsource_fees()
    {
        var line = OutsourceTest.Create("CBC", "Complete Blood Count", "Large", 120m, 45m);
        line.NetRevenue.Amount.Should().Be(75m);
        line.SampleVolume.Should().Be("Large");
        line.TestCode.Should().Be("CBC");
    }

    [Fact]
    public void Rejects_an_invalid_sample_volume()
    {
        var act = () => OutsourceTest.Create("CBC", "Complete Blood Count", "Huge", 10m, 5m);
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Set_tests_replaces_the_line_items()
    {
        var os = OutsourceSample.Create(LaboratoryId.New(), new DateOnly(2026, 8, 15), "External Lab", 3);
        os.SetTests(new[]
        {
            OutsourceTest.Create("CBC", "Complete Blood Count", "Small", 100m, 40m),
            OutsourceTest.Create("TSH", "Thyroid", "Medium", 200m, 90m),
        });
        os.Tests.Should().HaveCount(2);
        os.Tests.Sum(t => t.NetRevenue.Amount).Should().Be(170m); // (100-40)+(200-90)

        os.SetTests(new[] { OutsourceTest.Create("CBC", "Complete Blood Count", "Small", 100m, 40m) });
        os.Tests.Should().ContainSingle();
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
