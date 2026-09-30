using FluentAssertions;
using FollowUp.Application.Common.Abstractions.Persistence;
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
/// 2026-09-30: the Rep Income Revision query lists every entered sheet line with its LDM side (income, accessions, tests,
/// not verified, late additions) and, when a rep is chosen, the rep's labs with LDM income but no entry; the save command
/// upserts the reviewer's figures and clears an all-zero line.
/// </summary>
[Collection("integration")]
public sealed class RepIncomeRevisionTests
{
    private readonly IntegrationFixture _fx;
    public RepIncomeRevisionTests(IntegrationFixture fx) => _fx = fx;

    [SkippableFact]
    public async Task Revision_lines_carry_the_entered_and_ldm_sides_and_the_saved_actuals()
    {
        Skip.IfNot(_fx.DatabaseAvailable, "FOLLOWUP_DB not set.");
        var tag = Guid.NewGuid().ToString("N")[..8]; var day = new DateOnly(2026, 9, 3);
        Representative rep; Laboratory entered, ldmOnly;
        using (var scope = _fx.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
            rep = Representative.Register($"Resp {tag}", RepresentativeType.LabResponsible, GoalDuration.Monthly, Money.Zero, Money.Zero); db.Representatives.Add(rep);
            entered = Laboratory.Register(LabCode.Create($"MGL-{Random.Shared.Next(100000, 999999)}"), $"Entered lab {tag}", "A"); entered.AssignResponsible(rep.Id);
            ldmOnly = Laboratory.Register(LabCode.Create($"MGL-{Random.Shared.Next(100000, 999999)}"), $"Missed lab {tag}", "B"); ldmOnly.AssignResponsible(rep.Id);
            db.Laboratories.AddRange(entered, ldmOnly);
            var s1 = DailyLabStatistic.For(day, entered.Code.Value); s1.Set(2, 3, new Money(90m));
            var s2 = DailyLabStatistic.For(day, ldmOnly.Code.Value); s2.Set(1, 1, new Money(40m));
            db.DailyLabStatistics.AddRange(s1, s2);
            db.RepLabIncomes.Add(RepLabIncome.Create(rep.Id, entered.Id, day, 2, 100m, 60m, 10m, "sheet"));
            var reg = new DateTime(2026, 9, 3, 9, 0, 0);
            db.DetailedRegistrations.AddRange(
                DetailedRegistration.Create(day, entered.Code.Value, "BR1", "ACC-1", "P1", "GLU", 0, "Glucose", 30m, 0m, "3", "5", reg, reg.AddSeconds(20)),  // verified, with the reg
                DetailedRegistration.Create(day, entered.Code.Value, "BR1", "ACC-1", "P1", "CBC", 0, "CBC", 30m, 0m, "3", "4", reg, reg.AddHours(1)),        // reviewed → not verified, within 3 h
                DetailedRegistration.Create(day, entered.Code.Value, "BR1", "ACC-2", "P2", "TSH", 0, "TSH", 30m, 0m, "2", "1", reg, reg.AddHours(5)),        // ordered → not verified, after 3 h
                DetailedRegistration.Create(day, ldmOnly.Code.Value, "BR1", "ACC-3", "P3", "TSH", 0, "TSH", 40m, 0m, "3", "5", reg, reg));
            await db.SaveChangesAsync();
        }
        try
        {
            using (var scope = _fx.Services.CreateScope())
            {
                var q = scope.ServiceProvider.GetRequiredService<IAccountingQueries>();
                // Without a rep: only the entered line.
                var all = await q.RepIncomeRevisionAsync(day, day, null, null, OrgScope.Global, true, CancellationToken.None);
                var line = all.Should().ContainSingle(r => r.LaboratoryId == entered.Id.Value).Subject;
                all.Should().NotContain(r => r.LaboratoryId == ldmOnly.Id.Value, "a lab without an entry is listed only when its rep is chosen");
                line.EntryId.Should().NotBeNull(); line.RepName.Should().Be(rep.FullName);
                line.Samples.Should().Be(2); line.TotalRequired.Should().Be(100m); line.Paid.Should().Be(60m); line.Remaining.Should().Be(40m); line.DelayedPayment.Should().Be(10m); line.Notes.Should().Be("sheet");
                line.EnteredAt.Should().NotBeNull();
                line.LdmIncome.Should().Be(90m); line.Accessions.Should().Be(2); line.Tests.Should().Be(3);
                line.NotVerified.Should().Be(2); line.AddedWithin3h.Should().Be(1); line.AddedAfter3h.Should().Be(1);
                line.RevisionId.Should().BeNull(); line.ActualIncome.Should().BeNull();
                // With the rep: the missed lab appears without an entry, with its LDM side.
                var byRep = await q.RepIncomeRevisionAsync(day, day, rep.Id.Value, null, OrgScope.Global, true, CancellationToken.None);
                var missed = byRep.Should().ContainSingle(r => r.LaboratoryId == ldmOnly.Id.Value).Subject;
                missed.EntryId.Should().BeNull(); missed.TotalRequired.Should().Be(0m); missed.LdmIncome.Should().Be(40m); missed.Tests.Should().Be(1); missed.NotVerified.Should().Be(0);
                // Lab filter narrows to one lab.
                (await q.RepIncomeRevisionAsync(day, day, null, ldmOnly.Id.Value, OrgScope.Global, true, CancellationToken.None)).Should().BeEmpty("no entry and no rep chosen");
            }

            // Save the actuals, then clear them.
            using (var scope = _fx.Services.CreateScope())
            {
                var handler = ActivatorUtilities.CreateInstance<SaveRepIncomeRevisionHandler>(scope.ServiceProvider);
                var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
                var id = await handler.Handle(new SaveRepIncomeRevisionCommand(entered.Id.Value, rep.Id.Value, day, 95m, 60m, 5m, "checked"), CancellationToken.None);
                await db.SaveChangesAsync();
                id.Should().NotBeNull();
                var again = await handler.Handle(new SaveRepIncomeRevisionCommand(entered.Id.Value, rep.Id.Value, day, 90m, 70m, 5m, "checked twice"), CancellationToken.None);
                await db.SaveChangesAsync();
                again.Should().Be(id, "the same rep × lab × day updates the existing revision");
                var over = () => handler.Handle(new SaveRepIncomeRevisionCommand(entered.Id.Value, rep.Id.Value, day, 50m, 60m, 0m, null), CancellationToken.None);
                await over.Should().ThrowAsync<DomainException>();
            }
            using (var scope = _fx.Services.CreateScope())
            {
                var q = scope.ServiceProvider.GetRequiredService<IAccountingQueries>();
                var line = (await q.RepIncomeRevisionAsync(day, day, null, entered.Id.Value, OrgScope.Global, true, CancellationToken.None)).Single();
                line.RevisionId.Should().NotBeNull(); line.ActualIncome.Should().Be(90m); line.ActualPaid.Should().Be(70m); line.ActualRemaining.Should().Be(20m);
                line.ActualDelayedPayment.Should().Be(5m); line.RevisionNotes.Should().Be("checked twice"); line.RevisedBy.Should().NotBeNullOrEmpty(); line.RevisedAt.Should().NotBeNull();
            }
            using (var scope = _fx.Services.CreateScope())
            {
                var handler = ActivatorUtilities.CreateInstance<SaveRepIncomeRevisionHandler>(scope.ServiceProvider);
                var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
                (await handler.Handle(new SaveRepIncomeRevisionCommand(entered.Id.Value, rep.Id.Value, day, 0m, 0m, 0m, " "), CancellationToken.None)).Should().BeNull("an all-zero line clears the revision");
                await db.SaveChangesAsync();
                (await db.RepIncomeRevisions.CountAsync(v => v.LaboratoryId == entered.Id)).Should().Be(0);
            }
        }
        finally
        {
            using var scope = _fx.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM rep_income_revision WHERE representative_id = {rep.Id.Value}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM detailed_registration WHERE lab_code IN ({entered.Code.Value}, {ldmOnly.Code.Value})");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM daily_lab_statistic WHERE lab_code IN ({entered.Code.Value}, {ldmOnly.Code.Value})");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM rep_lab_income WHERE representative_id = {rep.Id.Value}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM laboratory WHERE id IN ({entered.Id.Value}, {ldmOnly.Id.Value})");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM representative WHERE id = {rep.Id.Value}");
        }
    }
}
