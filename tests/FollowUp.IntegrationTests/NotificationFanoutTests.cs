using FluentAssertions;
using FollowUp.Application.Features.Complaints.Commands;
using FollowUp.Application.Features.Laboratories.CreateLaboratory;
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
}
