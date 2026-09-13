using FluentAssertions;
using FollowUp.Domain.Laboratories;
using FollowUp.Domain.Reference;
using FollowUp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FollowUp.IntegrationTests;

/// <summary>
/// Area "Percentage Deal" and Laboratory "Credit" at the persistence layer: both round-trip, and the raw-SQL CHECKs
/// added by the AreaPercentageDealAndLabCredit migration hold the domain invariants even against a write that
/// bypasses the domain (23514 check_violation).
/// </summary>
[Collection("integration")]
public sealed class AreaPercentageDealPersistenceTests
{
    private readonly IntegrationFixture _fx;
    public AreaPercentageDealPersistenceTests(IntegrationFixture fx) => _fx = fx;

    [SkippableFact]
    public async Task Percentage_deal_and_lab_credit_round_trip()
    {
        Skip.IfNot(_fx.DatabaseAvailable, "FOLLOWUP_DB not set.");
        var tag = Guid.NewGuid().ToString("N")[..8];

        Guid areaId, labId;
        using (var scope = _fx.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
            var city = City.FromOracle($"C-{tag}", $"City {tag}", "Cairo");
            db.Cities.Add(city);
            var area = Area.FromOracle($"A-{tag}", $"Area {tag}", city.Id);
            area.SetPercentageDeal(true, 12.5m);
            db.Areas.Add(area);

            var lab = Laboratory.Register(LabCode.Create($"MGL-{Random.Shared.Next(100000, 999999)}"), $"Lab {tag}", "B");
            lab.SetCredit(true);
            db.Laboratories.Add(lab);
            await db.SaveChangesAsync();
            areaId = area.Id.Value; labId = lab.Id.Value;
        }

        using (var scope = _fx.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
            var area = await db.Areas.AsNoTracking().SingleAsync(a => a.Id == new AreaId(areaId));
            area.PercentageDeal.Should().BeTrue();
            area.Percentage.Should().Be(12.5m);

            var lab = await db.Laboratories.AsNoTracking().SingleAsync(l => l.Id == new LaboratoryId(labId));
            lab.Credit.Should().BeTrue();
        }
    }

    [SkippableFact]
    public async Task Database_checks_refuse_a_percentage_deal_state_the_domain_forbids()
    {
        Skip.IfNot(_fx.DatabaseAvailable, "FOLLOWUP_DB not set.");
        var tag = Guid.NewGuid().ToString("N")[..8];

        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
        var city = City.FromOracle($"C-{tag}", $"City {tag}", "Cairo");
        db.Cities.Add(city);
        var area = Area.FromOracle($"A-{tag}", $"Area {tag}", city.Id);
        db.Areas.Add(area);
        await db.SaveChangesAsync();
        var id = area.Id.Value;

        // Deal on with no percentage — the domain refuses this; the DB must too (belt and suspenders).
        var dealWithoutPercentage = () => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE area SET percentage_deal = true, percentage = NULL WHERE id = {id}");
        (await dealWithoutPercentage.Should().ThrowAsync<Npgsql.PostgresException>())
            .Which.SqlState.Should().Be("23514", "ck_area_percentage_deal");

        // Out-of-range percentage.
        var outOfRange = () => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE area SET percentage_deal = true, percentage = 150 WHERE id = {id}");
        (await outOfRange.Should().ThrowAsync<Npgsql.PostgresException>())
            .Which.SqlState.Should().Be("23514", "ck_area_percentage_range");

        // A percentage lingering on a disabled deal.
        var stalePercentage = () => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE area SET percentage_deal = false, percentage = 10 WHERE id = {id}");
        (await stalePercentage.Should().ThrowAsync<Npgsql.PostgresException>())
            .Which.SqlState.Should().Be("23514", "ck_area_percentage_deal (iff)");
    }
}
