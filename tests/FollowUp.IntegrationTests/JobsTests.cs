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
    public async Task Rollover_rolls_multiple_verified_visits_for_one_lab_into_a_single_monthly_row()
    {
        // Regression for BRD-1: a lab with more than one verified+received visit on the same day must roll
        // all of them into ONE monthly_sample row. The previous RollToMonthlyAsync looked the row up in the
        // database only, missed the row staged for the first visit, and staged a duplicate that violated the
        // unique (laboratory_id, period) index — throwing on the roll-over commit and stopping board
        // generation entirely. Without the fix this test throws DbUpdateException; with it, it passes.
        Skip.IfNot(_fx.DatabaseAvailable, "FOLLOWUP_DB not set.");
        await _fx.ResetAsync();

        var everyDay = new[] { "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday" };
        var labId = new Domain.Laboratories.LaboratoryId(await Send(new CreateLaboratoryCommand
        {
            Code = "MGL-ROLL",
            Name = "Rollover Lab",
            Segment = "A",
            Governorate = "Cairo",
            WorkDays = everyDay,
            VisitTimes = new[] { "09:00", "12:00" },
        }));

        DateOnly yesterday;
        Domain.Common.YearMonth period;
        using (var scope = _fx.Services.CreateScope())
        {
            var sp = scope.ServiceProvider;
            var board = sp.GetRequiredService<BoardService>();
            var clock = sp.GetRequiredService<FollowUp.Application.Common.Abstractions.IClock>();
            var db = sp.GetRequiredService<FollowUpDbContext>();
            yesterday = clock.CairoToday.AddDays(-1);
            period = Domain.Common.YearMonth.From(yesterday);

            // Two visits for the same lab on the same (past) day.
            (await board.GenerateBoardAsync(yesterday)).Should().Be(2);

            // Drive both to verified + received so each RollsToMonthly.
            var visits = await db.DailyVisits
                .Where(v => v.LaboratoryId == labId && v.VisitDate == yesterday).ToListAsync();
            visits.Should().HaveCount(2);
            visits[0].CheckIn(3, "tester", clock.UtcNow);
            visits[1].CheckIn(5, "tester", clock.UtcNow);
            foreach (var v in visits) { v.ReceiveAtLab(clock.UtcNow); v.SetVerified(true); }
            await db.SaveChangesAsync();
        }

        // The roll-over (which internally targets yesterday) must not throw and must produce one summed row.
        using (var scope = _fx.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<BoardService>().RunRolloverAsync();

        using (var scope = _fx.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
            var monthly = await db.MonthlySamples
                .Where(m => m.LaboratoryId == labId && m.Period == period).ToListAsync();
            monthly.Should().ContainSingle("both verified visits roll into one monthly row");
            monthly[0].SampleCount.Should().Be(8); // 3 + 5
        }
    }

    [SkippableFact]
    public async Task Rollover_archives_a_straggler_day_left_by_a_prior_failed_run_into_its_own_month()
    {
        // Regression for BRD-3: the roll-over selected only VisitDate == yesterday and rolled every visit into
        // yesterday's period. A day a previously failed run left un-archived was orphaned once the window
        // advanced — gone from visit_history and monthly_sample, yet still live in daily_visit and counted by
        // the board/lifecycle queries. The fix archives every visit <= yesterday, each into its OWN month, in a
        // single transaction. Without the fix the straggler is never archived and every assertion below fails.
        Skip.IfNot(_fx.DatabaseAvailable, "FOLLOWUP_DB not set.");
        await _fx.ResetAsync();

        var everyDay = new[] { "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday" };
        var labId = new Domain.Laboratories.LaboratoryId(await Send(new CreateLaboratoryCommand
        {
            Code = "MGL-STRAG", Name = "Straggler Lab", Segment = "A", Governorate = "Cairo",
            WorkDays = everyDay, VisitTimes = new[] { "09:00" },
        }));

        DateOnly stragglerDate;
        Domain.Common.YearMonth stragglerPeriod;
        using (var scope = _fx.Services.CreateScope())
        {
            var sp = scope.ServiceProvider;
            var board = sp.GetRequiredService<BoardService>();
            var clock = sp.GetRequiredService<FollowUp.Application.Common.Abstractions.IClock>();
            var db = sp.GetRequiredService<FollowUpDbContext>();

            // A visit from LAST MONTH: always <= yesterday, and always a different YearMonth than yesterday, so
            // it also proves the roll uses the visit's own period rather than yesterday's.
            stragglerDate = clock.CairoToday.AddDays(-1).AddMonths(-1);
            stragglerPeriod = Domain.Common.YearMonth.From(stragglerDate);

            (await board.GenerateBoardAsync(stragglerDate)).Should().Be(1);

            // Drive it to verified + received so it RollsToMonthly with a known sample count.
            var visit = await db.DailyVisits.SingleAsync(v => v.LaboratoryId == labId && v.VisitDate == stragglerDate);
            visit.CheckIn(4, "tester", clock.UtcNow);
            visit.ReceiveAtLab(clock.UtcNow);
            visit.SetVerified(true);
            await db.SaveChangesAsync();
        }

        // Tonight's roll-over targets yesterday/today, but must also pick up and archive the straggler day.
        using (var scope = _fx.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<BoardService>().RunRolloverAsync();

        using (var scope = _fx.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();

            (await db.DailyVisits.AnyAsync(v => v.LaboratoryId == labId && v.VisitDate == stragglerDate))
                .Should().BeFalse("the straggler day must not stay live in daily_visit");
            (await db.VisitHistory.AnyAsync(h => h.LaboratoryId == labId && h.VisitDate == stragglerDate))
                .Should().BeTrue("the straggler must be archived to visit_history");

            var monthly = await db.MonthlySamples
                .SingleAsync(m => m.LaboratoryId == labId && m.Period == stragglerPeriod);
            monthly.SampleCount.Should().Be(4); // rolled into its own month, not yesterday's
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

    [SkippableFact]
    public async Task Registering_a_lab_reconciles_todays_board_intra_day_via_the_outbox()
    {
        Skip.IfNot(_fx.DatabaseAvailable, "FOLLOWUP_DB not set.");
        await _fx.ResetAsync();

        // A lab scheduled every day at two times, so "today" is always a work day (BR-3 is date-agnostic here).
        var labId = new Domain.Laboratories.LaboratoryId(await Send(new CreateLaboratoryCommand
        {
            Code = "MGL-BR3", Name = "Intra-day Lab", Segment = "A", Governorate = "Cairo",
            WorkDays = new[] { "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday" },
            VisitTimes = new[] { "09:00", "12:00" },
        }));

        // The LaboratoryRegistered event is queued in the outbox, but no board rows exist yet.
        using (var scope = _fx.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
            (await db.DailyVisits.CountAsync(v => v.LaboratoryId == labId)).Should().Be(0);
        }

        // Draining the outbox runs the BR-3 handler, which reconciles today's board intra-day.
        using (var scope = _fx.Services.CreateScope())
        {
            var dispatcher = scope.ServiceProvider.GetRequiredService<OutboxDispatcher>();
            await dispatcher.DispatchAsync();
        }

        using (var scope = _fx.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
            var clock = scope.ServiceProvider.GetRequiredService<FollowUp.Application.Common.Abstractions.IClock>();
            var today = clock.CairoToday;
            var visits = await db.DailyVisits.Where(v => v.LaboratoryId == labId && v.VisitDate == today).ToListAsync();
            visits.Should().HaveCount(2);
        }
    }

    [SkippableFact]
    public async Task Schedule_change_prunes_stale_pending_visits_but_keeps_checked_in_ones()
    {
        Skip.IfNot(_fx.DatabaseAvailable, "FOLLOWUP_DB not set.");
        await _fx.ResetAsync();

        var everyDay = new[] { "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday" };
        var labId = new Domain.Laboratories.LaboratoryId(await Send(new CreateLaboratoryCommand
        {
            Code = "MGL-PRUNE", Name = "Prune Lab", Segment = "A", Governorate = "Cairo",
            WorkDays = everyDay, VisitTimes = new[] { "09:00", "12:00" },
        }));

        DateOnly today;
        using (var scope = _fx.Services.CreateScope())
        {
            var sp = scope.ServiceProvider;
            var board = sp.GetRequiredService<BoardService>();
            var clock = sp.GetRequiredService<FollowUp.Application.Common.Abstractions.IClock>();
            today = clock.CairoToday;

            (await board.GenerateBoardAsync(today)).Should().Be(2);

            var db = sp.GetRequiredService<FollowUpDbContext>();
            var nine = new TimeOnly(9, 0);

            // Check in the 09:00 visit — real activity that must survive the prune.
            var visit = await db.DailyVisits.FirstAsync(v => v.LaboratoryId == labId && v.VisitDate == today && v.ScheduledTime == nine);
            visit.CheckIn(3, "tester", clock.UtcNow);

            // Drop the 12:00 slot from the schedule (its still-Pending visit becomes stale).
            var lab = await db.Laboratories.FirstAsync(l => l.Id == labId);
            lab.SetSchedule(Domain.Laboratories.VisitSchedule.Create(everyDay.Select(Enum.Parse<DayOfWeek>), new[] { nine }));
            await db.SaveChangesAsync();
        }

        using (var scope = _fx.Services.CreateScope())
        {
            var result = await scope.ServiceProvider.GetRequiredService<BoardService>().ReconcileLabTodayAsync(labId);
            result.Pruned.Should().Be(1); // the still-Pending 12:00 visit
            result.Added.Should().Be(0);  // 09:00 already present
        }

        using (var scope = _fx.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
            var visits = await db.DailyVisits.Where(v => v.LaboratoryId == labId && v.VisitDate == today).ToListAsync();
            visits.Should().ContainSingle();
            visits[0].ScheduledTime.Should().Be(new TimeOnly(9, 0));
            visits[0].Status.Should().Be(Domain.Operations.VisitStatus.Visited);
        }
    }

    private async Task<Guid> Send(CreateLaboratoryCommand cmd)
    {
        using var scope = _fx.Services.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        return await mediator.Send(cmd);
    }
}
