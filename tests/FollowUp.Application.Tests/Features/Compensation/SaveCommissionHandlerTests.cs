using FluentAssertions;
using FollowUp.Application.Common.Exceptions;
using FollowUp.Application.Features.Compensation;
using FollowUp.Application.Tests.Common;
using FollowUp.Domain.Common;
using FollowUp.Domain.Compensation;
using FollowUp.Domain.Identity;
using FollowUp.Domain.Representatives;

namespace FollowUp.Application.Tests.Features.Compensation;

public class SaveCommissionHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Recomputes_payout_server_side_from_config_and_achieved()
    {
        var rep = Representative.Register("Rep", RepresentativeType.Collector, GoalDuration.Monthly,
            salary: new Money(3000m), target: new Money(2000m));
        var reps = new FakeRepresentativeRepository();
        reps.Store.Add(rep);

        var commissions = new FakeRepCommissionRepository();
        var configs = new FakeCompensationConfigRepository();
        configs.Add(CompensationConfig.Create(5m, 100m, new Money(500m), new[] { new LoyaltyTier("Gold", 100m, 500) }));

        // Achieved equals target => 5% of 2000 = 100 commission, +500 bonus, +3000 base = 3600 total.
        var data = new FakeCompensationData { RepAchieved = 2000 };
        var handler = new SaveCommissionHandler(reps, commissions, configs, data, new FakeCurrentUser(), new FakeClock(Now));

        await handler.Handle(new SaveCommissionCommand(rep.Id.Value, new YearMonth(2026, 8).Code), CancellationToken.None);

        var saved = commissions.Store.Single();
        saved.BaseSalary.Should().Be(new Money(3000m));
        saved.Commission.Should().Be(new Money(100m));
        saved.Bonus.Should().Be(new Money(500m));
        saved.Total.Should().Be(new Money(3600m));
    }

    [Fact]
    public async Task Save_is_refused_for_a_rep_outside_the_callers_scope()
    {
        var rep = Representative.Register("Rep", RepresentativeType.Collector, GoalDuration.Monthly,
            salary: new Money(3000m), target: new Money(2000m));
        rep.AssignScope(branch: null, governorate: "Cairo", area: null, city: null);
        var reps = new FakeRepresentativeRepository();
        reps.Store.Add(rep);
        var configs = new FakeCompensationConfigRepository();
        configs.Add(CompensationConfig.Create(5m, 100m, new Money(500m), new[] { new LoyaltyTier("Gold", 100m, 500) }));

        // Caller scoped to Giza; the rep is attributed to Cairo -> refused (finding CPN-3).
        var giza = OrgScope.Create(
            new[] { "*" }, new[] { "Giza" }, new[] { "*" }, new[] { "*" }, new[] { "*" }, new[] { "*" });
        var handler = new SaveCommissionHandler(reps, new FakeRepCommissionRepository(), configs,
            new FakeCompensationData { RepAchieved = 2000 }, new FakeCurrentUser { Scope = giza }, new FakeClock(Now));

        var act = () => handler.Handle(new SaveCommissionCommand(rep.Id.Value, new YearMonth(2026, 8).Code), CancellationToken.None);
        await act.Should().ThrowAsync<ForbiddenException>();
    }
}
