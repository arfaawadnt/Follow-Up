using FluentAssertions;
using FollowUp.Domain.Identity;
using FollowUp.Application.Features.AreaStats;
using FollowUp.Domain.Common;
using FollowUp.Domain.Laboratories;
using FollowUp.Domain.Statistics;
using FollowUp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FollowUp.IntegrationTests;

/// <summary>
/// 2026-09-28: Area Statistics carries the lab segment as an extra grain so the page can filter by segment (multi-select).
/// Two labs of the same area but different segments produce two rows for the day; their sum is still the area's total,
/// so the governorate → area grouping on the page is unchanged.
/// </summary>
[Collection("integration")]
public sealed class AreaStatsSegmentTests
{
    private readonly IntegrationFixture _fx;
    public AreaStatsSegmentTests(IntegrationFixture fx) => _fx = fx;

    [SkippableFact]
    public async Task Area_rows_are_split_by_lab_segment_and_still_sum_to_the_area_total()
    {
        Skip.IfNot(_fx.DatabaseAvailable, "FOLLOWUP_DB not set.");
        var tag = Guid.NewGuid().ToString("N")[..8]; var area = $"Area {tag}"; var day = new DateOnly(2026, 9, 27);
        Laboratory a, b;
        using (var scope = _fx.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
            a = Laboratory.Register(LabCode.Create($"MGL-{Random.Shared.Next(100000, 999999)}"), $"Lab A {tag}", "A"); a.PlaceInHierarchy("Cairo", "Cairo", null, area);
            b = Laboratory.Register(LabCode.Create($"MGL-{Random.Shared.Next(100000, 999999)}"), $"Lab B {tag}", "B"); b.PlaceInHierarchy("Cairo", "Cairo", null, area);
            db.Laboratories.AddRange(a, b);
            var sa = DailyLabStatistic.For(day, a.Code.Value); sa.Set(3, 30, new Money(300m));
            var sb = DailyLabStatistic.For(day, b.Code.Value); sb.Set(2, 20, new Money(200m));
            db.DailyLabStatistics.AddRange(sa, sb);
            await db.SaveChangesAsync();
        }
        try
        {
            using var scope = _fx.Services.CreateScope();
            var rows = (await scope.ServiceProvider.GetRequiredService<IAreaStatsQueries>().ListAsync(day, day, OrgScope.Global, CancellationToken.None))
                .Where(r => r.Area == area).ToList();
            rows.Should().HaveCount(2, "one row per (date, geography, branch, segment)");
            rows.Select(r => r.Segment).Should().BeEquivalentTo(new[] { "A", "B" });
            rows.Single(r => r.Segment == "A").TestCount.Should().Be(30);
            rows.Sum(r => r.TestCount).Should().Be(50, "the page sums the segments back into the area total");
            rows.Sum(r => r.Income).Should().Be(500m);
        }
        finally
        {
            using var scope = _fx.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM daily_lab_statistic WHERE lab_code IN ({a.Code.Value}, {b.Code.Value})");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM laboratory WHERE id IN ({a.Id.Value}, {b.Id.Value})");
        }
    }
}
