using FluentAssertions;
using FollowUp.Application.Features.Compensation;
using FollowUp.Domain.Common;
using FollowUp.Domain.Representatives;
using FollowUp.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FollowUp.IntegrationTests;

/// <summary>
/// CPN-8: "Save payouts" was client orchestration — one POST per rep, no rollback, silent abort — so a payroll
/// month could end up half-saved. SaveAllCommissionsCommand now recomputes and persists every in-scope rep in a
/// single transaction. This test proves one call saves a row for every rep; it fails if the handler stops early.
/// </summary>
[Collection("integration")]
public sealed class SaveAllCommissionsTests
{
    private readonly IntegrationFixture _fx;
    public SaveAllCommissionsTests(IntegrationFixture fx) => _fx = fx;

    [SkippableFact]
    public async Task Save_all_persists_a_commission_for_every_in_scope_rep_in_one_call()
    {
        Skip.IfNot(_fx.DatabaseAvailable, "FOLLOWUP_DB not set.");
        await _fx.ResetAsync();

        // ResetAsync doesn't clear reps/commissions — start from a deterministic slate of exactly two reps.
        using (var scope = _fx.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
            await db.Database.ExecuteSqlRawAsync("DELETE FROM rep_commission; DELETE FROM representative;");
            db.Representatives.Add(Representative.Register("Rep One", RepresentativeType.Collector, GoalDuration.Monthly,
                salary: new Money(3000m), target: new Money(2000m)));
            db.Representatives.Add(Representative.Register("Rep Two", RepresentativeType.Collector, GoalDuration.Monthly,
                salary: new Money(3000m), target: new Money(2000m)));
            await db.SaveChangesAsync();
        }

        var period = new YearMonth(2026, 8).Code;
        int saved;
        using (var scope = _fx.Services.CreateScope())
            saved = await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new SaveAllCommissionsCommand(period));

        saved.Should().Be(2, "both in-scope reps are saved in one call");

        using (var scope = _fx.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
            var ym = YearMonth.FromCode(period);
            (await db.Commissions.CountAsync(c => c.Period == ym))
                .Should().Be(2, "a payout row exists for every rep — none left half-saved");
        }
    }
}
