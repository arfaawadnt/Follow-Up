using FluentAssertions;
using FollowUp.Domain.Accounting;
using FollowUp.Domain.Common;
using FollowUp.Domain.Identity;
using FollowUp.Domain.Representatives;
using Xunit;

namespace FollowUp.Domain.Tests.Accounting;

/// <summary>2026-09-28: a Lab Responsible's statement closes month by month; the stored closing balance may be negative.</summary>
public sealed class StatementMonthCloseTests
{
    private static readonly RepresentativeId Rep = RepresentativeId.New();
    private static readonly AppUserId User = AppUserId.New();
    private static readonly DateTimeOffset At = new(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Close_stores_the_month_its_balance_and_who_closed_it()
    {
        var c = StatementMonthClose.Close(Rep, 2026, 9, 1234.567m, User, At, "  first close  ");
        c.Year.Should().Be(2026); c.Month.Should().Be(9);
        c.ClosingBalance.Should().Be(new Money(1234.57m), "Money keeps two decimals");
        c.ClosedByUserId.Should().Be(User); c.ClosedAtUtc.Should().Be(At);
        c.Notes.Should().Be("first close");
        c.MonthStart.Should().Be(new DateOnly(2026, 9, 1)); c.MonthEnd.Should().Be(new DateOnly(2026, 9, 30));
        c.Contains(new DateOnly(2026, 9, 15)).Should().BeTrue(); c.Contains(new DateOnly(2026, 10, 1)).Should().BeFalse();
    }

    [Fact]
    public void A_negative_closing_balance_is_kept_as_is()
    {
        StatementMonthClose.Close(Rep, 2026, 2, -250m, User, At, null).ClosingBalance.Amount.Should().Be(-250m, "the balance is in the rep's favour — carried as an opening credit");
    }

    [Fact]
    public void Month_and_year_are_validated()
    {
        var m0 = () => StatementMonthClose.Close(Rep, 2026, 0, 0m, User, At, null);
        var m13 = () => StatementMonthClose.Close(Rep, 2026, 13, 0m, User, At, null);
        var y = () => StatementMonthClose.Close(Rep, 1999, 1, 0m, User, At, null);
        m0.Should().Throw<DomainException>(); m13.Should().Throw<DomainException>(); y.Should().Throw<DomainException>();
    }

    [Fact]
    public void Next_month_rolls_over_the_year()
    {
        StatementMonthClose.NextMonth(2026, 9).Should().Be((2026, 10));
        StatementMonthClose.NextMonth(2026, 12).Should().Be((2027, 1));
    }
}
