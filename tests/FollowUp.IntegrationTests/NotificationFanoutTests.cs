using FluentAssertions;
using FollowUp.Application.Common.Abstractions;
using FollowUp.Application.Features.Complaints.Commands;
using FollowUp.Application.Features.Laboratories.CreateLaboratory;
using FollowUp.Domain.Identity;
using FollowUp.Infrastructure.Jobs;
using FollowUp.Infrastructure.Persistence;
using FollowUp.Infrastructure.Persistence.Seeding;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FollowUp.IntegrationTests;

[Collection("integration")]
public sealed class NotificationFanoutTests
{
    private readonly IntegrationFixture _fx;
    public NotificationFanoutTests(IntegrationFixture fx) => _fx = fx;

    [SkippableFact]
    public async Task Logging_a_complaint_fans_out_an_in_app_notification_via_the_outbox()
    {
        Skip.IfNot(_fx.DatabaseAvailable, "FOLLOWUP_DB not set.");
        await _fx.ResetAsync();

        // Ensure roles (with ManageComplaints), an admin recipient, and the 6 templates exist.
        using (var scope = _fx.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<DatabaseSeeder>().SeedAsync("Seed_Admin_2026!");

        // Log a complaint -> raises ComplaintLogged -> an outbox row.
        using (var scope = _fx.Services.CreateScope())
        {
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            var labId = await mediator.Send(new CreateLaboratoryCommand
            {
                Code = "MGL-NOTIF",
                Name = "Notify Lab",
                Segment = "A",
                Governorate = "Cairo",
            });
            await mediator.Send(new LogComplaintCommand
            {
                LaboratoryId = labId,
                Category = "TAT",
                ViaChannel = "Phone",
                Details = "late results",
            });
        }

        // Drain the outbox — the fan-out handler runs and writes the in-app feed row.
        using (var scope = _fx.Services.CreateScope())
        {
            var dispatched = await scope.ServiceProvider.GetRequiredService<OutboxDispatcher>().DispatchAsync();
            dispatched.Should().BeGreaterThan(0);
        }

        // Assert: a system notification for the complaint.logged event now exists.
        using (var scope = _fx.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
            var notifications = await db.SystemNotifications.Where(n => n.EventKey == "complaint.logged").ToListAsync();
            notifications.Should().NotBeEmpty();
            notifications[0].Title.Should().Contain("CMP-");
        }
    }

    /// <summary>
    /// Finding M-3 / MSG-002: a lab-scoped notification must not fan out to a user whose org scope excludes the
    /// event's lab — even when that user's role grants every privilege. Only scope should withhold it.
    /// </summary>
    [SkippableFact]
    public async Task A_lab_notification_is_withheld_from_an_out_of_scope_user()
    {
        Skip.IfNot(_fx.DatabaseAvailable, "FOLLOWUP_DB not set.");
        await _fx.ResetAsync();

        using (var scope = _fx.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<DatabaseSeeder>().SeedAsync("Seed_Admin_2026!");

        // A fully-privileged user scoped to Giza only — excluded from a Cairo lab's notification by scope alone.
        Guid gizaUserId;
        using (var scope = _fx.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
            var gizaScope = OrgScope.Create(new[] { "*" }, new[] { "Giza" }, new[] { "*" }, new[] { "*" }, new[] { "*" }, new[] { "*" });
            var role = Role.Create("GizaAll", Privileges.All, "en", "light", gizaScope);
            db.Roles.Add(role);
            var user = AppUser.Create("giza-user", hasher.Hash("Giza_User_2026!"), role.Id);
            user.SetProfile("giza@megalab.local", null);
            db.Users.Add(user);
            await db.SaveChangesAsync();
            gizaUserId = user.Id.Value;
        }

        // Log a complaint for a Cairo lab.
        using (var scope = _fx.Services.CreateScope())
        {
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            var labId = await mediator.Send(new CreateLaboratoryCommand
            {
                Code = "MGL-SCOPE",
                Name = "Cairo Notify Lab",
                Segment = "A",
                Governorate = "Cairo",
            });
            await mediator.Send(new LogComplaintCommand
            {
                LaboratoryId = labId,
                Category = "TAT",
                ViaChannel = "Phone",
                Details = "late results",
            });
        }

        using (var scope = _fx.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<OutboxDispatcher>().DispatchAsync();

        using (var scope = _fx.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
            var logged = await db.SystemNotifications.Where(n => n.EventKey == "complaint.logged").ToListAsync();
            logged.Should().NotBeEmpty("the in-scope admin still receives it");
            logged.Should().NotContain(n => n.RecipientUserId == new AppUserId(gizaUserId),
                "an out-of-scope user must not receive a lab-scoped notification");
        }
    }
}
