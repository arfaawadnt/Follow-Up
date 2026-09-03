using FluentAssertions;
using FollowUp.Application.Features.Auth;
using FollowUp.Domain.Identity;
using FollowUp.Infrastructure.Persistence;
using FollowUp.Infrastructure.Persistence.Seeding;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FollowUp.IntegrationTests;

[Collection("integration")]
public sealed class SeedAndLoginTests
{
    private readonly IntegrationFixture _fx;
    public SeedAndLoginTests(IntegrationFixture fx) => _fx = fx;

    [SkippableFact]
    public async Task Seeder_creates_baseline_and_admin_can_log_in()
    {
        Skip.IfNot(_fx.DatabaseAvailable, "FOLLOWUP_DB not set.");

        const string adminPassword = "Seed_Admin_2026!";

        // Reset identity/reference tables, then seed.
        using (var scope = _fx.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
            await db.Database.ExecuteSqlRawAsync(@"
SET followup.allow_audit_purge='on';
DELETE FROM audit_entry;
DELETE FROM user_session;
DELETE FROM app_user;
DELETE FROM role;
DELETE FROM notification_template;
DELETE FROM compensation_config;
DELETE FROM ref_item;");

            var seeder = scope.ServiceProvider.GetRequiredService<DatabaseSeeder>();
            var created = await seeder.SeedAsync(adminPassword);
            created.Should().Be("admin");
        }

        // Assert baseline counts.
        using (var scope = _fx.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
            (await db.Roles.CountAsync()).Should().Be(4);
            (await db.NotificationTemplates.CountAsync()).Should().Be(6);
            (await db.CompensationConfigs.CountAsync()).Should().Be(1);
            (await db.RefItems.CountAsync()).Should().BeGreaterThanOrEqualTo(6);
        }

        // The seeded admin can authenticate end-to-end (PBKDF2 verify → session + token).
        using (var scope = _fx.Services.CreateScope())
        {
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            var result = await mediator.Send(new LoginCommand("admin", adminPassword, "127.0.0.1", "itest"));

            result.Token.Should().NotBeNullOrEmpty();
            result.RoleName.Should().Be("Admin");
            result.Privileges.Should().Contain(Privileges.ManageUsers);
        }

        // Wrong password is rejected.
        using (var scope = _fx.Services.CreateScope())
        {
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            var act = async () => await mediator.Send(new LoginCommand("admin", "wrong-password", null, null));
            await act.Should().ThrowAsync<Application.Common.Exceptions.UnauthorizedException>();
        }
    }

    [SkippableFact]
    public async Task Seeder_is_idempotent()
    {
        Skip.IfNot(_fx.DatabaseAvailable, "FOLLOWUP_DB not set.");

        using var scope = _fx.Services.CreateScope();
        var seeder = scope.ServiceProvider.GetRequiredService<DatabaseSeeder>();
        // Second run should create nothing new (admin already exists).
        var again = await seeder.SeedAsync("irrelevant");
        again.Should().BeNull();

        var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
        (await db.Roles.CountAsync()).Should().Be(4);
    }
}
