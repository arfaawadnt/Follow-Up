using FluentAssertions;
using FollowUp.Application.Features.Compensation;
using FollowUp.Application.Tests.Common;
using FollowUp.Domain.Common;
using FollowUp.Domain.Compensation;
using FollowUp.Domain.Laboratories;

namespace FollowUp.Application.Tests.Features.Compensation;

public class RecalculateLoyaltyHandlerTests
{
    // Cairo "today" is in August 2026, so the current loyalty period is 2026-08.
    private static readonly DateTimeOffset AugustNow = new(2026, 8, 15, 0, 0, 0, TimeSpan.Zero);

    private static (RecalculateLoyaltyHandler handler, Laboratory lab, FakeLabLoyaltyLedgerRepository ledgers) Build()
    {
        var lab = Laboratory.Register(LabCode.Create("MGL-LOY"), "Loyal Lab", "A");
        lab.SetLoyalty(monthlyTarget: 100, points: 999, tier: "InitialTier"); // a distinctive current snapshot
        var labs = new FakeLaboratoryRepository();
        labs.Add(lab);

        var configs = new FakeCompensationConfigRepository();
        configs.Add(CompensationConfig.Create(5m, 100m, new Money(500m), new[] { new LoyaltyTier("Gold", 100m, 500) }));

        var ledgers = new FakeLabLoyaltyLedgerRepository();
        // Achieved == target (100) => 100% => Gold/500, which differs from the seeded 999/"InitialTier" snapshot.
        var data = new FakeCompensationData { LabAchieved = 100 };
        var handler = new RecalculateLoyaltyHandler(labs, ledgers, configs, data, new FakeCurrentUser(), new FakeClock(AugustNow));
        return (handler, lab, ledgers);
    }

    [Fact]
    public async Task Recalculating_a_past_period_records_the_ledger_but_leaves_the_live_snapshot_untouched()
    {
        // CPN-7: the ledger row is period-specific, but the lab's live snapshot (which drives the loyalty page)
        // must NOT be overwritten with a past period's numbers — otherwise a March recalc in August shows March's
        // standing while MtdSamples stays current.
        var (handler, lab, ledgers) = Build();

        await handler.Handle(new RecalculateLoyaltyCommand(lab.Id.Value, new YearMonth(2026, 3).Code), CancellationToken.None);

        lab.LoyaltyPoints.Should().Be(999, "a past-period recalc must not touch the live snapshot");
        lab.LoyaltyTier.Should().Be("InitialTier");
        ledgers.Store.Should().ContainSingle(l => l.Period == new YearMonth(2026, 3)); // the historical row is still written
    }

    [Fact]
    public async Task Recalculating_the_current_period_updates_the_live_snapshot()
    {
        // The guard must not over-block: a current-period recalc still refreshes the lab's standing.
        var (handler, lab, _) = Build();

        await handler.Handle(new RecalculateLoyaltyCommand(lab.Id.Value, new YearMonth(2026, 8).Code), CancellationToken.None);

        lab.LoyaltyPoints.Should().Be(500, "the current-period recalc refreshes the snapshot");
        lab.LoyaltyTier.Should().Be("Gold");
    }
}
