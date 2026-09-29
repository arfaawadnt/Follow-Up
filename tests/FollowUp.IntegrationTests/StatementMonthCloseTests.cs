using FluentAssertions;
using FollowUp.Application.Common.Abstractions.Persistence;
using FollowUp.Application.Features.Accounting;
using FollowUp.Domain.Accounting;
using FollowUp.Domain.Common;
using FollowUp.Domain.Identity;
using FollowUp.Domain.Laboratories;
using FollowUp.Domain.Representatives;
using FollowUp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FollowUp.IntegrationTests;

/// <summary>
/// 2026-09-28: closing a month of a Lab Responsible's statement stores its balance, ends the month with a closing-balance
/// line, opens the next month with the same balance, refuses writes dated inside the closed month, closes in order and
/// reopens only the latest month.
/// </summary>
[Collection("integration")]
public sealed class StatementMonthCloseTests
{
    private readonly IntegrationFixture _fx;
    public StatementMonthCloseTests(IntegrationFixture fx) => _fx = fx;

    [SkippableFact]
    public async Task Closing_a_month_carries_its_balance_and_locks_it()
    {
        Skip.IfNot(_fx.DatabaseAvailable, "FOLLOWUP_DB not set.");
        var tag = Guid.NewGuid().ToString("N")[..8];
        var aug = new DateOnly(2026, 8, 26); // August 2026 has ended on every day this test can run
        Representative rep; Laboratory lab;
        using (var scope = _fx.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
            rep = Representative.Register($"Resp {tag}", RepresentativeType.LabResponsible, GoalDuration.Monthly, Money.Zero, Money.Zero); db.Representatives.Add(rep);
            lab = Laboratory.Register(LabCode.Create($"MGL-{Random.Shared.Next(100000, 999999)}"), $"Lab {tag}", "A"); lab.AssignResponsible(rep.Id);
            db.Laboratories.Add(lab);
            db.RepLabIncomes.Add(RepLabIncome.Create(rep.Id, lab.Id, aug, 5, 1000m, 1000m, 0m, null)); // August debit 1000
            db.RepLabIncomes.Add(RepLabIncome.Create(rep.Id, lab.Id, new DateOnly(2026, 9, 3), 2, 300m, 300m, 0m, null)); // September debit 300
            await db.SaveChangesAsync();
        }
        try
        {
            // Close August: balance 1000 (nothing earlier for this rep).
            using (var scope = _fx.Services.CreateScope())
            {
                var handler = ActivatorUtilities.CreateInstance<CloseStatementMonthHandler>(scope.ServiceProvider);
                var dto = await handler.Handle(new CloseStatementMonthCommand(rep.Id.Value, 2026, 8, "first close"), CancellationToken.None);
                await scope.ServiceProvider.GetRequiredService<FollowUpDbContext>().SaveChangesAsync();
                dto.ClosingBalance.Should().Be(1000m); dto.IsLatest.Should().BeTrue();

                // Out of order / not ended / already closed.
                var again = () => handler.Handle(new CloseStatementMonthCommand(rep.Id.Value, 2026, 8, null), CancellationToken.None);
                await again.Should().ThrowAsync<DomainException>().WithMessage("*already closed*");
                var skip = () => handler.Handle(new CloseStatementMonthCommand(rep.Id.Value, 2026, 6, null), CancellationToken.None);
                await skip.Should().ThrowAsync<DomainException>().WithMessage("*in order*");
                var future = () => handler.Handle(new CloseStatementMonthCommand(rep.Id.Value, 2027, 12, null), CancellationToken.None);
                await future.Should().ThrowAsync<DomainException>();
            }

            using (var scope = _fx.Services.CreateScope())
            {
                var q = scope.ServiceProvider.GetRequiredService<IAccountingQueries>();
                // August alone: the closing line zeroes the month.
                var augSt = await q.StatementAsync(StatementBy.Responsible, rep.Id.Value, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31), OrgScope.Global, CancellationToken.None);
                var closing = augSt!.Rows.Should().ContainSingle(r => r.Kind == "ClosingBalance").Subject;
                closing.Date.Should().Be(new DateOnly(2026, 8, 31)); closing.Credit.Should().Be(1000m); closing.Balance.Should().Be(0m);
                closing.Notes.Should().Contain("first close").And.NotContain("≠", "the stored balance still matches the computed one");
                augSt.Rows.Last().Kind.Should().Be("ClosingBalance");
                // September alone: opens with August's balance, then its own activity.
                var sepSt = await q.StatementAsync(StatementBy.Responsible, rep.Id.Value, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 27), OrgScope.Global, CancellationToken.None);
                var opening = sepSt!.Rows.First();
                opening.Kind.Should().Be("OpeningBalance"); opening.Date.Should().Be(new DateOnly(2026, 9, 1)); opening.Debit.Should().Be(1000m);
                sepSt.Balance.Should().Be(1300m);
                // Both months in one range: closing + opening between them, same final balance.
                var both = await q.StatementAsync(StatementBy.Responsible, rep.Id.Value, new DateOnly(2026, 8, 1), new DateOnly(2026, 9, 27), OrgScope.Global, CancellationToken.None);
                both!.Rows.Select(r => r.Kind).Should().ContainInOrder("TotalRequired", "ClosingBalance", "OpeningBalance", "TotalRequired");
                both.Balance.Should().Be(1300m);
                // Listed for the page.
                var closes = await q.MonthClosesAsync(rep.Id.Value, OrgScope.Global, CancellationToken.None);
                closes.Should().ContainSingle().Which.Should().Match<StatementMonthCloseDto>(c => c.Year == 2026 && c.Month == 8 && c.ClosingBalance == 1000m && c.IsLatest);

                // The closed month refuses writes; the open one does not.
                var guard = scope.ServiceProvider.GetRequiredService<IStatementCloseGuard>();
                var blocked = () => guard.EnsureOpenForRepsAsync(new[] { rep.Id }, aug, CancellationToken.None);
                await blocked.Should().ThrowAsync<DomainException>().WithMessage("*August 2026 is closed*");
                var viaLab = () => guard.EnsureOpenForPenaltyAsync(lab.Id, null, new DateOnly(2026, 8, 2), CancellationToken.None);
                await viaLab.Should().ThrowAsync<DomainException>();
                await guard.EnsureOpenForRepsAsync(new[] { rep.Id }, new DateOnly(2026, 9, 3), CancellationToken.None);
            }

            // Reopen August: the lines disappear and the month accepts writes again.
            using (var scope = _fx.Services.CreateScope())
            {
                var handler = ActivatorUtilities.CreateInstance<ReopenStatementMonthHandler>(scope.ServiceProvider);
                await handler.Handle(new ReopenStatementMonthCommand(rep.Id.Value, 2026, 8), CancellationToken.None);
                await scope.ServiceProvider.GetRequiredService<FollowUpDbContext>().SaveChangesAsync();
            }
            using (var scope = _fx.Services.CreateScope())
            {
                var q = scope.ServiceProvider.GetRequiredService<IAccountingQueries>();
                var sepSt = await q.StatementAsync(StatementBy.Responsible, rep.Id.Value, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 27), OrgScope.Global, CancellationToken.None);
                sepSt!.Rows.Should().NotContain(r => r.Kind == "OpeningBalance");
                await scope.ServiceProvider.GetRequiredService<IStatementCloseGuard>().EnsureOpenForRepsAsync(new[] { rep.Id }, aug, CancellationToken.None);
            }
        }
        finally
        {
            using var scope = _fx.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM statement_month_close WHERE representative_id = {rep.Id.Value}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM rep_lab_income WHERE representative_id = {rep.Id.Value}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM laboratory WHERE id = {lab.Id.Value}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM representative WHERE id = {rep.Id.Value}");
        }
    }
}
