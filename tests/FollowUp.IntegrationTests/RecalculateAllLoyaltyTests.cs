using FluentAssertions;
using FollowUp.Application.Features.Compensation;
using FollowUp.Application.Features.Laboratories.CreateLaboratory;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace FollowUp.IntegrationTests;

/// <summary>
/// Coverage for the global loyalty recalculation (finding CPN-11 — RecalculateAll had no test). Exercises the
/// real scoped enumeration (GetLoyaltySummaryAsync) so every in-scope lab is recalculated in one pass.
/// </summary>
[Collection("integration")]
public sealed class RecalculateAllLoyaltyTests
{
    private readonly IntegrationFixture _fx;
    public RecalculateAllLoyaltyTests(IntegrationFixture fx) => _fx = fx;

    [SkippableFact]
    public async Task Recalculate_all_processes_every_in_scope_lab()
    {
        Skip.IfNot(_fx.DatabaseAvailable, "FOLLOWUP_DB not set.");
        await _fx.ResetAsync(); // clears laboratory, so exactly the two seeded labs are in scope

        using (var scope = _fx.Services.CreateScope())
        {
            var m = scope.ServiceProvider.GetRequiredService<IMediator>();
            await m.Send(new CreateLaboratoryCommand { Code = "MGL-RA1", Name = "Lab One", Segment = "A", Governorate = "Cairo" });
            await m.Send(new CreateLaboratoryCommand { Code = "MGL-RA2", Name = "Lab Two", Segment = "A", Governorate = "Giza" });
        }

        int recalculated;
        using (var scope = _fx.Services.CreateScope())
            recalculated = await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new RecalculateAllLoyaltyCommand());

        recalculated.Should().Be(2, "every in-scope lab's loyalty is recalculated in one pass");
    }
}
