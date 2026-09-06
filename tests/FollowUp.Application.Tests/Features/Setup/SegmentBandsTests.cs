using FluentAssertions;
using FollowUp.Application.Features.Setup;
using FollowUp.Application.Tests.Common;

namespace FollowUp.Application.Tests.Features.Setup;

public class SegmentBandsTests
{
    // Default tiers: C ≤ 3000, B ≤ 6000, A ≤ 10000, VIP unbounded (> 10000).
    private static readonly (string Code, decimal? UpperBound)[] Defaults =
    {
        ("C", 3000m), ("B", 6000m), ("A", 10000m), ("VIP", null),
    };

    [Theory]
    [InlineData(0, "C")]
    [InlineData(1500, "C")]
    [InlineData(3000, "C")]      // upper bound is inclusive
    [InlineData(3000.5, "B")]    // just over C's bound → next tier
    [InlineData(3001, "B")]
    [InlineData(6000, "B")]
    [InlineData(6000.01, "A")]
    [InlineData(10000, "A")]
    [InlineData(10000.01, "VIP")]
    [InlineData(25000, "VIP")]   // unbounded top tier
    public void Assigns_income_to_the_matching_tier(decimal income, string expected) =>
        SegmentBands.Match(Defaults, income).Should().Be(expected);

    [Fact]
    public void Ordering_of_bands_does_not_matter()
    {
        var shuffled = new (string, decimal?)[] { ("VIP", null), ("A", 10000m), ("C", 3000m), ("B", 6000m) };
        SegmentBands.Match(shuffled, 5000m).Should().Be("B");
        SegmentBands.Match(shuffled, 99999m).Should().Be("VIP");
    }

    [Fact]
    public void Returns_null_when_no_tier_covers_the_income()
    {
        // No unbounded top tier and income exceeds every upper bound → unmatched.
        var capped = new (string, decimal?)[] { ("C", 3000m), ("B", 6000m) };
        SegmentBands.Match(capped, 9000m).Should().BeNull();
    }

    [Fact]
    public async Task Create_handler_persists_the_income_band()
    {
        var repo = new FakeRefItemRepository();
        var handler = new CreateRefItemHandler(repo);

        await handler.Handle(new CreateRefItemCommand("Segment", "VIP", "VIP", null, TargetIncomeFrom: 10001m, TargetIncomeTo: null), CancellationToken.None);

        var item = repo.Store.Should().ContainSingle().Subject;
        item.TargetIncomeFrom.Should().Be(10001m);
        item.TargetIncomeTo.Should().BeNull();
    }
}
