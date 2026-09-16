using FluentAssertions;
using FollowUp.Application.Features.Accounting;
using FollowUp.Application.Features.LabStats;
using FollowUp.Domain.Common;
using FollowUp.Domain.Identity;
using FollowUp.Domain.Laboratories;
using FollowUp.Domain.Reference;
using FollowUp.Domain.Statistics;
using FollowUp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FollowUp.IntegrationTests;

/// <summary>
/// Daily lab statistics split by registration branch (2026-09-16): a lab's day may hold one row per reg branch (plus the
/// branch-less row xlsx imports feed), the Lab Statistics read model names the branch through the Branches reference,
/// the "lab day total" consumers still sum the rows, and the DB keeps (date, lab, branch) unique.
/// </summary>
[Collection("integration")]
public sealed class LabStatsRegBranchTests
{
    private readonly IntegrationFixture _fx;
    public LabStatsRegBranchTests(IntegrationFixture fx) => _fx = fx;

    private static readonly DateOnly D = new(2026, 9, 14);

    [SkippableFact]
    public async Task A_labs_day_splits_by_reg_branch_and_totals_still_sum_the_rows()
    {
        Skip.IfNot(_fx.DatabaseAvailable, "FOLLOWUP_DB not set.");
        var tag = Guid.NewGuid().ToString("N")[..8];
        Laboratory lab; string code; var branchCode = $"RB-{tag}".ToUpperInvariant(); Guid refId;

        using (var scope = _fx.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
            var refItem = RefItem.Create(RefType.Branch, branchCode, $"Reg Branch {tag}", null); db.RefItems.Add(refItem); refId = refItem.Id.Value;
            lab = Laboratory.Register(LabCode.Create($"MGL-{Random.Shared.Next(100000, 999999)}"), $"Lab {tag}", "B"); db.Laboratories.Add(lab);
            code = lab.Code.Value.ToUpperInvariant();
            var known = DailyLabStatistic.For(D, code, branchCode.ToLowerInvariant()); known.Set(2, 5, new Money(100m));   // normalised to upper case
            var other = DailyLabStatistic.For(D, code, "ZZ-UNKNOWN"); other.Set(1, 3, new Money(50m));               // no reference row → code shown
            var legacy = DailyLabStatistic.For(D, code); legacy.Set(1, 1, new Money(10m));                          // branch "" (xlsx / pre-split)
            db.DailyLabStatistics.AddRange(known, other, legacy);
            await db.SaveChangesAsync();
            known.Branch.Should().Be(branchCode);
        }

        try
        {
            using var scope = _fx.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();

            var rows = (await scope.ServiceProvider.GetRequiredService<ILabStatsQueries>().ListAsync(D, D, OrgScope.Global, CancellationToken.None))
                .Where(r => r.LabCode == code).ToList();
            rows.Should().HaveCount(3, "one row per registration branch of the lab's day");
            rows.Select(r => r.RegBranch).Should().BeEquivalentTo(new[] { $"Reg Branch {tag}", "ZZ-UNKNOWN", null });
            rows.Sum(r => r.Income).Should().Be(160m);

            // Consumers that need the lab's day total sum the branch rows (statement by Lab → synced income of the day).
            var st = await scope.ServiceProvider.GetRequiredService<IAccountingQueries>().StatementAsync(StatementBy.Lab, lab.Id.Value, D, D, OrgScope.Global, CancellationToken.None);
            st!.Rows.Should().ContainSingle(r => r.Kind == "OracleIncome").Which.Debit.Should().Be(160m);

            var dup = () => db.Database.ExecuteSqlInterpolatedAsync($@"
INSERT INTO daily_lab_statistic (id, date, lab_code, branch, registrations, test_count, income)
VALUES ({Guid.NewGuid()}, {D}, {code}, {branchCode}, 1, 1, 1)");
            (await dup.Should().ThrowAsync<Npgsql.PostgresException>()).Which.SqlState.Should().Be("23505", "(date, lab, reg branch) is unique");
        }
        finally
        {
            using var scope = _fx.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM daily_lab_statistic WHERE lab_code = {code}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM laboratory WHERE id = {lab.Id.Value}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM ref_item WHERE id = {refId}");
        }
    }
}
