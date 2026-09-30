using FluentAssertions;
using FollowUp.Domain.Accounting;
using FollowUp.Domain.Common;
using FollowUp.Domain.Laboratories;
using FollowUp.Domain.Representatives;
using Xunit;

namespace FollowUp.Domain.Tests.Accounting;

/// <summary>2026-09-30: the reviewer's actual figures beside a Rep Income line — non-negative, paid ≤ income, remaining derived.</summary>
public sealed class RepIncomeRevisionTests
{
    private static readonly RepresentativeId Rep = RepresentativeId.New();
    private static readonly LaboratoryId Lab = LaboratoryId.New();
    private static readonly DateOnly Day = new(2026, 9, 3);

    [Fact]
    public void Create_keeps_the_figures_and_derives_the_remaining()
    {
        var v = RepIncomeRevision.Create(Rep, Lab, Day, 1000m, 750m, 20m, "  checked against LDM  ");
        v.ActualIncome.Should().Be(new Money(1000m)); v.ActualPaid.Should().Be(new Money(750m));
        v.ActualRemaining.Should().Be(new Money(250m), "remaining = income − paid");
        v.ActualDelayedPayment.Should().Be(new Money(20m));
        v.Notes.Should().Be("checked against LDM");
        v.IsEmpty.Should().BeFalse();
    }

    [Fact]
    public void Paid_cannot_exceed_the_income_and_nothing_may_be_negative()
    {
        var over = () => RepIncomeRevision.Create(Rep, Lab, Day, 100m, 150m, 0m, null);
        over.Should().Throw<DomainException>().WithMessage("*cannot exceed*");
        var negative = () => RepIncomeRevision.Create(Rep, Lab, Day, -1m, 0m, 0m, null);
        negative.Should().Throw<DomainException>();
        var negativeDelayed = () => RepIncomeRevision.Create(Rep, Lab, Day, 10m, 0m, -5m, null);
        negativeDelayed.Should().Throw<DomainException>();
    }

    [Fact]
    public void Update_replaces_the_figures_and_an_all_zero_line_is_empty()
    {
        var v = RepIncomeRevision.Create(Rep, Lab, Day, 500m, 500m, 0m, "x");
        v.Update(0m, 0m, 0m, "   ");
        v.IsEmpty.Should().BeTrue("an all-zero revision with no notes is removed by the save command");
        v.Update(300m, 100m, 50m, null);
        v.ActualRemaining.Amount.Should().Be(200m);
    }
}
