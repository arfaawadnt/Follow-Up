using FluentAssertions;
using FollowUp.Domain.Statistics;
using Xunit;

namespace FollowUp.Domain.Tests.Statistics;

/// <summary>2026-09-28: a test line added after its registration is classified by the gap (≤ 3 h blue, &gt; 3 h red).</summary>
public sealed class DetailedRegistrationTests
{
    private static readonly DateTime Reg = new(2026, 9, 28, 9, 0, 0);

    private static DetailedRegistration Line(DateTime? reg, DateTime? test) =>
        DetailedRegistration.Create(new DateOnly(2026, 9, 28), "LAB1", "BR1", "ACC1", "Patient", "T1", 0, "Test",
            10m, 0m, "3", "5", reg, test);

    [Fact]
    public void Test_created_with_the_registration_is_not_an_addition()
    {
        Line(Reg, Reg).TestAddition.Should().Be(TestAdditionStatus.None);
        Line(Reg, Reg.AddMinutes(-5)).TestAddition.Should().Be(TestAdditionStatus.None, "an earlier stamp is never a late addition");
    }

    [Fact]
    public void Unknown_timestamps_are_not_classified()
    {
        Line(null, Reg).TestAddition.Should().Be(TestAdditionStatus.None);
        Line(Reg, null).TestAddition.Should().Be(TestAdditionStatus.None);
        Line(Reg, null).TestAdditionDelay.Should().BeNull();
    }

    [Fact]
    public void Added_within_three_hours_is_within_and_beyond_is_over()
    {
        Line(Reg, Reg.AddSeconds(1)).TestAddition.Should().Be(TestAdditionStatus.Within3Hours);
        Line(Reg, Reg.AddHours(3)).TestAddition.Should().Be(TestAdditionStatus.Within3Hours, "exactly 3 hours is still within");
        Line(Reg, Reg.AddHours(3).AddSeconds(1)).TestAddition.Should().Be(TestAdditionStatus.Over3Hours);
        Line(Reg, Reg.AddDays(2)).TestAddition.Should().Be(TestAdditionStatus.Over3Hours);
    }

    [Fact]
    public void Timestamps_are_kept_as_wall_clock_values()
    {
        var utc = DateTime.SpecifyKind(Reg, DateTimeKind.Utc);
        var line = Line(utc, utc.AddHours(1));
        line.RegCreatedAt!.Value.Kind.Should().Be(DateTimeKind.Unspecified);
        line.TestCreatedAt!.Value.Kind.Should().Be(DateTimeKind.Unspecified);
        line.RegCreatedAt.Should().Be(Reg);
    }
}
