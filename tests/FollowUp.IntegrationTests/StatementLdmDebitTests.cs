using FluentAssertions;
using FollowUp.Application.Features.Accounting;
using FollowUp.Domain.Accounting;
using FollowUp.Domain.Common;
using FollowUp.Domain.Identity;
using FollowUp.Domain.Laboratories;
using FollowUp.Domain.Representatives;
using FollowUp.Domain.Statistics;
using FollowUp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FollowUp.IntegrationTests;

/// <summary>
/// 2026-09-28: the Rep Statement debit of a day is the Rep Income "total required" for the labs that have a sheet entry
/// PLUS the LDM income (synced lab statistics) of the subject's labs that have none; every debit line carries the LDM
/// income of the labs it covers, and the details query lists the synced registrations behind it, split by kind.
/// </summary>
[Collection("integration")]
public sealed class StatementLdmDebitTests
{
    private readonly IntegrationFixture _fx;
    public StatementLdmDebitTests(IntegrationFixture fx) => _fx = fx;

    [SkippableFact]
    public async Task Labs_without_a_sheet_entry_post_their_ldm_income_and_the_details_split_by_kind()
    {
        Skip.IfNot(_fx.DatabaseAvailable, "FOLLOWUP_DB not set.");
        var tag = Guid.NewGuid().ToString("N")[..8]; var day = new DateOnly(2026, 9, 26);
        Representative rep; Laboratory withSheet, ldmOnly;
        using (var scope = _fx.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
            rep = Representative.Register($"Resp {tag}", RepresentativeType.LabResponsible, GoalDuration.Monthly, Money.Zero, Money.Zero); db.Representatives.Add(rep);
            withSheet = Laboratory.Register(LabCode.Create($"MGL-{Random.Shared.Next(100000, 999999)}"), $"Sheet lab {tag}", "A"); withSheet.AssignResponsible(rep.Id);
            ldmOnly = Laboratory.Register(LabCode.Create($"MGL-{Random.Shared.Next(100000, 999999)}"), $"LDM lab {tag}", "B"); ldmOnly.AssignResponsible(rep.Id);
            db.Laboratories.AddRange(withSheet, ldmOnly);
            // LDM income that day: 900 for the sheet lab (the sheet says 1000), 450 for the lab nobody entered.
            var s1 = DailyLabStatistic.For(day, withSheet.Code.Value); s1.Set(5, 12, new Money(900m));
            var s2 = DailyLabStatistic.For(day, ldmOnly.Code.Value); s2.Set(3, 7, new Money(450m));
            db.DailyLabStatistics.AddRange(s1, s2);
            db.RepLabIncomes.Add(RepLabIncome.Create(rep.Id, withSheet.Id, day, 5, 1000m, 1000m, 0m, null));
            // Two registration lines per lab so the details query has something to list. Creation stamps (2026-09-28): the CBC
            // was added 2 h after its registration (within 3 h), the TSH 5 h after (after 3 h), the glucose with the registration.
            var reg = new DateTime(2026, 9, 26, 9, 0, 0);
            db.DetailedRegistrations.AddRange(
                DetailedRegistration.Create(day, withSheet.Code.Value, "BR1", "ACC-S1", "Patient S", "GLU", 0, "Glucose", 500m, 0m, "3", "5", reg, reg.AddSeconds(30)),
                DetailedRegistration.Create(day, withSheet.Code.Value, "BR1", "ACC-S1", "Patient S", "CBC", 0, "CBC", 400m, 0m, "3", "5", reg, reg.AddHours(2)),
                DetailedRegistration.Create(day, ldmOnly.Code.Value, "BR1", "ACC-L1", "Patient L", "TSH", 0, "TSH", 450m, 0m, "2", "1", reg, reg.AddHours(5)));
            await db.SaveChangesAsync();
        }
        try
        {
            using var scope = _fx.Services.CreateScope();
            var q = scope.ServiceProvider.GetRequiredService<IAccountingQueries>();
            var st = await q.StatementAsync(StatementBy.Responsible, rep.Id.Value, day, day, OrgScope.Global, CancellationToken.None);
            st.Should().NotBeNull();
            var sheet = st!.Rows.Should().ContainSingle(r => r.Kind == "TotalRequired").Subject;
            sheet.Debit.Should().Be(1000m, "the manual total required wins for a lab with a sheet entry");
            sheet.LdmIncome.Should().Be(900m, "the LDM income of the labs the line covers is shown beside it");
            sheet.Notes.Should().Contain("tests added within 3 h 400.00").And.Contain("after 3 h 0.00", "the note totals the fees of late-added tests");
            var ldm = st.Rows.Should().ContainSingle(r => r.Kind == "LdmIncome").Subject;
            ldm.Debit.Should().Be(450m, "a lab without a sheet entry posts its LDM income as the debit");
            ldm.LdmIncome.Should().Be(450m);
            ldm.Notes.Should().Contain("tests added within 3 h 0.00").And.Contain("after 3 h 450.00");
            st.TotalDebit.Should().Be(1450m);

            var sheetDetails = await q.StatementLdmDetailsAsync(StatementBy.Responsible, rep.Id.Value, day, "TotalRequired", OrgScope.Global, true, CancellationToken.None);
            sheetDetails.Select(d => d.AccNo).Distinct().Should().BeEquivalentTo(new[] { "ACC-S1" });
            sheetDetails.Sum(d => d.Fee).Should().Be(900m);
            sheetDetails.Should().OnlyContain(d => d.LabName == withSheet.Name);
            sheetDetails.Single(d => d.TestCode == "GLU").TestAddition.Should().Be("None", "30 s after the registration is inside the grace window");
            sheetDetails.Single(d => d.TestCode == "CBC").TestAddition.Should().Be("Within3Hours");
            var ldmDetails = await q.StatementLdmDetailsAsync(StatementBy.Responsible, rep.Id.Value, day, "LdmIncome", OrgScope.Global, true, CancellationToken.None);
            ldmDetails.Should().ContainSingle().Which.Should().Match<StatementLdmDetailDto>(d => d.AccNo == "ACC-L1" && d.TestStatus == "1" && d.SampleStatus == "2" && d.TestAddition == "Over3Hours");
        }
        finally
        {
            using var scope = _fx.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM detailed_registration WHERE lab_code IN ({withSheet.Code.Value}, {ldmOnly.Code.Value})");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM daily_lab_statistic WHERE lab_code IN ({withSheet.Code.Value}, {ldmOnly.Code.Value})");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM rep_lab_income WHERE representative_id = {rep.Id.Value}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM laboratory WHERE id IN ({withSheet.Id.Value}, {ldmOnly.Id.Value})");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM representative WHERE id = {rep.Id.Value}");
        }
    }
}
