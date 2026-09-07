using FluentAssertions;
using FollowUp.Infrastructure.Persistence;
using FollowUp.Infrastructure.Persistence.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FollowUp.IntegrationTests;

/// <summary>
/// Finding B-4 / IAM-001: the built-in admin must never be seeded with a source-visible default password.
/// When the admin would actually be created (empty user table) and no password is supplied, the seeder must
/// fail fast instead of falling back to a literal.
/// </summary>
[Collection("integration")]
public sealed class AdminSeedFailFastTests
{
    private readonly IntegrationFixture _fx;
    public AdminSeedFailFastTests(IntegrationFixture fx) => _fx = fx;

    [SkippableFact]
    public async Task Seeding_the_admin_without_a_password_fails_fast()
    {
        Skip.IfNot(_fx.DatabaseAvailable, "FOLLOWUP_DB not set.");
        await _fx.ResetAsync();

        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
        await db.Database.ExecuteSqlRawAsync("DELETE FROM user_session; DELETE FROM app_user;");
        var seeder = scope.ServiceProvider.GetRequiredService<DatabaseSeeder>();

        var act = () => seeder.SeedAsync(null);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*FOLLOWUP_ADMIN_PASSWORD*");
        (await db.Users.CountAsync()).Should().Be(0, "no admin may be created without a configured password");

        // Restore the admin with the canonical seed password other tests authenticate against, so the shared
        // test database is left in the state they expect.
        await seeder.SeedAsync("Seed_Admin_2026!");
    }

    [SkippableFact]
    public async Task Seeding_the_admin_with_a_password_creates_it()
    {
        Skip.IfNot(_fx.DatabaseAvailable, "FOLLOWUP_DB not set.");
        await _fx.ResetAsync();

        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
        await db.Database.ExecuteSqlRawAsync("DELETE FROM user_session; DELETE FROM app_user;");
        var seeder = scope.ServiceProvider.GetRequiredService<DatabaseSeeder>();

        // Seed with the canonical password other tests authenticate against, so the shared admin is left in the
        // state they expect.
        var created = await seeder.SeedAsync("Seed_Admin_2026!");

        created.Should().Be("admin");
        (await db.Users.CountAsync()).Should().Be(1);
    }
}
