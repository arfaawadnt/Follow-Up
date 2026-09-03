using FluentAssertions;
using FollowUp.Domain.Common;
using FollowUp.Domain.Laboratories;
using FollowUp.Domain.Marketing;
using FollowUp.Domain.Representatives;
using FollowUp.Domain.Statistics;

namespace FollowUp.Domain.Tests.Marketing;

public class MarketingVisitTests
{
    [Fact]
    public void Schedule_carries_number_time_and_plan()
    {
        var visit = MarketingVisit.Schedule(5, LaboratoryId.New(), RepresentativeId.New(),
            MarketingPurpose.Promotion, new DateOnly(2026, 9, 1), new TimeOnly(10, 30), "pitch the new panel");

        visit.Reference.Should().Be("MV5");
        visit.Number.Should().Be(5);
        visit.ScheduledTime.Should().Be(new TimeOnly(10, 30));
        visit.Plan.Should().Be("pitch the new panel");
        visit.Status.Should().Be(MarketingVisitStatus.Scheduled);
    }

    [Fact]
    public void Schedule_rejects_non_positive_numbers()
    {
        var act = () => MarketingVisit.Schedule(0, LaboratoryId.New(), RepresentativeId.New(),
            MarketingPurpose.Routine, new DateOnly(2026, 9, 1));

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void TestStatistic_income_is_upserted_and_never_negative()
    {
        var stat = TestStatistic.For(new DateOnly(2026, 8, 1), "cbc");

        stat.SetIncome(new Money(1250.456m));
        stat.Income.Amount.Should().Be(1250.46m); // banker's rounding to 2dp

        var act = () => stat.SetIncome(new Money(-1m));
        act.Should().Throw<DomainException>();
    }
}
