using FluentAssertions;
using FollowUp.Application.Common.Abstractions;
using FollowUp.Application.Common.Abstractions.Persistence;
using FollowUp.Domain.Integration;
using FollowUp.Infrastructure.Jobs;
using FollowUp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FollowUp.IntegrationTests;

/// <summary>
/// The LabStats Oracle feed is grouped by (day, lab, reg.branch_code) since 2026-09-16. This drives the real
/// <see cref="OracleSyncRunner"/> with a fake reader returning the feed's own column shape (THE_DATE, LAB_CODE, BRANCH,
/// REG_COUNT, TEST_COUNT, INCOME) and checks the rows land keyed by the triple with their own counts, that the lab-day
/// total is the sum of the branch rows, and that a re-synced window drops a branch Oracle no longer reports.
/// </summary>
[Collection("integration")]
public sealed class LabStatsRegBranchSyncTests
{
    private readonly IntegrationFixture _fx;
    public LabStatsRegBranchSyncTests(IntegrationFixture fx) => _fx = fx;

    private static readonly DateOnly D = new(2026, 9, 10);

    private static OracleRow Row(DateOnly date, string lab, string? branch, int regs, int tests, decimal income) => new(new Dictionary<string, object?>
    {
        ["THE_DATE"] = date.ToDateTime(TimeOnly.MinValue),
        ["LAB_CODE"] = lab,
        ["BRANCH"] = branch,
        ["REG_COUNT"] = regs,
        ["TEST_COUNT"] = tests,
        ["INCOME"] = income,
    });

    [SkippableFact]
    public async Task Sync_stores_one_row_per_reg_branch_and_a_resync_drops_a_vanished_branch()
    {
        Skip.IfNot(_fx.DatabaseAvailable, "FOLLOWUP_DB not set.");
        var lab = $"MGL-RB{Random.Shared.Next(1000, 9999)}";

        using (var scope = _fx.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
            if (!await db.OracleConfigs.AnyAsync()) { scope.ServiceProvider.GetRequiredService<IOracleConfigRepository>().Add(OracleConfig.Create(enabled: true, intervalHours: 24)); await db.SaveChangesAsync(); }
        }

        try
        {
            // Day 1 from Oracle: the lab registered at two branches (and one registration with no branch code).
            using (var scope = _fx.Services.CreateScope())
            {
                var reader = new FakeReader(new[] { Row(D, lab, "cai", 3, 8, 800m), Row(D, lab, "GIZ", 2, 4, 400m), Row(D, lab, null, 1, 1, 50m) });
                var runner = ActivatorUtilities.CreateInstance<OracleSyncRunner>(scope.ServiceProvider, reader);
                var r = await runner.RunLabStatsAsync(D, D, manual: true, CancellationToken.None);
                r.Ran.Should().BeTrue(); r.LabsUpserted.Should().Be(3);

                var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
                var rows = await db.DailyLabStatistics.AsNoTracking().Where(s => s.LabCode == lab).OrderBy(s => s.Branch).ToListAsync();
                rows.Select(s => (s.Branch, s.Registrations, s.TestCount, s.Income.Amount)).Should().BeEquivalentTo(new[]
                {
                    ("", 1, 1, 50m),          // NULL branch_code → the branch-less row
                    ("CAI", 3, 8, 800m),      // codes are normalised to upper case
                    ("GIZ", 2, 4, 400m),
                });
                rows.Sum(s => s.Registrations).Should().Be(6); rows.Sum(s => s.TestCount).Should().Be(13); rows.Sum(s => s.Income.Amount).Should().Be(1250m,
                    "the lab-day total every other consumer sums is exactly the old single-row figure");
            }

            // Re-sync the same window: CAI grew, GIZ vanished, the branch-less row stays → GIZ is dropped as a stale key.
            using (var scope = _fx.Services.CreateScope())
            {
                var reader = new FakeReader(new[] { Row(D, lab, "CAI", 4, 9, 900m), Row(D, lab, null, 1, 1, 50m) });
                var runner = ActivatorUtilities.CreateInstance<OracleSyncRunner>(scope.ServiceProvider, reader);
                (await runner.RunLabStatsAsync(D, D, manual: true, CancellationToken.None)).LabsUpserted.Should().Be(2);

                var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
                var rows = await db.DailyLabStatistics.AsNoTracking().Where(s => s.LabCode == lab).OrderBy(s => s.Branch).ToListAsync();
                rows.Select(s => (s.Branch, s.Registrations, s.Income.Amount)).Should().BeEquivalentTo(new[] { ("", 1, 50m), ("CAI", 4, 900m) });
            }
        }
        finally
        {
            using var scope = _fx.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM daily_lab_statistic WHERE lab_code = {lab}");
        }
    }

    private sealed class FakeReader : IOracleReader
    {
        private readonly IReadOnlyList<OracleRow> _rows;
        public FakeReader(IReadOnlyList<OracleRow> rows) => _rows = rows;
        public Task<IReadOnlyList<OracleRow>> ExecuteAsync(string queryName, CancellationToken ct) => Task.FromResult(_rows);
        public Task<IReadOnlyList<OracleRow>> ExecuteAsync(string queryName, OracleDateWindow window, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<OracleRow>>(queryName == "LabStats" ? _rows : Array.Empty<OracleRow>());
    }
}
