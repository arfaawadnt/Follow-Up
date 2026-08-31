using FluentAssertions;
using FollowUp.Application.Features.Laboratories.CreateLaboratory;
using FollowUp.Domain.Laboratories;
using FollowUp.Domain.Operations;
using FollowUp.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FollowUp.IntegrationTests;

/// <summary>
/// BRD-9: duplicate-visit prevention was app-level only (the generators skip a lab/time already present), but
/// the midnight roll-over and the intra-day reconcile run in different Hangfire jobs and can both read an empty
/// slot and insert it. The unique (laboratory_id, visit_date, scheduled_time) index is the DB second line of
/// defense that stops the duplicate regardless of the race.
/// </summary>
[Collection("integration")]
public sealed class DailyVisitUniqueSlotTests
{
    private readonly IntegrationFixture _fx;
    public DailyVisitUniqueSlotTests(IntegrationFixture fx) => _fx = fx;

    [SkippableFact]
    public async Task A_second_visit_for_the_same_lab_date_and_time_is_rejected_by_the_unique_index()
    {
        Skip.IfNot(_fx.DatabaseAvailable, "FOLLOWUP_DB not set.");
        await _fx.ResetAsync();

        Guid labId;
        using (var scope = _fx.Services.CreateScope())
            labId = await scope.ServiceProvider.GetRequiredService<IMediator>()
                .Send(new CreateLaboratoryCommand { Code = "MGL-DUP", Name = "Dup Lab", Segment = "A", Governorate = "Cairo" });

        var date = new DateOnly(2026, 8, 20);
        var time = new TimeOnly(9, 0);

        using (var scope = _fx.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
            db.DailyVisits.Add(DailyVisit.Schedule(new LaboratoryId(labId), null, date, time));
            await db.SaveChangesAsync();
        }

        using (var scope = _fx.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
            db.DailyVisits.Add(DailyVisit.Schedule(new LaboratoryId(labId), null, date, time));

            var act = async () => await db.SaveChangesAsync();

            await act.Should().ThrowAsync<DbUpdateException>("the unique (lab, date, time) index rejects a duplicate slot");
        }
    }
}
