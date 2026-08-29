using FluentAssertions;
using FollowUp.Infrastructure.Persistence;
using FollowUp.Infrastructure.Persistence.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FollowUp.IntegrationTests;

/// <summary>
/// The startup seeder runs on every app boot and is invoked by several test hosts. When two run concurrently
/// against a fresh (or just-reset) database — e.g. parallel test assemblies sharing one DB, or two API
/// instances starting together — the check-then-insert of the uniquely-named baseline rows must not race into
/// a <c>duplicate key value violates unique constraint "ix_role_name"</c>. Reproduces the CI backend failure.
/// </summary>
[Collection("integration")]
public sealed class SeederConcurrencyTests
{
    private readonly IntegrationFixture _fx;
    public SeederConcurrencyTests(IntegrationFixture fx) => _fx = fx;

    [SkippableFact]
    public async Task Concurrent_seeders_do_not_violate_the_role_name_unique_index()
    {
        Skip.IfNot(_fx.DatabaseAvailable, "FOLLOWUP_DB not set.");

        // Start from an empty identity baseline — the window a parallel test host / app instance seeds into.
        using (var scope = _fx.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
            await db.Database.ExecuteSqlRawAsync(@"
SET followup.allow_audit_purge='on';
DELETE FROM audit_entry;
DELETE FROM user_session;
DELETE FROM app_user;
DELETE FROM role;");
        }

        // Several seeders hitting the empty tables at once must serialize, not both insert the baseline.
        async Task SeedOnce()
        {
            using var scope = _fx.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<DatabaseSeeder>().SeedAsync("Seed_Admin_2026!");
        }

        var race = () => Task.WhenAll(SeedOnce(), SeedOnce(), SeedOnce());
        await race.Should().NotThrowAsync(
            "concurrent seeders must serialize (advisory lock) instead of racing into an ix_role_name duplicate");

        using (var scope = _fx.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
            (await db.Roles.CountAsync()).Should().Be(4);
            (await db.Roles.CountAsync(r => r.Name == "Admin")).Should().Be(1);
            (await db.Users.CountAsync(u => u.Username == "admin")).Should().Be(1);
        }
    }
}
