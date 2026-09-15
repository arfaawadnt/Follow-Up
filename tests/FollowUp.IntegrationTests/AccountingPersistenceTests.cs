using FluentAssertions;
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
/// Accounting module at the persistence layer and through the real read side: every ledger round-trips (jsonb lists,
/// enumerations, Money), Serial is DB-generated, the raw-SQL CHECKs hold the domain invariants against writes that bypass
/// the domain (23514), and the two computed reports — the deduction suggestion and the rep statement — derive the right
/// numbers from real rows.
/// </summary>
[Collection("integration")]
public sealed class AccountingPersistenceTests
{
    private readonly IntegrationFixture _fx;
    public AccountingPersistenceTests(IntegrationFixture fx) => _fx = fx;
    private static readonly DateOnly D = new(2026, 9, 13);
    private static string Tag() => Guid.NewGuid().ToString("N")[..8];
    private static Laboratory NewLab(string tag) => Laboratory.Register(LabCode.Create($"MGL-{Random.Shared.Next(100000, 999999)}"), $"Lab {tag}", "B");
    private static Representative NewRep(string tag) => Representative.Register($"Rep {tag}", RepresentativeType.Collector, GoalDuration.Monthly, Money.Zero, Money.Zero);

    [SkippableFact]
    public async Task Every_ledger_round_trips_and_serial_is_db_generated()
    {
        Skip.IfNot(_fx.DatabaseAvailable, "FOLLOWUP_DB not set.");
        var tag = Tag();
        Guid reasonId, treasuryId, entry1, entry2, penaltyId, deductionId, collectionId, incomeId, rep1, rep2;

        using (var scope = _fx.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
            var reason = TreasuryReason.Create($"Fuel {tag}"); db.TreasuryReasons.Add(reason);
            var treasury = Treasury.Create($"Main {tag}", new[] { "Cairo", "Giza" }); db.Treasuries.Add(treasury);
            var e1 = TreasuryEntry.Create(treasury.Id, D, 500m, 0m, reason.Id, "opening cash");
            var e2 = TreasuryEntry.Create(treasury.Id, D, 0m, 120.5m, reason.Id, null);
            db.TreasuryEntries.AddRange(e1, e2);

            var lab = NewLab(tag); db.Laboratories.Add(lab);
            var r1 = NewRep(tag + "a"); var r2 = NewRep(tag + "b"); db.Representatives.AddRange(r1, r2);
            var city = City.FromOracle($"C-{tag}", $"City {tag}", "Cairo"); db.Cities.Add(city);
            var area = Area.FromOracle($"A-{tag}", $"Area {tag}", city.Id); db.Areas.Add(area);

            var penalty = PenaltyRecord.Create(lab.Id, D, "ACC-1", "Patient", "T1", "Wrong", 300m, "T2", "Right", 120m, PenaltyUser.Technician);
            db.PenaltyRecords.Add(penalty);
            var deduction = Deduction.Create(area.Id, D, DeductionReason.PercentageDeal, 987.65m, "Sept", new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));
            db.Deductions.Add(deduction);
            var collection = Collection.Create(lab.Id, D, CollectionType.Group, new[] { r1.Id, r2.Id }, 1000m, 2500m, IbanOption.Iban18, "Cashier", null);
            db.Collections.Add(collection);
            var income = RepIncomeEntry.Create(r1.Id, D, 1500m, "cash"); db.RepIncomeEntries.Add(income);
            await db.SaveChangesAsync();

            reasonId = reason.Id.Value; treasuryId = treasury.Id.Value; entry1 = e1.Id.Value; entry2 = e2.Id.Value;
            penaltyId = penalty.Id.Value; deductionId = deduction.Id.Value; collectionId = collection.Id.Value; incomeId = income.Id.Value;
            rep1 = r1.Id.Value; rep2 = r2.Id.Value;

            // Serial is a PostgreSQL identity populated on insert. EF batches the two entries into one multi-row
            // INSERT … RETURNING, and PostgreSQL does not guarantee the order in which identity values are handed
            // out across that batch — so assert "assigned and distinct", not "increasing in AddRange order".
            e1.Serial.Should().BeGreaterThan(0);
            e2.Serial.Should().BeGreaterThan(0).And.NotBe(e1.Serial);
            penalty.Serial.Should().BeGreaterThan(0);
        }

        using (var scope = _fx.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
            var treasury = await db.Treasuries.AsNoTracking().SingleAsync(t => t.Id == new TreasuryId(treasuryId));
            treasury.Branches.Should().BeEquivalentTo(new[] { "Cairo", "Giza" }, "jsonb string list round-trips");

            var e2 = await db.TreasuryEntries.AsNoTracking().SingleAsync(e => e.Id == new TreasuryEntryId(entry2));
            e2.Credit.Amount.Should().Be(120.5m); e2.Debit.Amount.Should().Be(0m); e2.ReasonId.Should().Be(new TreasuryReasonId(reasonId));

            var penalty = await db.PenaltyRecords.AsNoTracking().SingleAsync(p => p.Id == new PenaltyRecordId(penaltyId));
            penalty.User.Should().BeSameAs(PenaltyUser.Technician, "enumeration persisted by name");
            penalty.PenaltyAmount.Amount.Should().Be(180m);

            var deduction = await db.Deductions.AsNoTracking().SingleAsync(d => d.Id == new DeductionId(deductionId));
            deduction.Reason.Should().BeSameAs(DeductionReason.PercentageDeal);
            deduction.PeriodTo.Should().Be(new DateOnly(2026, 9, 30));

            var collection = await db.Collections.AsNoTracking().SingleAsync(c => c.Id == new CollectionId(collectionId));
            collection.RepIds.Should().BeEquivalentTo(new[] { new RepresentativeId(rep1), new RepresentativeId(rep2) }, "jsonb rep list round-trips");
            collection.Iban.Should().BeSameAs(IbanOption.Iban18);
            collection.Total.Amount.Should().Be(3500m);

            (await db.RepIncomeEntries.AsNoTracking().SingleAsync(i => i.Id == new RepIncomeEntryId(incomeId))).Amount.Amount.Should().Be(1500m);
        }
    }

    [SkippableFact]
    public async Task Database_checks_refuse_ledger_states_the_domain_forbids()
    {
        Skip.IfNot(_fx.DatabaseAvailable, "FOLLOWUP_DB not set.");
        var tag = Tag();
        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();

        var reason = TreasuryReason.Create($"Fuel {tag}"); db.TreasuryReasons.Add(reason);
        var treasury = Treasury.Create($"Main {tag}", new[] { "Cairo" }); db.Treasuries.Add(treasury);
        var entry = TreasuryEntry.Create(treasury.Id, D, 100m, 0m, reason.Id, null); db.TreasuryEntries.Add(entry);
        var lab = NewLab(tag); db.Laboratories.Add(lab);
        var rep = NewRep(tag); db.Representatives.Add(rep);
        var collection = Collection.Create(lab.Id, D, CollectionType.Single, new[] { rep.Id }, 100m, 0m, null, null, null); db.Collections.Add(collection);
        var income = RepIncomeEntry.Create(rep.Id, D, 50m, null); db.RepIncomeEntries.Add(income);
        await db.SaveChangesAsync();
        var eid = entry.Id.Value; var cid = collection.Id.Value; var iid = income.Id.Value;

        async Task Refused(string because, FormattableString sql)
        {
            var act = () => db.Database.ExecuteSqlInterpolatedAsync(sql);
            (await act.Should().ThrowAsync<Npgsql.PostgresException>(because)).Which.SqlState.Should().Be("23514", because);
        }

        await Refused("ck_treasury_entry_one_sided: both sides positive", $"UPDATE treasury_entry SET credit = 50 WHERE id = {eid}");
        await Refused("ck_treasury_entry_one_sided: neither side positive", $"UPDATE treasury_entry SET debit = 0 WHERE id = {eid}");
        await Refused("ck_collection_iban_iff_bank: bank without IBAN", $"UPDATE collection SET bank = 500 WHERE id = {cid}");
        await Refused("ck_collection_iban_iff_bank: IBAN without bank", $"UPDATE collection SET iban = '16' WHERE id = {cid}");
        await Refused("ck_collection_has_amount: nothing collected", $"UPDATE collection SET cash = 0 WHERE id = {cid}");
        await Refused("ck_rep_income_entry_positive", $"UPDATE rep_income_entry SET amount = 0 WHERE id = {iid}");
    }

    [SkippableFact]
    public async Task Deduction_suggestions_derive_from_penalties_and_from_area_income_times_the_deal_percentage()
    {
        Skip.IfNot(_fx.DatabaseAvailable, "FOLLOWUP_DB not set.");
        var tag = Tag();
        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
        var queries = scope.ServiceProvider.GetRequiredService<IAccountingQueries>();

        var city = City.FromOracle($"C-{tag}", $"City {tag}", "Cairo"); db.Cities.Add(city);
        var area = Area.FromOracle($"A-{tag}", $"Area {tag}", city.Id);
        area.SetPercentageDeal(true, 10m); // 10 % deal
        db.Areas.Add(area);
        var noDeal = Area.FromOracle($"A2-{tag}", $"Area2 {tag}", city.Id); db.Areas.Add(noDeal);

        // A lab placed in the area (labs carry the area by name), with synced income of 1,000 on the day.
        var lab = Laboratory.FromOracle(LabCode.Create($"MGL-{Random.Shared.Next(100000, 999999)}"), $"Lab {tag}");
        lab.ApplyOracleMaster($"Lab {tag}", null, null, "Cairo", city.Name, area.Name, null, null);
        db.Laboratories.Add(lab);
        var stat = DailyLabStatistic.For(D, lab.Code.Value.ToUpperInvariant()); stat.Set(5, 20, new Money(1000m)); db.DailyLabStatistics.Add(stat);
        // Two penalties in the period for that lab: (300−120) + (50−80) = 180 − 30 = 150.
        db.PenaltyRecords.Add(PenaltyRecord.Create(lab.Id, D, "A1", "P", "T1", "W", 300m, "T2", "R", 120m, PenaltyUser.Rep));
        db.PenaltyRecords.Add(PenaltyRecord.Create(lab.Id, D, "A2", "P", "T3", "W", 50m, "T4", "R", 80m, PenaltyUser.DataEntry));
        await db.SaveChangesAsync();

        var pct = await queries.SuggestDeductionAsync(area.Id.Value, DeductionReason.PercentageDeal, D, D, OrgScope.Global, CancellationToken.None);
        pct.Value.Should().Be(100m, "1,000 income × 10 %");
        pct.Basis.Should().Contain("10%");

        var pen = await queries.SuggestDeductionAsync(area.Id.Value, DeductionReason.Penalty, D, D, OrgScope.Global, CancellationToken.None);
        pen.Value.Should().Be(150m, "Σ (wrong − right), negatives included");
        pen.Basis.Should().Contain("2 penalty row(s)");

        var act = () => queries.SuggestDeductionAsync(noDeal.Id.Value, DeductionReason.PercentageDeal, D, D, OrgScope.Global, CancellationToken.None);
        await act.Should().ThrowAsync<FollowUp.Application.Common.Exceptions.ValidationException>("an area without an active deal has nothing to multiply by");
    }

    [SkippableFact]
    public async Task Rep_statement_debits_synced_and_manual_income_and_credits_collections_with_a_running_balance()
    {
        Skip.IfNot(_fx.DatabaseAvailable, "FOLLOWUP_DB not set.");
        var tag = Tag();
        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
        var queries = scope.ServiceProvider.GetRequiredService<IAccountingQueries>();

        var rep = NewRep(tag); db.Representatives.Add(rep);
        var other = NewRep(tag + "x"); db.Representatives.Add(other);
        // The rep is the assigned collector of this lab → its synced income is the rep's Oracle-derived debit.
        var lab = NewLab(tag); lab.AssignCollectors(new[] { rep.Id }); db.Laboratories.Add(lab);
        var otherLab = NewLab(tag + "o"); otherLab.AssignCollectors(new[] { other.Id }); db.Laboratories.Add(otherLab);
        var s1 = DailyLabStatistic.For(D, lab.Code.Value.ToUpperInvariant()); s1.Set(3, 10, new Money(1000m)); db.DailyLabStatistics.Add(s1);
        var s2 = DailyLabStatistic.For(D, otherLab.Code.Value.ToUpperInvariant()); s2.Set(3, 10, new Money(99999m)); db.DailyLabStatistics.Add(s2); // not this rep's
        db.RepIncomeEntries.Add(RepIncomeEntry.Create(rep.Id, D, 200m, "real cash"));
        db.Collections.Add(Collection.Create(lab.Id, D, CollectionType.Single, new[] { rep.Id }, 300m, 0m, null, null, null));
        db.Collections.Add(Collection.Create(otherLab.Id, D, CollectionType.Single, new[] { other.Id }, 777m, 0m, null, null, null)); // not this rep's
        await db.SaveChangesAsync();

        var st = await queries.RepStatementAsync(rep.Id.Value, D, D, OrgScope.Global, CancellationToken.None);
        st.Should().NotBeNull();
        st!.RepName.Should().Be(rep.FullName);
        st.Rows.Select(r => r.Kind).Should().Equal("OracleIncome", "ManualIncome", "Collection");
        st.Rows[0].Debit.Should().Be(1000m); st.Rows[0].Balance.Should().Be(1000m);
        st.Rows[1].Debit.Should().Be(200m); st.Rows[1].Balance.Should().Be(1200m);
        st.Rows[2].Credit.Should().Be(300m); st.Rows[2].Balance.Should().Be(900m);
        st.TotalDebit.Should().Be(1200m); st.TotalCredit.Should().Be(300m); st.Balance.Should().Be(900m);

        // A rep outside the caller's geographic scope resolves to null (surfaced as 404 — never a leak).
        // Wildcard everywhere except Branches: a Register()ed rep carries a null Branch, and the rep-scope filter hides a
        // null dimension from any non-wildcard caller (matching ScopeGuard.EnsureInScope(Representative)).
        var narrow = OrgScope.Create(new[] { "Nowhere" }, new[] { "*" }, new[] { "*" }, new[] { "*" }, new[] { "*" }, new[] { "*" });
        (await queries.RepStatementAsync(rep.Id.Value, D, D, narrow, CancellationToken.None)).Should().BeNull();
    }
}
