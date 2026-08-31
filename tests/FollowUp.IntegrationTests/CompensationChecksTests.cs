using FluentAssertions;
using FollowUp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace FollowUp.IntegrationTests;

/// <summary>
/// CPN-13: the compensation tables had no CHECK constraints (SchemaHardening added none), so a raw write could
/// persist a negative rate/target/points or an empty tier set that the domain would never allow. These prove the
/// ck_* second line of defense rejects such writes (SQLSTATE 23514). A valid non-empty tier array is supplied so
/// the negative-rate row is rejected specifically by the non-negativity constraint.
/// </summary>
[Collection("integration")]
public sealed class CompensationChecksTests
{
    private readonly IntegrationFixture _fx;
    public CompensationChecksTests(IntegrationFixture fx) => _fx = fx;

    [SkippableFact]
    public async Task A_negative_commission_rate_is_rejected_by_the_database()
    {
        Skip.IfNot(_fx.DatabaseAvailable, "FOLLOWUP_DB not set.");
        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();

        var act = async () => await db.Database.ExecuteSqlRawAsync(@"
INSERT INTO compensation_config (id, commission_rate_percent, bonus_threshold_percent, bonus_amount, loyalty_tiers, created_at, created_by)
VALUES ('cpn13-neg-rate', -1, 0, 0, '[1]'::jsonb, now(), 'test');");

        await act.Should().ThrowAsync<PostgresException>().Where(e => e.SqlState == "23514"); // check_violation
    }

    [SkippableFact]
    public async Task An_empty_loyalty_tier_set_is_rejected_by_the_database()
    {
        Skip.IfNot(_fx.DatabaseAvailable, "FOLLOWUP_DB not set.");
        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();

        var act = async () => await db.Database.ExecuteSqlRawAsync(@"
INSERT INTO compensation_config (id, commission_rate_percent, bonus_threshold_percent, bonus_amount, loyalty_tiers, created_at, created_by)
VALUES ('cpn13-empty-tiers', 5, 100, 0, '[]'::jsonb, now(), 'test');");

        await act.Should().ThrowAsync<PostgresException>().Where(e => e.SqlState == "23514"); // check_violation
    }
}
