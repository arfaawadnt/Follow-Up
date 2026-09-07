using FluentAssertions;
using FollowUp.Domain.Laboratories;
using FollowUp.Domain.Operations;
using FollowUp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FollowUp.IntegrationTests;

/// <summary>
/// Finding M-11 / BIZ-002: OutsourceSample is edited by several concurrent paths but carried no concurrency
/// token, so conflicting writes were silent last-writer-wins. With the xmin token, a stale write must raise a
/// concurrency conflict (mapped to 409 by TransactionBehavior) instead of overwriting.
/// </summary>
[Collection("integration")]
public sealed class OutsourceConcurrencyTests
{
    private readonly IntegrationFixture _fx;
    public OutsourceConcurrencyTests(IntegrationFixture fx) => _fx = fx;

    [SkippableFact]
    public async Task Concurrent_updates_conflict_on_the_row_version()
    {
        Skip.IfNot(_fx.DatabaseAvailable, "FOLLOWUP_DB not set.");
        await _fx.ResetAsync();

        Guid sampleId;
        using (var scope = _fx.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
            var lab = Laboratory.Register(LabCode.Create("MGL-OC1"), "OC Lab", "A");
            db.Laboratories.Add(lab);
            var sample = OutsourceSample.Create(lab.Id, new DateOnly(2026, 8, 15), "Dest", 3);
            db.OutsourceSamples.Add(sample);
            await db.SaveChangesAsync();
            sampleId = sample.Id.Value;
        }

        using var scopeA = _fx.Services.CreateScope();
        using var scopeB = _fx.Services.CreateScope();
        var dbA = scopeA.ServiceProvider.GetRequiredService<FollowUpDbContext>();
        var dbB = scopeB.ServiceProvider.GetRequiredService<FollowUpDbContext>();

        var a = await dbA.OutsourceSamples.FirstAsync(x => x.Id == new OutsourceSampleId(sampleId));
        var b = await dbB.OutsourceSamples.FirstAsync(x => x.Id == new OutsourceSampleId(sampleId)); // same xmin

        a.Update(10, "DestA", "notesA");
        await dbA.SaveChangesAsync(); // commits first — bumps xmin

        b.Update(20, "DestB", "notesB");
        var act = async () => await dbB.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateConcurrencyException>("the stale write must not silently overwrite");
    }
}
