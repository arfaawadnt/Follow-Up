using FluentAssertions;
using FollowUp.Domain.Common;
using FollowUp.Domain.Reference;
using FollowUp.Domain.Representatives;
using FollowUp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FollowUp.IntegrationTests;

/// <summary>
/// Area management roles end-to-end at the persistence layer: the widened rep-type CHECK accepts the two new types
/// (a save would fail with 23514 otherwise), and an area's manager/responsible FKs round-trip.
/// </summary>
[Collection("integration")]
public sealed class AreaRolesPersistenceTests
{
    private readonly IntegrationFixture _fx;
    public AreaRolesPersistenceTests(IntegrationFixture fx) => _fx = fx;

    [SkippableFact]
    public async Task New_rep_types_persist_and_an_area_round_trips_its_manager_and_responsible()
    {
        Skip.IfNot(_fx.DatabaseAvailable, "FOLLOWUP_DB not set.");
        var tag = Guid.NewGuid().ToString("N")[..8];

        Guid areaId, managerId, responsibleId;
        using (var scope = _fx.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();

            // Reps of the two NEW types — rejected by the old 4-value CHECK, accepted by the widened one.
            var manager = Representative.Register($"Mgr {tag}", RepresentativeType.AreaManager, GoalDuration.Monthly, Money.Zero, Money.Zero);
            var responsible = Representative.Register($"Resp {tag}", RepresentativeType.AreaResponsible, GoalDuration.Monthly, Money.Zero, Money.Zero);
            db.Representatives.AddRange(manager, responsible);

            var city = City.FromOracle($"C-{tag}", $"City {tag}", "Cairo");
            db.Cities.Add(city);
            var area = Area.FromOracle($"A-{tag}", $"Area {tag}", city.Id);
            area.AssignManager(manager.Id);
            area.AssignResponsible(responsible.Id);
            db.Areas.Add(area);
            await db.SaveChangesAsync();

            areaId = area.Id.Value; managerId = manager.Id.Value; responsibleId = responsible.Id.Value;
        }

        using (var scope = _fx.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
            var area = await db.Areas.AsNoTracking().SingleAsync(a => a.Id == new AreaId(areaId));
            area.AreaManagerId.Should().Be(new RepresentativeId(managerId));
            area.AreaResponsibleId.Should().Be(new RepresentativeId(responsibleId));

            var types = await db.Representatives.AsNoTracking()
                .Where(r => r.Id == new RepresentativeId(managerId) || r.Id == new RepresentativeId(responsibleId))
                .Select(r => r.Type).ToListAsync();
            types.Should().BeEquivalentTo(new[] { RepresentativeType.AreaManager, RepresentativeType.AreaResponsible });
        }
    }
}
