using FluentAssertions;
using FollowUp.Application.Features.Compensation;
using FollowUp.Application.Tests.Common;
using FollowUp.Domain.Common;
using FollowUp.Domain.Laboratories;

namespace FollowUp.Application.Tests.Features.Compensation;

/// <summary>Handler coverage for the compensation config + lab-target commands (finding CPN-11 — these had no tests).</summary>
public class CompensationConfigHandlerTests
{
    private static SetCompensationConfigCommand Config(decimal rate, decimal bonus, params LoyaltyTierInput[] tiers) =>
        new(rate, BonusThresholdPercent: 100m, BonusAmount: bonus, tiers);

    [Fact]
    public async Task Set_config_creates_the_first_time_configuration()
    {
        var configs = new FakeCompensationConfigRepository();
        var handler = new SetCompensationConfigHandler(configs);

        await handler.Handle(Config(5m, 500m, new LoyaltyTierInput("Gold", 100m, 500)), CancellationToken.None);

        configs.Config.Should().NotBeNull();
        configs.Config!.CommissionRatePercent.Should().Be(5m);
        configs.Config.LoyaltyTiers.Should().ContainSingle(t => t.Name == "Gold");
    }

    [Fact]
    public async Task Set_config_rejects_a_negative_rate_on_first_creation()
    {
        // The handler must not be able to persist an invalid first-time config (CPN-5, reachable via Create).
        var handler = new SetCompensationConfigHandler(new FakeCompensationConfigRepository());

        var act = () => handler.Handle(Config(-1m, 500m, new LoyaltyTierInput("Gold", 100m, 500)), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>();
    }

    [Fact]
    public async Task Set_config_rejects_an_empty_tier_set()
    {
        // An empty tier set would zero every lab's loyalty on the next recalc (CPN-6, reachable via SetTiers).
        var handler = new SetCompensationConfigHandler(new FakeCompensationConfigRepository());

        var act = () => handler.Handle(Config(5m, 500m), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>();
    }

    [Fact]
    public async Task Set_lab_target_updates_the_monthly_target()
    {
        var lab = Laboratory.Register(LabCode.Create("MGL-TGT"), "Target Lab", "A");
        var labs = new FakeLaboratoryRepository();
        labs.Add(lab);
        var handler = new SetLabTargetHandler(labs, new FakeCurrentUser());

        await handler.Handle(new SetLabTargetCommand(lab.Id.Value, 250), CancellationToken.None);

        lab.MonthlyTarget.Should().Be(250);
    }
}
