using FluentAssertions;
using FollowUp.Application.Features.Complaints.Commands;
using FollowUp.Application.Features.Laboratories.CreateLaboratory;
using FollowUp.Domain.Laboratories;
using FollowUp.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace FollowUp.IntegrationTests;

/// <summary>
/// CMP-11: complaint.stage had no CHECK constraint, though SchemaHardening added one for status. The
/// ck_complaint_stage constraint is the DB second line of defense that rejects a stage value outside the
/// ComplaintStage enumeration (e.g. from a bad manual write or a future code path that bypasses the aggregate).
/// </summary>
[Collection("integration")]
public sealed class ComplaintStageCheckTests
{
    private readonly IntegrationFixture _fx;
    public ComplaintStageCheckTests(IntegrationFixture fx) => _fx = fx;

    [SkippableFact]
    public async Task An_out_of_enumeration_stage_value_is_rejected_by_the_database()
    {
        Skip.IfNot(_fx.DatabaseAvailable, "FOLLOWUP_DB not set.");
        await _fx.ResetAsync();

        Guid complaintId;
        using (var scope = _fx.Services.CreateScope())
        {
            var m = scope.ServiceProvider.GetRequiredService<IMediator>();
            var labId = await m.Send(new CreateLaboratoryCommand { Code = "MGL-STG", Name = "Stage Lab", Segment = "A", Governorate = "Cairo" });
            await m.Send(new LogComplaintCommand { LaboratoryId = labId, Category = "Result Quality", ViaChannel = "Phone", Details = "d" });
            var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
            complaintId = (await db.Complaints.AsNoTracking().FirstAsync(c => c.LaboratoryId == new LaboratoryId(labId))).Id.Value;
        }

        using (var scope = _fx.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
            var act = async () => await db.Database.ExecuteSqlRawAsync(
                "UPDATE complaint SET stage = 'BogusStage' WHERE id = {0}", complaintId);

            await act.Should().ThrowAsync<PostgresException>().Where(e => e.SqlState == "23514"); // check_violation
        }
    }
}
