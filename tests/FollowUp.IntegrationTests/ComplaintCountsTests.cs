using FluentAssertions;
using FollowUp.Application.Features.Complaints.Commands;
using FollowUp.Application.Features.Complaints.Contracts;
using FollowUp.Application.Features.Laboratories.CreateLaboratory;
using FollowUp.Domain.Identity;
using FollowUp.Domain.Laboratories;
using FollowUp.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FollowUp.IntegrationTests;

/// <summary>
/// CMP-16: the status-pill counts were computed client-side from a single 100-row page, so they were wrong past
/// 100 rows and under a status filter. CountsAsync computes them server-side over the whole in-scope set; this
/// proves the per-status breakdown is correct regardless of which status is being viewed.
/// </summary>
[Collection("integration")]
public sealed class ComplaintCountsTests
{
    private readonly IntegrationFixture _fx;
    public ComplaintCountsTests(IntegrationFixture fx) => _fx = fx;

    [SkippableFact]
    public async Task Counts_reflect_every_status_across_the_whole_in_scope_set()
    {
        Skip.IfNot(_fx.DatabaseAvailable, "FOLLOWUP_DB not set.");
        await _fx.ResetAsync();

        using (var scope = _fx.Services.CreateScope())
        {
            var m = scope.ServiceProvider.GetRequiredService<IMediator>();
            var labId = await m.Send(new CreateLaboratoryCommand { Code = "MGL-CNT", Name = "Counts Lab", Segment = "A", Governorate = "Cairo" });
            await m.Send(new LogComplaintCommand { LaboratoryId = labId, Category = "Result Quality", ViaChannel = "Phone", Details = "one" });
            await m.Send(new LogComplaintCommand { LaboratoryId = labId, Category = "Result Quality", ViaChannel = "Phone", Details = "two" });

            var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
            var first = await db.Complaints.AsNoTracking()
                .Where(c => c.LaboratoryId == new LaboratoryId(labId)).OrderBy(c => c.Number).FirstAsync();
            await m.Send(new StartComplaintCommand(first.Id.Value)); // Open -> InProgress
        }

        using (var scope = _fx.Services.CreateScope())
        {
            var queries = scope.ServiceProvider.GetRequiredService<IComplaintQueries>();
            var counts = await queries.CountsAsync(OrgScope.Global, null, null, CancellationToken.None);

            counts.Total.Should().Be(2);
            counts.Open.Should().Be(1);
            counts.InProgress.Should().Be(1);
            counts.Resolved.Should().Be(0);
        }
    }
}
