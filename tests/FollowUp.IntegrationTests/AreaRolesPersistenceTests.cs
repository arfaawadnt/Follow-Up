using FluentAssertions;
using FollowUp.Domain.Common;
using FollowUp.Domain.Laboratories;
using FollowUp.Domain.Reference;
using FollowUp.Domain.Representatives;
using FollowUp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FollowUp.IntegrationTests;

/// <summary>
/// Management roles end-to-end at the persistence layer: the rep-type CHECK accepts LabResponsible / AreaManager and
/// refuses the retired AreaResponsible name, an area's manager FK and a lab's responsible FK round-trip, and the lab's
/// responsible is RESTRICTed (the rep cannot be deleted from under it).
/// </summary>
[Collection("integration")]
public sealed class AreaRolesPersistenceTests
{
    private readonly IntegrationFixture _fx;
    public AreaRolesPersistenceTests(IntegrationFixture fx) => _fx = fx;

    [SkippableFact]
    public async Task Rep_types_persist_and_an_area_manager_and_a_lab_responsible_round_trip()
    {
        Skip.IfNot(_fx.DatabaseAvailable, "FOLLOWUP_DB not set.");
        var tag = Guid.NewGuid().ToString("N")[..8];

        Guid areaId, labId, managerId, responsibleId;
        using (var scope = _fx.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();

            var manager = Representative.Register($"Mgr {tag}", RepresentativeType.AreaManager, GoalDuration.Monthly, Money.Zero, Money.Zero);
            var responsible = Representative.Register($"Resp {tag}", RepresentativeType.LabResponsible, GoalDuration.Monthly, Money.Zero, Money.Zero);
            db.Representatives.AddRange(manager, responsible);

            var city = City.FromOracle($"C-{tag}", $"City {tag}", "Cairo");
            db.Cities.Add(city);
            var area = Area.FromOracle($"A-{tag}", $"Area {tag}", city.Id);
            area.AssignManager(manager.Id);
            db.Areas.Add(area);

            var lab = Laboratory.Register(LabCode.Create($"MGL-{Random.Shared.Next(100000, 999999)}"), $"Lab {tag}", "B");
            lab.AssignResponsible(responsible.Id);
            db.Laboratories.Add(lab);
            await db.SaveChangesAsync();

            areaId = area.Id.Value; labId = lab.Id.Value; managerId = manager.Id.Value; responsibleId = responsible.Id.Value;
        }

        using (var scope = _fx.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
            var area = await db.Areas.AsNoTracking().SingleAsync(a => a.Id == new AreaId(areaId));
            area.AreaManagerId.Should().Be(new RepresentativeId(managerId));
            var lab = await db.Laboratories.AsNoTracking().SingleAsync(l => l.Id == new LaboratoryId(labId));
            lab.ResponsibleRepId.Should().Be(new RepresentativeId(responsibleId));

            var types = await db.Representatives.AsNoTracking()
                .Where(r => r.Id == new RepresentativeId(managerId) || r.Id == new RepresentativeId(responsibleId))
                .Select(r => r.Type).ToListAsync();
            types.Should().BeEquivalentTo(new[] { RepresentativeType.AreaManager, RepresentativeType.LabResponsible });

            // The retired type name is refused by the CHECK; the responsible cannot be deleted from under its lab.
            var oldName = () => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE representative SET type = 'AreaResponsible' WHERE id = {responsibleId}");
            (await oldName.Should().ThrowAsync<Npgsql.PostgresException>()).Which.SqlState.Should().Be("23514");
            var deleteRep = () => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM representative WHERE id = {responsibleId}");
            (await deleteRep.Should().ThrowAsync<Npgsql.PostgresException>()).Which.SqlState.Should().Be("23503", "Restrict FK from laboratory.responsible_rep_id");
        }
    }
}
