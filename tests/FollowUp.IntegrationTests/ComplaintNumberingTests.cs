using FluentAssertions;
using FollowUp.Application.Features.Complaints.Commands;
using FollowUp.Application.Features.Laboratories.CreateLaboratory;
using FollowUp.Domain.Laboratories;
using FollowUp.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FollowUp.IntegrationTests;

/// <summary>
/// Finding M-12 / CMP-7: the complaint number was a read-max-plus-one with no lock, so concurrent creation
/// could collide on the number (unique index → raw 500). The transaction-scoped advisory lock must serialize
/// creation so every concurrent complaint gets a distinct number.
/// </summary>
[Collection("integration")]
public sealed class ComplaintNumberingTests
{
    private readonly IntegrationFixture _fx;
    public ComplaintNumberingTests(IntegrationFixture fx) => _fx = fx;

    [SkippableFact]
    public async Task Concurrent_complaint_creation_yields_distinct_numbers()
    {
        Skip.IfNot(_fx.DatabaseAvailable, "FOLLOWUP_DB not set.");
        await _fx.ResetAsync();
        _fx.Idempotency.CurrentKey = null; // each command executes (no idempotent replay)

        Guid labId;
        using (var scope = _fx.Services.CreateScope())
        {
            var m = scope.ServiceProvider.GetRequiredService<IMediator>();
            labId = await m.Send(new CreateLaboratoryCommand { Code = "MGL-CN1", Name = "CN Lab", Segment = "A", Governorate = "Cairo" });
        }

        const int n = 6;
        await Task.WhenAll(Enumerable.Range(0, n).Select(i => Task.Run(async () =>
        {
            using var scope = _fx.Services.CreateScope();
            var m = scope.ServiceProvider.GetRequiredService<IMediator>();
            await m.Send(new LogComplaintCommand { LaboratoryId = labId, Category = "TAT", ViaChannel = "Phone", Details = $"c{i}" });
        })));

        using (var scope = _fx.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
            var numbers = await db.Complaints.Where(c => c.LaboratoryId == new LaboratoryId(labId)).Select(c => c.Number).ToListAsync();
            numbers.Should().HaveCount(n);
            numbers.Distinct().Should().HaveCount(n, "concurrent creation must not collide on the complaint number");
        }
    }
}
