using FluentAssertions;
using FollowUp.Domain.Laboratories;
using FollowUp.Domain.Operations;
using FollowUp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FollowUp.IntegrationTests;

/// <summary>
/// Finding M-14 / BIZ-003: the lab foreign keys on outsource_sample / complaint / marketing_visit were
/// OnDelete(Cascade), so deleting a lab would silently destroy financial and regulated records. They are now
/// Restrict — a lab with dependent records cannot be deleted.
/// </summary>
[Collection("integration")]
public sealed class LabDeleteRestrictTests
{
    private readonly IntegrationFixture _fx;
    public LabDeleteRestrictTests(IntegrationFixture fx) => _fx = fx;

    [SkippableFact]
    public async Task Deleting_a_lab_with_an_outsource_record_is_restricted()
    {
        Skip.IfNot(_fx.DatabaseAvailable, "FOLLOWUP_DB not set.");
        await _fx.ResetAsync();

        Guid labId;
        using (var scope = _fx.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
            var lab = Laboratory.Register(LabCode.Create("MGL-DR1"), "DR Lab", "A");
            db.Laboratories.Add(lab);
            db.OutsourceSamples.Add(OutsourceSample.Create(lab.Id, new DateOnly(2026, 8, 15), "Dest", 2));
            await db.SaveChangesAsync();
            labId = lab.Id.Value;
        }

        using (var scope = _fx.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
            var lab = await db.Laboratories.FirstAsync(l => l.Id == new LaboratoryId(labId));
            db.Laboratories.Remove(lab);

            var act = async () => await db.SaveChangesAsync();

            await act.Should().ThrowAsync<DbUpdateException>("the outsource FK is Restrict, so the delete is blocked");
        }
    }
}
