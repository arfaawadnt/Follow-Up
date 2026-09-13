using FluentAssertions;
using FollowUp.Domain.Common;
using FollowUp.Domain.Reference;
using Xunit;

namespace FollowUp.Domain.Tests.Reference;

/// <summary>
/// Area "Percentage Deal": an operator-managed flag plus a 0–100 percentage that the Accounting Deductions report
/// multiplies area income by. The invariant is "percentage present iff the deal is on", and the Oracle mirror must
/// never touch either field (they are absent from ApplyOracle, like RealName and the management roles).
/// </summary>
public class AreaPercentageDealTests
{
    private static Area NewArea() => Area.Create("Nasr City", CityId.New(), transportationRequired: false);

    [Fact]
    public void A_new_area_has_no_percentage_deal_by_default()
    {
        var area = NewArea();
        area.PercentageDeal.Should().BeFalse("unchecked by default");
        area.Percentage.Should().BeNull();
    }

    [Fact]
    public void Enabling_the_deal_stores_the_percentage_rounded_to_two_decimals()
    {
        var area = NewArea();
        area.SetPercentageDeal(true, 12.345m);
        area.PercentageDeal.Should().BeTrue();
        area.Percentage.Should().Be(12.34m, "banker's rounding to numeric(5,2), matching Money's convention");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    public void The_boundaries_zero_and_one_hundred_are_valid_percentages(decimal pct)
    {
        var area = NewArea();
        area.SetPercentageDeal(true, pct);
        area.Percentage.Should().Be(pct);
    }

    [Fact]
    public void Disabling_the_deal_clears_the_percentage_so_a_stale_value_can_never_be_applied()
    {
        var area = NewArea();
        area.SetPercentageDeal(true, 25m);

        // A stray percentage arriving with the deal off is discarded, not stored.
        area.SetPercentageDeal(false, 99m);

        area.PercentageDeal.Should().BeFalse();
        area.Percentage.Should().BeNull("the Deductions report must never pick up a percentage from a disabled deal");
    }

    [Fact]
    public void Enabling_the_deal_without_a_percentage_is_refused()
    {
        var area = NewArea();
        var act = () => area.SetPercentageDeal(true, null);
        act.Should().Throw<DomainException>().WithMessage("*percentage is required*");
        area.PercentageDeal.Should().BeFalse("the refused change leaves the area untouched");
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(100.01)]
    [InlineData(150)]
    public void A_percentage_outside_zero_to_one_hundred_is_refused(double pct)
    {
        var area = NewArea();
        var act = () => area.SetPercentageDeal(true, (decimal)pct);
        act.Should().Throw<DomainException>().WithMessage("*between 0 and 100*");
    }

    [Fact]
    public void Oracle_mirror_update_never_touches_the_operator_managed_percentage_deal()
    {
        var area = Area.FromOracle("A-1", "Old Name", CityId.New());
        area.SetPercentageDeal(true, 7.5m);

        area.ApplyOracle("New Name", CityId.New());

        area.Name.Should().Be("New Name", "the sync still mirrors Oracle-owned master data");
        area.PercentageDeal.Should().BeTrue("the sync must not switch an operator's deal off");
        area.Percentage.Should().Be(7.5m, "nor alter its percentage");
    }
}
