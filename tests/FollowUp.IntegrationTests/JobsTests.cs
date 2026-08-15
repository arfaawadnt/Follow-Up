using FluentAssertions;
using FollowUp.Application.Features.Laboratories.CreateLaboratory;
using FollowUp.Infrastructure.Jobs;
using FollowUp.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FollowUp.IntegrationTests;

[Collection("integration")]
public sealed class JobsTests
{
    private readonly IntegrationFixture _fx;
    public JobsTests(IntegrationFixture fx) => _fx = fx;

    [SkippableFact]
    public async Task Board_service_generates_visits_then_sweep_marks_them_missed()
    {
        Skip.IfNot(_fx.DatabaseAvailable, "FOLLOWUP_DB not set.");
        await _fx.ResetAsync();

        // A lab scheduled on Sundays at two times.
        await Send(new CreateLaboratoryCommand
        {
            Code = "MGL-JOB1", Name = "Job Lab", Segment = "A", Governorate = "Cairo",
            WorkDays = new[] { "Sunday" }, VisitTimes = new[] { "09:00", "12:00" },
        });

        var sunday = new DateOnly(2026, 8, 16); // a Sunday

        using (var scope = _fx.Services.CreateScope())
        {
            var board = scope.ServiceProvider.GetRequiredService<BoardService>();
            var created = await board.GenerateBoardAsync(sunday);
            created.Should().Be(2);

            var missed = await board.RunMissedSweepAsync(sunday);
            missed.Should().Be(2);
        }

        using (var scope = _fx.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
            var visits = await db.DailyVisits.Where(v => v.VisitDate == sunday).ToListAsync();
            visits.Should().HaveCount(2);
            visits.Should().OnlyContain(v => v.Status == Domain.Operations.VisitStatus.Missed);
        }
    }

    [SkippableFact]
    public async Task Outbox_dispatcher_drains_pending_messages()
    {
        Skip.IfNot(_fx.DatabaseAvailable, "FOLLOWUP_DB not set.");
        await _fx.ResetAsync();

        // Creating a lab raises LaboratoryRegistered -> an outbox row.
        await Send(new CreateLaboratoryCommand { Code = "MGL-OBX", Name = "Outbox Lab", Segment = "B", Governorate = "Giza" });

        using (var scope = _fx.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
            (await db.OutboxMessages.CountAsync(m => m.ProcessedAt == null)).Should().BeGreaterThan(0);
        }

        using (var scope = _fx.Services.CreateScope())
        {
            var dispatcher = scope.ServiceProvider.GetRequiredService<OutboxDispatcher>();
            var dispatched = await dispatcher.DispatchAsync();
            dispatched.Should().BeGreaterThan(0);
        }

        using (var scope = _fx.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
            (await db.OutboxMessages.CountAsync(m => m.ProcessedAt == null)).Should().Be(0);
        }
    }

    private async Task<Guid> Send(CreateLaboratoryCommand cmd)
    {
        using var scope = _fx.Services.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        return await mediator.Send(cmd);
    }
}
