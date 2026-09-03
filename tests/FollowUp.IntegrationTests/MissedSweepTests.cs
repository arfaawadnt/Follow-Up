using FluentAssertions;
using FollowUp.Application.Common.Abstractions;
using FollowUp.Application.Features.Laboratories.CreateLaboratory;
using FollowUp.Domain.Laboratories;
using FollowUp.Domain.Operations;
using FollowUp.Infrastructure.Jobs;
using FollowUp.Infrastructure.Persistence;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FollowUp.IntegrationTests;

/// <summary>
/// BRD-10: the evening missed-sweep marked EVERY still-Pending visit Missed with no scheduled-time guard, so a
/// lab whose slot falls after the sweep runs was missed before its window. The sweep now spares future slots on
/// today's run (a past day, e.g. the midnight rollover's yesterday, is still all-due).
/// </summary>
[Collection("integration")]
public sealed class MissedSweepTests
{
    private readonly IntegrationFixture _fx;
    public MissedSweepTests(IntegrationFixture fx) => _fx = fx;

    private sealed class FixedClock : IClock
    {
        private readonly DateTimeOffset _now;
        public FixedClock(DateTimeOffset now) => _now = now;
        public DateTimeOffset UtcNow => _now;
        public DateTimeOffset CairoNow => _now;
        public DateOnly CairoToday => DateOnly.FromDateTime(_now.DateTime);
    }

    [SkippableFact]
    public async Task Todays_sweep_misses_a_past_slot_but_spares_a_still_future_one()
    {
        Skip.IfNot(_fx.DatabaseAvailable, "FOLLOWUP_DB not set.");
        await _fx.ResetAsync();

        var today = new DateOnly(2026, 8, 20);
        Guid labId;
        using (var scope = _fx.Services.CreateScope())
        {
            labId = await scope.ServiceProvider.GetRequiredService<IMediator>()
                .Send(new CreateLaboratoryCommand { Code = "MGL-SWEEP", Name = "Sweep Lab", Segment = "A", Governorate = "Cairo" });
            var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
            db.DailyVisits.Add(DailyVisit.Schedule(new LaboratoryId(labId), null, today, new TimeOnly(9, 0)));   // past
            db.DailyVisits.Add(DailyVisit.Schedule(new LaboratoryId(labId), null, today, new TimeOnly(16, 0)));  // future
            await db.SaveChangesAsync();
        }

        // Sweep at 14:00 Cairo on that day.
        var clock = new FixedClock(new DateTimeOffset(2026, 8, 20, 14, 0, 0, TimeSpan.FromHours(2)));
        using (var scope = _fx.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<BoardService>>();
            var swept = await new BoardService(db, clock, logger).RunMissedSweepAsync();
            swept.Should().Be(1, "only the 09:00 slot is past due at 14:00");
        }

        using (var scope = _fx.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
            var visits = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(
                db.DailyVisits.Where(v => v.LaboratoryId == new LaboratoryId(labId)));
            visits.Single(v => v.ScheduledTime == new TimeOnly(9, 0)).Status.Should().Be(VisitStatus.Missed);
            visits.Single(v => v.ScheduledTime == new TimeOnly(16, 0)).Status.Should().Be(VisitStatus.Pending);
        }
    }
}
