using FluentAssertions;
using FollowUp.Application.Common.Security;
using FollowUp.Application.Features.Accounting;
using FollowUp.Domain.Accounting;
using FollowUp.Domain.Common;
using FollowUp.Domain.Identity;
using FollowUp.Domain.Laboratories;
using FollowUp.Domain.Operations;
using FollowUp.Domain.Reference;
using FollowUp.Domain.Representatives;
using FollowUp.Domain.Statistics;
using FollowUp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FollowUp.IntegrationTests;

/// <summary>
/// Rep Statement "real income" sheet (2026-09-16) through the real read side and database: the Lab Responsibles linked to
/// an area, the sheet rows (the rep's labs with a recorded visit that day — live board or archive — plus labs already
/// entered) with their view-only context (LDM income, visit figures, penalty, remaining carried from earlier days), the
/// statement's TotalRequired debit, and the DB guards (one line per rep × lab × date; paid ≤ total required).
/// </summary>
[Collection("integration")]
public sealed class RealIncomeSheetTests
{
    private readonly IntegrationFixture _fx;
    public RealIncomeSheetTests(IntegrationFixture fx) => _fx = fx;

    private static readonly DateOnly D = new(2026, 9, 14);
    private static string Tag() => Guid.NewGuid().ToString("N")[..8];
    private static Laboratory NewLab(string tag, string area)
    {
        var lab = Laboratory.Register(LabCode.Create($"MGL-{Random.Shared.Next(100000, 999999)}"), $"Lab {tag}", "B");
        lab.PlaceInHierarchy("BR", "Cairo", null, area);
        return lab;
    }

    [SkippableFact]
    public async Task Sheet_lists_the_reps_visited_labs_with_context_and_its_lines_feed_the_statement()
    {
        Skip.IfNot(_fx.DatabaseAvailable, "FOLLOWUP_DB not set.");
        var tag = Tag();
        City city; Area area; Laboratory l1, l2, l3; Representative rep, other;

        using (var scope = _fx.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
            city = City.FromOracle($"C-{tag}", $"City {tag}", "Cairo"); db.Cities.Add(city);
            area = Area.FromOracle($"A-{tag}", $"Area {tag}", city.Id); db.Areas.Add(area);
            rep = Representative.Register($"Resp {tag}", RepresentativeType.LabResponsible, GoalDuration.Monthly, Money.Zero, Money.Zero);
            other = Representative.Register($"Other {tag}", RepresentativeType.LabResponsible, GoalDuration.Monthly, Money.Zero, Money.Zero);
            db.Representatives.AddRange(rep, other);
            l1 = NewLab(tag + "1", area.Name); l1.AssignResponsible(rep.Id);     // visited on D → on the sheet
            l2 = NewLab(tag + "2", area.Name); l2.AssignResponsible(rep.Id);     // no visit on D → only once entered by hand
            l3 = NewLab(tag + "3", area.Name); l3.AssignResponsible(other.Id);   // the other responsible's lab → never on rep's sheet
            db.Laboratories.AddRange(l1, l2, l3);

            // L1: today's board (live) check-in with the figures the sheet shows; L3: archived check-in (other rep).
            var v1 = DailyVisit.Schedule(l1.Id, null, D, new TimeOnly(9, 0)); v1.CheckIn(7, "collector", DateTimeOffset.UtcNow, totalRequired: 900); db.DailyVisits.Add(v1);
            var v3 = DailyVisit.Schedule(l3.Id, null, D, new TimeOnly(9, 0)); v3.CheckIn(4, "collector", DateTimeOffset.UtcNow, totalRequired: 400);
            db.VisitHistory.Add(VisitHistory.ArchiveFrom(v3, DateTimeOffset.UtcNow));
            // L2 was visited the day before — not this date.
            var v2 = DailyVisit.Schedule(l2.Id, null, D.AddDays(-1), new TimeOnly(9, 0)); v2.CheckIn(1, "collector", DateTimeOffset.UtcNow, totalRequired: 50);
            db.VisitHistory.Add(VisitHistory.ArchiveFrom(v2, DateTimeOffset.UtcNow));

            var s = DailyLabStatistic.For(D, l1.Code.Value.ToUpperInvariant()); s.Set(3, 10, new Money(1234.5m)); db.DailyLabStatistics.Add(s);
            // Every penalty type counts on the sheet as right − wrong (2026-09-18): (120 − 300) + (100 − 500) = −580.
            db.PenaltyRecords.Add(PenaltyRecord.Create(l1.Id, D, "ACC-1", "Patient", "T1", "Wrong", 300m, "T2", "Right", 120m, PenaltyUser.LabRequest, null, null));
            db.PenaltyRecords.Add(PenaltyRecord.Create(l1.Id, D, "ACC-2", "Patient", "T1", "Wrong", 500m, "T2", "Right", 100m, PenaltyUser.Rep, null, rep.Id));
            // Earlier sheet lines of L1: remaining 200 (D-2) and a 50 delayed payment (D-1) → 150 carried into D.
            db.RepLabIncomes.Add(RepLabIncome.Create(rep.Id, l1.Id, D.AddDays(-2), 5, 500m, 300m, 0m, null));
            db.RepLabIncomes.Add(RepLabIncome.Create(rep.Id, l1.Id, D.AddDays(-1), 2, 100m, 100m, 50m, null));
            await db.SaveChangesAsync();
        }

        try
        {
            using var scope = _fx.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
            var queries = scope.ServiceProvider.GetRequiredService<IAccountingQueries>();

            // Lab Responsibles linked to the area, with how many of its labs they hold.
            var reps = await queries.RealIncomeRepsAsync(area.Id.Value, OrgScope.Global, CancellationToken.None);
            reps.Should().Contain(r => r.Id == rep.Id.Value && r.LabCount == 2);
            reps.Should().Contain(r => r.Id == other.Id.Value && r.LabCount == 1);
            (await queries.RealIncomeLabsAsync(area.Id.Value, OrgScope.Global, true, CancellationToken.None)).Select(l => l.Id)
                .Should().BeEquivalentTo(new[] { l1.Id.Value, l2.Id.Value, l3.Id.Value });

            // The sheet before any entry: only the rep's lab visited that day, with its context.
            var sheet = await queries.RealIncomeSheetAsync(area.Id.Value, D, rep.Id.Value, OrgScope.Global, true, CancellationToken.None);
            sheet.Should().NotBeNull();
            sheet!.RepName.Should().Be(rep.FullName); sheet.AreaName.Should().Be(area.Name);
            var row = sheet.Rows.Should().ContainSingle().Subject;
            row.LaboratoryId.Should().Be(l1.Id.Value);
            row.HasVisit.Should().BeTrue(); row.VisitTotalRequired.Should().Be(900); row.VisitSamples.Should().Be(7);
            row.LdmIncome.Should().Be(1234.5m); row.Penalty.Should().Be(-580m, "Σ right − wrong of both penalties");
            row.PreviousRemaining.Should().Be(150m, "(500 − 300) + (100 − 100) − 50 delayed");
            row.EntryId.Should().BeNull(); row.TotalRequired.Should().Be(0m);

            // Enter the day: L1 partly paid + a delayed payment; L2 added by hand (no visit).
            db.RepLabIncomes.Add(RepLabIncome.Create(rep.Id, l1.Id, D, 7, 900m, 600m, 150m, "partly"));
            db.RepLabIncomes.Add(RepLabIncome.Create(rep.Id, l2.Id, D, 2, 100m, 100m, 0m, null));
            await db.SaveChangesAsync();

            sheet = await queries.RealIncomeSheetAsync(area.Id.Value, D, rep.Id.Value, OrgScope.Global, true, CancellationToken.None);
            sheet!.Rows.Select(r => r.LaboratoryId).Should().BeEquivalentTo(new[] { l1.Id.Value, l2.Id.Value }, "an entered lab stays on the sheet even without a visit");
            var r1 = sheet.Rows.Single(r => r.LaboratoryId == l1.Id.Value);
            r1.EntryId.Should().NotBeNull(); r1.Paid.Should().Be(600m); r1.Remaining.Should().Be(300m); r1.DelayedPayment.Should().Be(150m); r1.Notes.Should().Be("partly");
            var r2 = sheet.Rows.Single(r => r.LaboratoryId == l2.Id.Value);
            r2.HasVisit.Should().BeFalse(); r2.VisitSamples.Should().BeNull(); r2.Paid.Should().Be(100m);
            // The other responsible's sheet has only their own visited lab.
            (await queries.RealIncomeSheetAsync(area.Id.Value, D, other.Id.Value, OrgScope.Global, true, CancellationToken.None))!
                .Rows.Select(r => r.LaboratoryId).Should().Equal(l3.Id.Value);
            // The next day carries L1's new remaining: 150 + (900 − 600) − 150 = 300.
            var next = await queries.RealIncomeSheetAsync(area.Id.Value, D.AddDays(1), rep.Id.Value, OrgScope.Global, true, CancellationToken.None);
            next!.Rows.Should().BeEmpty("no visit and no entry on that day yet");

            // Statement (2026-09-18): Debit = the sheet's total required (900 + 100) + the right test of each penalty; Credit = the
            // wrong test of each penalty. Both penalties land on this rep: the Rep one because the rep made it, the lab request
            // because the rep is L1's Lab Responsible. Paid / delayed / synced income no longer post to the statement.
            var st = await queries.RepStatementAsync(rep.Id.Value, D, D, OrgScope.Global, CancellationToken.None);
            var required = st!.Rows.Should().ContainSingle(r => r.Kind == "TotalRequired").Subject;
            required.Debit.Should().Be(1000m); required.Notes.Should().Contain("2 lab");
            st.Rows.Should().NotContain(r => r.Kind == "OracleIncome" || r.Kind == "RealIncome");
            st.Rows.Where(r => r.Kind == "PenaltyRight").Select(r => r.Debit).Should().BeEquivalentTo(new[] { 120m, 100m });
            st.Rows.Where(r => r.Kind == "PenaltyWrong").Select(r => r.Credit).Should().BeEquivalentTo(new[] { 300m, 500m });
            var byRep = await queries.StatementAsync(StatementBy.Responsible, rep.Id.Value, D, D, OrgScope.Global, CancellationToken.None);
            byRep!.SubjectName.Should().Be(rep.FullName);
            byRep.TotalDebit.Should().Be(1220m); byRep.TotalCredit.Should().Be(800m);
            // By Area: every lab of the area — the other responsible's L3 has no sheet line, so total required stays 1000; the same penalties.
            var byArea = await queries.StatementAsync(StatementBy.Area, area.Id.Value, D, D, OrgScope.Global, CancellationToken.None);
            byArea!.SubjectName.Should().Be(area.Name);
            byArea.Rows.Should().Contain(r => r.Kind == "TotalRequired" && r.Debit == 1000m);
            byArea.TotalCredit.Should().Be(800m);
            byArea.Rows.Should().NotContain(r => r.Kind == "Collection" || r.Kind == "ManualIncome", "collections belong to reps, not areas");
            // By Lab: L2 has only its hand-entered line.
            var byLab = await queries.StatementAsync(StatementBy.Lab, l2.Id.Value, D, D, OrgScope.Global, CancellationToken.None);
            byLab!.SubjectName.Should().Be(l2.Name);
            byLab.Rows.Should().ContainSingle().Which.Debit.Should().Be(100m);
            (await queries.StatementAsync(StatementBy.Lab, Guid.NewGuid(), D, D, OrgScope.Global, CancellationToken.None)).Should().BeNull();

            // DB guards.
            var repPenaltyId = await db.PenaltyRecords.AsNoTracking().Where(p => p.LaboratoryId == l1.Id && p.AccNo == "ACC-2").Select(p => p.Id.Value).SingleAsync();
            var retype = () => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE penalty_record SET penalty_user = 'LabRequest' WHERE id = {repPenaltyId}");
            (await retype.Should().ThrowAsync<Npgsql.PostgresException>()).Which.SqlState.Should().Be("23514", "a lab-request penalty names nobody (ck_penalty_record_performed_by)");
            var oneSided = () => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE penalty_record SET right_test_code = NULL, right_test_name = NULL, right_value = 0 WHERE id = {repPenaltyId}");
            (await oneSided.Should().ThrowAsync<Npgsql.PostgresException>()).Which.SqlState.Should().Be("23514", "a staff penalty keeps both tests (ck_penalty_record_tests)");
            var dup = () => db.Database.ExecuteSqlInterpolatedAsync($@"
INSERT INTO rep_lab_income (id, date, representative_id, laboratory_id, samples, total_required, paid, delayed_payment, created_at, created_by)
VALUES ({Guid.NewGuid()}, {D}, {rep.Id.Value}, {l1.Id.Value}, 1, 10, 5, 0, now(), 'test')");
            (await dup.Should().ThrowAsync<Npgsql.PostgresException>()).Which.SqlState.Should().Be("23505", "one line per rep × lab × date");
            var over = () => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE rep_lab_income SET paid = total_required + 1 WHERE laboratory_id = {l2.Id.Value} AND date = {D}");
            (await over.Should().ThrowAsync<Npgsql.PostgresException>()).Which.SqlState.Should().Be("23514", "paid never above the total required");
        }
        finally
        {
            using var scope = _fx.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
            var labIds = new[] { l1.Id.Value, l2.Id.Value, l3.Id.Value };
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM rep_lab_income WHERE laboratory_id = ANY({labIds})");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM penalty_record WHERE laboratory_id = ANY({labIds})");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM daily_lab_statistic WHERE lab_code = {l1.Code.Value.ToUpperInvariant()}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM visit_history WHERE laboratory_id = ANY({labIds})");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM daily_visit WHERE laboratory_id = ANY({labIds})");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM laboratory WHERE id = ANY({labIds})");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM representative WHERE id IN ({rep.Id.Value}, {other.Id.Value})");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM area WHERE id = {area.Id.Value}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM city WHERE id = {city.Id.Value}");
        }
    }
}
