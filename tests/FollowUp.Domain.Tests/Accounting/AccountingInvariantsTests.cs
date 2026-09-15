using FluentAssertions;
using FollowUp.Domain.Accounting;
using FollowUp.Domain.Common;
using FollowUp.Domain.Laboratories;
using FollowUp.Domain.Reference;
using FollowUp.Domain.Representatives;
using Xunit;

namespace FollowUp.Domain.Tests.Accounting;

/// <summary>Accounting module domain invariants — the rules the DB CHECKs mirror and the report math relies on.</summary>
public class AccountingInvariantsTests
{
    private static readonly DateOnly D = new(2026, 9, 13);

    // ---- Enumerations ----

    [Fact]
    public void Enumerations_expose_the_agreed_fixed_values()
    {
        Enumeration.GetAll<PenaltyUser>().Select(e => e.Name).Should().BeEquivalentTo(new[] { "Rep", "DataEntry", "Technician" });
        Enumeration.GetAll<DeductionReason>().Select(e => e.Name).Should().BeEquivalentTo(new[] { "Transportation", "Penalty", "PercentageDeal" });
        Enumeration.GetAll<CollectionType>().Select(e => e.Name).Should().BeEquivalentTo(new[] { "Single", "Group" });
        // The IBAN is a fixed three-value pick (operator decision), persisted by these names.
        Enumeration.GetAll<IbanOption>().Select(e => e.Name).Should().BeEquivalentTo(new[] { "12", "16", "18" });
    }

    // ---- Treasury ----

    [Fact]
    public void A_treasury_must_be_assigned_to_at_least_one_branch()
    {
        var act = () => Treasury.Create("Main", Array.Empty<string>());
        act.Should().Throw<DomainException>().WithMessage("*at least one branch*");

        var t = Treasury.Create("Main", new[] { "Cairo", " cairo ", "Giza" });
        t.Branches.Should().BeEquivalentTo(new[] { "Cairo", "Giza" }, "branches are trimmed and de-duplicated case-insensitively");
    }

    [Fact]
    public void A_treasury_entry_is_one_sided_debit_or_credit()
    {
        var tid = TreasuryId.New(); var rid = TreasuryReasonId.New();
        var debit = TreasuryEntry.Create(tid, D, 500m, 0m, rid, null);
        debit.Debit.Amount.Should().Be(500m); debit.Credit.Amount.Should().Be(0m);

        var credit = TreasuryEntry.Create(tid, D, 0m, 120.505m, rid, "fuel");
        credit.Credit.Amount.Should().Be(120.50m, "Money rounds to 2dp (banker's)");

        var both = () => TreasuryEntry.Create(tid, D, 10m, 10m, rid, null);
        both.Should().Throw<DomainException>().WithMessage("*exactly one*");
        var neither = () => TreasuryEntry.Create(tid, D, 0m, 0m, rid, null);
        neither.Should().Throw<DomainException>().WithMessage("*exactly one*");
        var negative = () => TreasuryEntry.Create(tid, D, -1m, 0m, rid, null);
        negative.Should().Throw<DomainException>().WithMessage("*cannot be negative*");
    }

    // ---- Penalty ----

    [Fact]
    public void Penalty_amount_is_wrong_minus_right_and_may_be_negative()
    {
        var rep = FollowUp.Domain.Representatives.RepresentativeId.New();
        var p = PenaltyRecord.Create(LaboratoryId.New(), D, "ACC-1", "Patient", "T1", "Wrong test", 300m, "T2", "Right test", 120m, PenaltyUser.Rep, null, rep);
        p.PenaltyAmount.Amount.Should().Be(180m, "the over-charge is the penalty (operator decision)");
        p.PerformedByRepId.Should().Be(rep);

        var tech = FollowUp.Domain.Identity.AppUserId.New();
        p.Update(D, "ACC-1", "Patient", "T1", "Wrong", 100m, "T2", "Right", 250m, PenaltyUser.Technician, tech, null);
        p.PenaltyAmount.Amount.Should().Be(-150m, "an under-charge is a negative penalty and must not be clamped");
        p.UserType.Should().BeSameAs(PenaltyUser.Technician);
        p.PerformedByUserId.Should().Be(tech);
        p.PerformedByRepId.Should().BeNull("re-attributing to a system user releases the representative link");
    }

    [Fact]
    public void A_penalty_requires_its_identifying_fields_and_non_negative_values()
    {
        var lab = LaboratoryId.New();
        var rep = FollowUp.Domain.Representatives.RepresentativeId.New();
        FluentActions.Invoking(() => PenaltyRecord.Create(lab, D, "", "Patient", "T1", "W", 1m, "T2", "R", 1m, PenaltyUser.Rep, null, rep))
            .Should().Throw<DomainException>().WithMessage("*Acc No is required*");
        FluentActions.Invoking(() => PenaltyRecord.Create(lab, D, "A", "Patient", "T1", "W", -1m, "T2", "R", 1m, PenaltyUser.Rep, null, rep))
            .Should().Throw<DomainException>().WithMessage("*cannot be negative*");
    }

    [Fact]
    public void A_penalty_names_exactly_the_person_matching_its_user_type()
    {
        var lab = LaboratoryId.New();
        var rep = FollowUp.Domain.Representatives.RepresentativeId.New();
        var user = FollowUp.Domain.Identity.AppUserId.New();
        PenaltyRecord Make(PenaltyUser type, FollowUp.Domain.Identity.AppUserId? u, FollowUp.Domain.Representatives.RepresentativeId? r) =>
            PenaltyRecord.Create(lab, D, "A", "P", "T1", "W", 1m, "T2", "R", 1m, type, u, r);

        // Rep → a representative, and only a representative.
        FluentActions.Invoking(() => Make(PenaltyUser.Rep, null, null)).Should().Throw<DomainException>().WithMessage("*representative*");
        FluentActions.Invoking(() => Make(PenaltyUser.Rep, user, rep)).Should().Throw<DomainException>().WithMessage("*cannot also name a system user*");
        Make(PenaltyUser.Rep, null, rep).PerformedByRepId.Should().Be(rep);

        // DataEntry / Technician → a system user, and only a system user.
        foreach (var type in new[] { PenaltyUser.DataEntry, PenaltyUser.Technician })
        {
            FluentActions.Invoking(() => Make(type, null, null)).Should().Throw<DomainException>().WithMessage("*system user*");
            FluentActions.Invoking(() => Make(type, user, rep)).Should().Throw<DomainException>().WithMessage("*cannot also name a representative*");
            Make(type, user, null).PerformedByUserId.Should().Be(user);
        }
    }

    // ---- Deduction ----

    [Fact]
    public void A_deduction_period_is_both_or_neither_and_ordered()
    {
        var area = AreaId.New();
        var typed = Deduction.Create(area, D, DeductionReason.Transportation, 75m, null, null, null);
        typed.PeriodFrom.Should().BeNull();

        var suggested = Deduction.Create(area, D, DeductionReason.PercentageDeal, 1234.5m, "Sept", new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));
        suggested.PeriodFrom.Should().Be(new DateOnly(2026, 9, 1));

        FluentActions.Invoking(() => Deduction.Create(area, D, DeductionReason.Penalty, 1m, null, new DateOnly(2026, 9, 1), null))
            .Should().Throw<DomainException>().WithMessage("*both a start and an end*");
        FluentActions.Invoking(() => Deduction.Create(area, D, DeductionReason.Penalty, 1m, null, new DateOnly(2026, 9, 30), new DateOnly(2026, 9, 1)))
            .Should().Throw<DomainException>().WithMessage("*must not precede*");
        FluentActions.Invoking(() => Deduction.Create(area, D, DeductionReason.Penalty, -1m, null, null, null))
            .Should().Throw<DomainException>().WithMessage("*cannot be negative*");
    }

    // ---- Collection ----

    [Fact]
    public void A_single_collection_names_one_rep_and_a_group_at_least_two()
    {
        var lab = LaboratoryId.New(); var r1 = RepresentativeId.New(); var r2 = RepresentativeId.New();
        var single = Collection.Create(lab, D, CollectionType.Single, new[] { r1 }, 100m, 0m, null, "Ahmed", null);
        single.RepIds.Should().ContainSingle().Which.Should().Be(r1);

        var group = Collection.Create(lab, D, CollectionType.Group, new[] { r1, r2, r2 }, 100m, 0m, null, null, null);
        group.RepIds.Should().HaveCount(2, "reps are de-duplicated");

        FluentActions.Invoking(() => Collection.Create(lab, D, CollectionType.Single, new[] { r1, r2 }, 100m, 0m, null, null, null))
            .Should().Throw<DomainException>().WithMessage("*exactly one rep*");
        FluentActions.Invoking(() => Collection.Create(lab, D, CollectionType.Group, new[] { r1 }, 100m, 0m, null, null, null))
            .Should().Throw<DomainException>().WithMessage("*at least two reps*");
    }

    [Fact]
    public void A_collection_needs_an_amount_and_an_iban_exactly_when_there_is_a_bank_amount()
    {
        var lab = LaboratoryId.New(); var rep = new[] { RepresentativeId.New() };

        FluentActions.Invoking(() => Collection.Create(lab, D, CollectionType.Single, rep, 0m, 0m, null, null, null))
            .Should().Throw<DomainException>().WithMessage("*cash or bank amount*");
        FluentActions.Invoking(() => Collection.Create(lab, D, CollectionType.Single, rep, 0m, 500m, null, null, null))
            .Should().Throw<DomainException>().WithMessage("*requires the IBAN*");

        var bank = Collection.Create(lab, D, CollectionType.Single, rep, 200m, 500m, IbanOption.Iban16, null, null);
        bank.Iban.Should().BeSameAs(IbanOption.Iban16);
        bank.Total.Amount.Should().Be(700m);

        // Dropping the bank amount clears the IBAN so a stale account can never linger.
        bank.Update(D, CollectionType.Single, rep, 200m, 0m, IbanOption.Iban16, null, null);
        bank.Iban.Should().BeNull();
        bank.Total.Amount.Should().Be(200m);
    }

    // ---- Rep income ----

    [Fact]
    public void A_manual_rep_income_line_must_be_positive()
    {
        var e = RepIncomeEntry.Create(RepresentativeId.New(), D, 1500.255m, "cash handed over");
        e.Amount.Amount.Should().Be(1500.26m);
        FluentActions.Invoking(() => RepIncomeEntry.Create(RepresentativeId.New(), D, 0m, null))
            .Should().Throw<DomainException>().WithMessage("*greater than zero*");
    }
}
