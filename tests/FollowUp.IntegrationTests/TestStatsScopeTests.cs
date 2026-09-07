using FluentAssertions;
using FollowUp.Application.Features.TestCatalogue;
using FollowUp.Domain.Common;
using FollowUp.Domain.Identity;
using FollowUp.Domain.Reference;
using FollowUp.Domain.Statistics;
using FollowUp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FollowUp.IntegrationTests;

/// <summary>
/// Finding B-6 / STAT-002: test statistics carry only a branch dimension. A branch-restricted caller must see
/// only their branch's rows. The role scope holds branch NAMES while the stat holds a branch CODE, so the read
/// resolves the code to a name before matching.
/// </summary>
[Collection("integration")]
public sealed class TestStatsScopeTests
{
    private readonly IntegrationFixture _fx;
    public TestStatsScopeTests(IntegrationFixture fx) => _fx = fx;

    [SkippableFact]
    public async Task Test_statistics_are_scoped_by_branch()
    {
        Skip.IfNot(_fx.DatabaseAvailable, "FOLLOWUP_DB not set.");
        await _fx.ResetAsync();

        var day = new DateOnly(2099, 1, 15); // unique day so the assertions see only this test's rows
        try
        {
            using (var scope = _fx.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
                db.RefItems.Add(RefItem.Create(RefType.Branch, "B6-CAI", "B6 Cairo Branch", null));
                db.RefItems.Add(RefItem.Create(RefType.Branch, "B6-GIZ", "B6 Giza Branch", null));
                var cai = TestStatistic.For(day, "B6T100", 0, "B6-CAI"); cai.SetCount(5); cai.SetIncome(new Money(500));
                var giz = TestStatistic.For(day, "B6T200", 0, "B6-GIZ"); giz.SetCount(7); giz.SetIncome(new Money(700));
                db.TestStatistics.AddRange(cai, giz);
                await db.SaveChangesAsync();
            }

            using (var scope = _fx.Services.CreateScope())
            {
                var q = scope.ServiceProvider.GetRequiredService<ITestCatalogueQueries>();

                var cairoScope = OrgScope.Create(new[] { "B6 Cairo Branch" }, new[] { "*" }, new[] { "*" }, new[] { "*" }, new[] { "*" }, new[] { "*" });
                var scoped = await q.GetTestStatsAsync(day, day, cairoScope, CancellationToken.None);
                scoped.Select(r => r.TestCode).Should().Contain("B6T100");
                scoped.Select(r => r.TestCode).Should().NotContain("B6T200", "a Giza-branch stat is outside a Cairo-branch scope");

                var all = await q.GetTestStatsAsync(day, day, OrgScope.Global, CancellationToken.None);
                all.Select(r => r.TestCode).Should().Contain(new[] { "B6T100", "B6T200" }, "a branch-unrestricted caller sees all branches");
            }
        }
        finally
        {
            using var scope = _fx.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
            await db.Database.ExecuteSqlRawAsync(
                "DELETE FROM test_statistic WHERE test_code IN ('B6T100','B6T200'); DELETE FROM ref_item WHERE code IN ('B6-CAI','B6-GIZ');");
        }
    }
}
