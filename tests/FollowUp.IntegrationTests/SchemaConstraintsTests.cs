using FluentAssertions;
using FollowUp.Domain.Reference;
using FollowUp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace FollowUp.IntegrationTests;

/// <summary>
/// DB-level integrity constraints added in the schema-migration group: the Oracle SourceCode partial-unique
/// index (LAB-009), the city→area restrict FK (LAB-010), and the segment income-band CHECK (BIZ-008).
/// </summary>
[Collection("integration")]
public sealed class SchemaConstraintsTests
{
    private readonly IntegrationFixture _fx;
    public SchemaConstraintsTests(IntegrationFixture fx) => _fx = fx;

    [SkippableFact]
    public async Task Duplicate_oracle_source_code_is_rejected() // LAB-009
    {
        Skip.IfNot(_fx.DatabaseAvailable, "FOLLOWUP_DB not set.");
        var code = "SC-" + Guid.NewGuid().ToString("N")[..10];
        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();

        db.Cities.Add(City.FromOracle(code, "City One", "Cairo"));
        await db.SaveChangesAsync();

        db.Cities.Add(City.FromOracle(code, "City Two", "Cairo"));
        var act = () => db.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>(
            "a second record with the same Oracle source code violates the partial-unique index");
    }

    [SkippableFact]
    public async Task Deleting_a_city_that_still_has_areas_is_restricted() // LAB-010
    {
        Skip.IfNot(_fx.DatabaseAvailable, "FOLLOWUP_DB not set.");
        var code = "C-" + Guid.NewGuid().ToString("N")[..10];
        Guid cityId;
        using (var scope = _fx.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
            var city = City.FromOracle(code, "Parent City", "Cairo");
            db.Cities.Add(city);
            await db.SaveChangesAsync();
            db.Areas.Add(Area.FromOracle(code, "Child Area", city.Id));
            await db.SaveChangesAsync();
            cityId = city.Id.Value;
        }

        // Delete in a fresh context that does NOT track the area, so the request reaches the DB and its Restrict
        // FK rejects it (with the area tracked, EF would block the severed required relationship client-side first).
        using (var scope = _fx.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
            var city = await db.Cities.FirstAsync(c => c.Id == new Domain.Reference.CityId(cityId));
            db.Cities.Remove(city);
            var act = () => db.SaveChangesAsync();

            var ex = (await act.Should().ThrowAsync<DbUpdateException>(
                "the city→area FK is Restrict, so a city with areas cannot be deleted")).Which;
            ((Npgsql.PostgresException)ex.InnerException!).SqlState.Should().Be("23503"); // foreign_key_violation
        }
    }

    [SkippableFact]
    public async Task An_inverted_segment_income_band_is_rejected() // BIZ-008
    {
        Skip.IfNot(_fx.DatabaseAvailable, "FOLLOWUP_DB not set.");
        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();

        // A valid band (from <= to) inserts fine — proves the raw shape is right.
        var okCode = "SEG-OK-" + Guid.NewGuid().ToString("N")[..8];
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO ref_item (id, type, code, name_en, sort_order, target_income_from, target_income_to, created_at, created_by) VALUES (gen_random_uuid(), {RefType.Segment.Id}, {okCode}, 'Good band', 0, 0, 1000, now(), 'test')");

        // An inverted band (to < from) is refused by ck_ref_item_target_income_order (SqlState 23514).
        var badCode = "SEG-BAD-" + Guid.NewGuid().ToString("N")[..8];
        var act = () => db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO ref_item (id, type, code, name_en, sort_order, target_income_from, target_income_to, created_at, created_by) VALUES (gen_random_uuid(), {RefType.Segment.Id}, {badCode}, 'Bad band', 0, 100, 50, now(), 'test')");

        (await act.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("23514");
    }
}
