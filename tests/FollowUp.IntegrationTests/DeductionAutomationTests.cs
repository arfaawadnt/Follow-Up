using FluentAssertions;
using FollowUp.Application.Common.Abstractions;
using FollowUp.Application.Features.Accounting;
using FollowUp.Domain.Accounting;
using FollowUp.Domain.Common;
using FollowUp.Domain.Identity;
using FollowUp.Domain.Laboratories;
using FollowUp.Domain.Reference;
using FollowUp.Domain.Representatives;
using FollowUp.Domain.Statistics;
using FollowUp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FollowUp.IntegrationTests;

/// <summary>
/// The deductions automation (2026-09-15) through the real runner, read side and database: one Percentage Deal row per
/// area per month that follows the synced income day by day, stops following once an operator adjusts it, and is never
/// duplicated; a recorded penalty never becomes a deduction (2026-09-18); the DB refuses the shapes the domain forbids.
/// </summary>
[Collection("integration")]
public sealed class DeductionAutomationTests
{
    private readonly IntegrationFixture _fx;
    public DeductionAutomationTests(IntegrationFixture fx) => _fx = fx;

    private static string Tag() => Guid.NewGuid().ToString("N")[..8];

    [SkippableFact]
    public async Task Daily_run_keeps_one_auto_deal_row_per_area_month_current_until_adjusted_and_never_mirrors_penalties()
    {
        Skip.IfNot(_fx.DatabaseAvailable, "FOLLOWUP_DB not set.");
        var tag = Tag();
        var d10 = new DateOnly(2026, 9, 10); var d11 = new DateOnly(2026, 9, 11);
        Area area, noDeal; Laboratory lab; Representative rep; PenaltyRecord legacyPenalty;

        using (var scope = _fx.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
            var city = City.FromOracle($"C-{tag}", $"City {tag}", "Cairo"); db.Cities.Add(city);
            area = Area.FromOracle($"A-{tag}", $"Area {tag}", city.Id); area.SetPercentageDeal(true, 10m); db.Areas.Add(area);
            noDeal = Area.FromOracle($"N-{tag}", $"NoDeal {tag}", city.Id); db.Areas.Add(noDeal);
            lab = Laboratory.FromOracle(LabCode.Create($"MGL-{Random.Shared.Next(100000, 999999)}"), $"Lab {tag}");
            lab.ApplyOracleMaster($"Lab {tag}", null, null, "Cairo", city.Name, area.Name, null, null);
            db.Laboratories.Add(lab);
            var code = lab.Code.Value.ToUpperInvariant();
            var s10 = DailyLabStatistic.For(d10, code); s10.Set(5, 20, new Money(1000m)); db.DailyLabStatistics.Add(s10);
            var s11 = DailyLabStatistic.For(d11, code); s11.Set(5, 20, new Money(500m)); db.DailyLabStatistics.Add(s11);
            rep = Representative.Register($"Rep {tag}", RepresentativeType.Collector, GoalDuration.Monthly, Money.Zero, Money.Zero); db.Representatives.Add(rep);
            // A penalty on the area's lab: it must never appear among the area's deductions.
            legacyPenalty = PenaltyRecord.Create(lab.Id, d10, "ACC-L", "Patient", "T1", "W", 300m, "T2", "R", 120m, PenaltyUser.Rep, null, rep.Id);
            db.PenaltyRecords.Add(legacyPenalty);
            await db.SaveChangesAsync();
        }

        try
        {
            // ---- Day 10: first run creates the month's row from the income so far and links the legacy penalty.
            using (var scope = _fx.Services.CreateScope())
            {
                var runner = scope.ServiceProvider.GetRequiredService<IDeductionAutomationRunner>();
                var r = await runner.RunAsync(d10, manual: true, CancellationToken.None);
                r.Month.Should().Be("2026-09");
                r.DealCreated.Should().BeGreaterThanOrEqualTo(1);

                var queries = scope.ServiceProvider.GetRequiredService<IAccountingQueries>();
                var rows = await queries.DeductionsAsync(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), area.Id.Value, OrgScope.Global, CancellationToken.None);
                var deal = rows.Should().ContainSingle(x => x.Origin == "AutoDeal").Subject;
                deal.Value.Should().Be(100m, "1,000 income through the 10th × 10 %");
                deal.Date.Should().Be(new DateOnly(2026, 9, 1)); deal.PeriodFrom.Should().Be(new DateOnly(2026, 9, 1)); deal.PeriodTo.Should().Be(d10);
                deal.IsAdjusted.Should().BeFalse(); deal.SystemNote.Should().Contain("Auto-calculated").And.Contain("10%");
                rows.Should().OnlyContain(x => x.Origin == "AutoDeal", "the penalty on the area's lab is not a deduction");
                (await queries.DeductionsAsync(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), noDeal.Id.Value, OrgScope.Global, CancellationToken.None))
                    .Should().BeEmpty("an area without a deal gets no automated row");
            }

            // ---- Day 11: the same row follows the income (no second row); the penalty is not linked twice.
            using (var scope = _fx.Services.CreateScope())
            {
                var runner = scope.ServiceProvider.GetRequiredService<IDeductionAutomationRunner>();
                var r = await runner.RunAsync(d11, manual: true, CancellationToken.None);
                r.DealRecalculated.Should().BeGreaterThanOrEqualTo(1);

                var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
                var deals = await db.Deductions.AsNoTracking().Where(d => d.AreaId == area.Id && d.Origin == DeductionOrigin.AutoDeal).ToListAsync();
                var deal = deals.Should().ContainSingle().Subject;
                deal.Value.Amount.Should().Be(150m, "1,500 income through the 11th × 10 %");
                deal.PeriodTo.Should().Be(d11);
                (await db.Deductions.CountAsync(d => d.AreaId == area.Id)).Should().Be(1, "still only the deal row");
            }

            // ---- An operator adjusts the value: the next run leaves the row alone and reports it skipped.
            using (var scope = _fx.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
                var deal = await db.Deductions.SingleAsync(d => d.AreaId == area.Id && d.Origin == DeductionOrigin.AutoDeal);
                deal.Adjust(999m, "negotiated", "Suggested: manual override");
                await db.SaveChangesAsync();
            }
            using (var scope = _fx.Services.CreateScope())
            {
                var runner = scope.ServiceProvider.GetRequiredService<IDeductionAutomationRunner>();
                var r = await runner.RunAsync(d11, manual: true, CancellationToken.None);
                r.DealSkippedAdjusted.Should().BeGreaterThanOrEqualTo(1);
                var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
                var deal = await db.Deductions.AsNoTracking().SingleAsync(d => d.AreaId == area.Id && d.Origin == DeductionOrigin.AutoDeal);
                deal.Value.Amount.Should().Be(999m); deal.IsAdjusted.Should().BeTrue(); deal.Notes.Should().Be("negotiated");

                // ---- The DB refuses the shapes the domain forbids (23514 / 23505).
                var badOrigin = () => db.Database.ExecuteSqlInterpolatedAsync($@"
INSERT INTO deduction (id, date, area_id, reason, ""value"", origin, is_adjusted, created_at, created_by)
VALUES ({Guid.NewGuid()}, {d10}, {area.Id.Value}, 'Transportation', 1, 'AutoPenalty', false, now(), 'test')");
                (await badOrigin.Should().ThrowAsync<Npgsql.PostgresException>()).Which.SqlState.Should().Be("23514", "AutoPenalty is no longer an origin");
                var dupDeal = () => db.Database.ExecuteSqlInterpolatedAsync($@"
INSERT INTO deduction (id, date, area_id, reason, ""value"", origin, is_adjusted, period_from, period_to, created_at, created_by)
VALUES ({Guid.NewGuid()}, {new DateOnly(2026, 9, 1)}, {area.Id.Value}, 'PercentageDeal', 1, 'AutoDeal', false, {new DateOnly(2026, 9, 1)}, {d11}, now(), 'test')");
                (await dupDeal.Should().ThrowAsync<Npgsql.PostgresException>()).Which.SqlState.Should().Be("23505", "one AutoDeal row per area per month");
            }
        }
        finally
        {
            using var scope = _fx.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM deduction WHERE area_id IN ({area.Id.Value}, {noDeal.Id.Value})");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM penalty_record WHERE laboratory_id = {lab.Id.Value}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM daily_lab_statistic WHERE lab_code = {lab.Code.Value.ToUpperInvariant()}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM laboratory WHERE id = {lab.Id.Value}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM representative WHERE id = {rep.Id.Value}");
        }
    }
}
