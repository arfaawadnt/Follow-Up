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
/// CMP-12: complaint.representative_id was an unconstrained Guid. The fk_complaint_representative constraint is the
/// database second line of defense that rejects a reference to a non-existent representative (the handler rejects
/// it up front, but a raw write must be rejected too).
/// </summary>
[Collection("integration")]
public sealed class ComplaintRepresentativeFkTests
{
    private readonly IntegrationFixture _fx;
    public ComplaintRepresentativeFkTests(IntegrationFixture fx) => _fx = fx;

    [SkippableFact]
    public async Task A_reference_to_a_non_existent_representative_is_rejected_by_the_database()
    {
        Skip.IfNot(_fx.DatabaseAvailable, "FOLLOWUP_DB not set.");
        await _fx.ResetAsync();

        Guid complaintId;
        using (var scope = _fx.Services.CreateScope())
        {
            var m = scope.ServiceProvider.GetRequiredService<IMediator>();
            var labId = await m.Send(new CreateLaboratoryCommand { Code = "MGL-FK", Name = "FK Lab", Segment = "A", Governorate = "Cairo" });
            await m.Send(new LogComplaintCommand { LaboratoryId = labId, Category = "Result Quality", ViaChannel = "Phone", Details = "d" });
            var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
            complaintId = (await db.Complaints.AsNoTracking().FirstAsync(c => c.LaboratoryId == new LaboratoryId(labId))).Id.Value;
        }

        using (var scope = _fx.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
            var act = async () => await db.Database.ExecuteSqlRawAsync(
                "UPDATE complaint SET representative_id = {0} WHERE id = {1}", Guid.NewGuid(), complaintId);

            await act.Should().ThrowAsync<PostgresException>().Where(e => e.SqlState == "23503"); // foreign_key_violation
        }
    }
}
