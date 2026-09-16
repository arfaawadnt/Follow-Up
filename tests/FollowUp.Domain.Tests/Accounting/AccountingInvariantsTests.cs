using FluentAssertions;
using FollowUp.Domain.Accounting;
using FollowUp.Domain.Common;
using FollowUp.Domain.Identity;
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
        Enumeration.GetAll<PenaltyUser>().Select(e => e.Name).Should().BeEquivalentTo(new[] { "Rep", "DataEntry", "Technician", "LabRequest" });
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

    [Fact]
    public void A_collection_mirror_follows_the_collection_until_validated_and_reopens_when_the_cash_changes()
    {
        var tid = TreasuryId.New(); var rep = RepresentativeId.New();
        var c = Collection.Create(D, CollectionType.Single, new[] { (rep, 0m) }, 1000m, 250m, IbanOption.Iban16, "Ahmed", null);

        var e = TreasuryEntry.FromCollection(tid, c, new[] { "Rep One" });
        e.Origin.Should().BeSameAs(TreasuryEntryOrigin.AutoCollection);
        e.ValidationStatus.Should().BeSameAs(TreasuryValidationStatus.Pending);
        e.Debit.Amount.Should().Be(1000m, "the cash, never the bank part"); e.Credit.Amount.Should().Be(0m);
        e.ReasonId.Should().BeNull("the collection is the reason");
        e.CollectionId.Should().Be(c.Id); e.CollectedCash!.Value.Amount.Should().Be(1000m); e.Date.Should().Be(D);
        e.SystemNote.Should().Contain("Rep One").And.Contain("1000.00").And.Contain("bank 250.00").And.Contain("Ahmed");
        e.HasDiscrepancy.Should().BeFalse();

        FluentActions.Invoking(() => e.Update(D, 5m, 0m, TreasuryReasonId.New(), null)).Should().Throw<DomainException>().WithMessage("*validated or adjusted*");
        FluentActions.Invoking(() => e.AdjustValidated(900m, null)).Should().Throw<DomainException>().WithMessage("*Validate the entry before*");

        // While pending, the collection's cash drives the debit.
        c.Update(D.AddDays(1), CollectionType.Single, new[] { (rep, 0m) }, 1200m, 250m, IbanOption.Iban16, "Ahmed", null);
        e.RefreshFromCollection(c, new[] { "Rep One" });
        e.Debit.Amount.Should().Be(1200m); e.Date.Should().Be(D.AddDays(1));

        // The treasury confirms less than collected → validated with a discrepancy.
        e.Validate(1150m, "50 short", "cashier", new DateTimeOffset(2026, 9, 16, 10, 0, 0, TimeSpan.Zero));
        e.ValidationStatus.Should().BeSameAs(TreasuryValidationStatus.Validated);
        e.Debit.Amount.Should().Be(1150m); e.ValidatedBy.Should().Be("cashier"); e.ValidationNote.Should().Be("50 short");
        e.HasDiscrepancy.Should().BeTrue();
        FluentActions.Invoking(() => e.Validate(1m, null, "x", DateTimeOffset.UtcNow)).Should().Throw<DomainException>().WithMessage("*already validated*");

        // Post-validation correction keeps it validated; a collection edit that leaves the cash alone keeps it validated too.
        e.AdjustValidated(1160m, "recounted");
        e.Debit.Amount.Should().Be(1160m); e.Notes.Should().Be("recounted"); e.ValidationStatus.Should().BeSameAs(TreasuryValidationStatus.Validated);
        c.Update(D.AddDays(1), CollectionType.Single, new[] { (rep, 0m) }, 1200m, 300m, IbanOption.Iban16, "Ahmed", "notes only");
        e.RefreshFromCollection(c, new[] { "Rep One" });
        e.ValidationStatus.Should().BeSameAs(TreasuryValidationStatus.Validated, "the collected cash did not change");
        e.Debit.Amount.Should().Be(1160m, "the treasury's confirmed amount stands");

        // A cash change on the collection re-opens validation with the new cash.
        c.Update(D.AddDays(1), CollectionType.Single, new[] { (rep, 0m) }, 1500m, 300m, IbanOption.Iban16, "Ahmed", null);
        e.RefreshFromCollection(c, new[] { "Rep One" });
        e.ValidationStatus.Should().BeSameAs(TreasuryValidationStatus.Pending);
        e.Debit.Amount.Should().Be(1500m); e.ValidatedBy.Should().BeNull();
        e.Notes.Should().Be("recounted", "operator notes are never touched by the automation");

        var bankOnly = Collection.Create(D, CollectionType.Single, new[] { (rep, 0m) }, 0m, 500m, IbanOption.Iban12, null, null);
        FluentActions.Invoking(() => TreasuryEntry.FromCollection(tid, bankOnly, Array.Empty<string>())).Should().Throw<DomainException>().WithMessage("*cash amount*");
        var manual = TreasuryEntry.Create(tid, D, 10m, 0m, TreasuryReasonId.New(), null);
        manual.ValidationStatus.Should().BeSameAs(TreasuryValidationStatus.NotRequired);
        FluentActions.Invoking(() => manual.Validate(10m, null, "x", DateTimeOffset.UtcNow)).Should().Throw<DomainException>();
    }

    [Fact]
    public void A_treasury_grant_normalises_its_rights()
    {
        var g = TreasuryGrant.Create(RoleId.New(), TreasuryId.New(), view: false, validate: true, update: false);
        g.CanView.Should().BeTrue("Validate needs the page"); g.CanValidate.Should().BeTrue(); g.CanUpdate.Should().BeFalse();
        g.Set(false, false, false);
        g.IsEmpty.Should().BeTrue();
        g.Set(false, false, true);
        g.CanView.Should().BeTrue(); g.CanUpdate.Should().BeTrue();
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

        // LabRequest → nobody: the lab asked for the wrong test.
        var labRequest = Make(PenaltyUser.LabRequest, null, null);
        labRequest.PerformedByRepId.Should().BeNull(); labRequest.PerformedByUserId.Should().BeNull();
        FluentActions.Invoking(() => Make(PenaltyUser.LabRequest, user, null)).Should().Throw<DomainException>().WithMessage("*names no person*");
        FluentActions.Invoking(() => Make(PenaltyUser.LabRequest, null, rep)).Should().Throw<DomainException>().WithMessage("*names no person*");
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

    [Fact]
    public void An_auto_penalty_deduction_mirrors_its_penalty_floors_under_charges_and_resyncs_on_refresh()
    {
        var area = AreaId.New();
        var rep = FollowUp.Domain.Representatives.RepresentativeId.New();
        var penalty = PenaltyRecord.Create(LaboratoryId.New(), D, "ACC-7", "Patient", "T1", "Wrong test", 300m, "T2", "Right test", 120m, PenaltyUser.Rep, null, rep);

        var d = Deduction.FromPenalty(area, penalty, "Alpha Lab");
        d.Origin.Should().BeSameAs(DeductionOrigin.AutoPenalty);
        d.Reason.Should().BeSameAs(DeductionReason.Penalty);
        d.PenaltyRecordId.Should().Be(penalty.Id);
        d.Date.Should().Be(D); d.PeriodFrom.Should().Be(D); d.PeriodTo.Should().Be(D);
        d.Value.Amount.Should().Be(180m, "wrong − right");
        d.SystemNote.Should().Contain("Alpha Lab").And.Contain("ACC-7").And.Contain("Patient").And.Contain("Wrong test").And.Contain("Right test");
        d.IsAdjusted.Should().BeFalse();

        // An operator tweaks the value and writes a note; the penalty then changes to an under-charge.
        d.Adjust(150m, "checked with the lab", null);
        d.IsAdjusted.Should().BeTrue();
        penalty.Update(D.AddDays(1), "ACC-7", "Patient", "T1", "Wrong", 100m, "T2", "Right", 250m, PenaltyUser.Rep, null, rep);
        d.RefreshFromPenalty(penalty, "Alpha Lab");
        d.Value.Amount.Should().Be(0m, "an under-charge is not deducted from the area");
        d.SystemNote.Should().Contain("-150.00").And.Contain("under-charge");
        d.Date.Should().Be(D.AddDays(1));
        d.IsAdjusted.Should().BeFalse("the penalty is the source of truth; its change supersedes the adjustment");
        d.Notes.Should().Be("checked with the lab", "operator notes are never touched by the automation");

        var other = PenaltyRecord.Create(LaboratoryId.New(), D, "X", "P", "T1", "W", 1m, "T2", "R", 1m, PenaltyUser.Rep, null, rep);
        FluentActions.Invoking(() => d.RefreshFromPenalty(other, "Lab")).Should().Throw<DomainException>().WithMessage("*does not mirror*");
        FluentActions.Invoking(() => d.Update(D, DeductionReason.Penalty, 1m, null, null, null)).Should().Throw<DomainException>().WithMessage("*only its value and notes*");
    }

    [Fact]
    public void An_auto_deal_deduction_is_recalculated_until_an_operator_adjusts_it()
    {
        var area = AreaId.New();
        var month = new YearMonth(2026, 9);
        var d = Deduction.AutoDeal(area, month, new DateOnly(2026, 9, 10), 250m, "income 2,500 × 10%");
        d.Origin.Should().BeSameAs(DeductionOrigin.AutoDeal);
        d.Reason.Should().BeSameAs(DeductionReason.PercentageDeal);
        d.Date.Should().Be(new DateOnly(2026, 9, 1), "dated on the 1st so the month's filter finds it");
        d.PeriodFrom.Should().Be(new DateOnly(2026, 9, 1)); d.PeriodTo.Should().Be(new DateOnly(2026, 9, 10));
        d.SystemNote.Should().Be("income 2,500 × 10%");

        d.Recalculate(new DateOnly(2026, 9, 11), 275m, "income 2,750 × 10%");
        d.Value.Amount.Should().Be(275m); d.PeriodTo.Should().Be(new DateOnly(2026, 9, 11));
        FluentActions.Invoking(() => d.Recalculate(new DateOnly(2026, 8, 31), 1m, "x")).Should().Throw<DomainException>().WithMessage("*precedes*");

        // Notes-only edit is not an adjustment; a value change is, and it stops the recalculation.
        d.Adjust(275m, "agreed with area manager", null);
        d.IsAdjusted.Should().BeFalse("same value, only a note");
        d.Adjust(300m, "agreed with area manager", "Suggested: income 3,000 × 10%");
        d.IsAdjusted.Should().BeTrue();
        d.SystemNote.Should().Be("Suggested: income 3,000 × 10%", "the Suggest-value basis replaces the system note");
        d.Notes.Should().Be("agreed with area manager");
        FluentActions.Invoking(() => d.Recalculate(new DateOnly(2026, 9, 12), 1m, "x")).Should().Throw<DomainException>().WithMessage("*manually adjusted*");

        var manual = Deduction.Create(area, D, DeductionReason.Transportation, 5m, null, null, null);
        manual.Origin.Should().BeSameAs(DeductionOrigin.Manual);
        FluentActions.Invoking(() => manual.Adjust(6m, null, null)).Should().Throw<DomainException>().WithMessage("*Use Update*");
        FluentActions.Invoking(() => manual.Recalculate(D, 1m, "x")).Should().Throw<DomainException>();
    }

    // ---- Collection ----

    [Fact]
    public void A_single_collection_names_one_rep_who_takes_the_total_and_a_group_splits_it_exactly()
    {
        var r1 = RepresentativeId.New(); var r2 = RepresentativeId.New();
        var single = Collection.Create(D, CollectionType.Single, new[] { (r1, 0m) }, 100m, 0m, null, "Ahmed", null);
        single.RepIds.Should().ContainSingle().Which.Should().Be(r1);
        single.ShareOf(r1).Amount.Should().Be(100m, "a single rep takes the whole amount whatever was passed");
        single.ShareOf(r2).Amount.Should().Be(0m, "a rep who is not on the collection has no share");

        var group = Collection.Create(D, CollectionType.Group, new[] { (r1, 60m), (r2, 40m) }, 70m, 30m, IbanOption.Iban12, null, null);
        group.Shares.Select(s => (s.RepId, s.Amount.Amount)).Should().Equal((r1, 60m), (r2, 40m));
        group.RepIds.Should().Equal(r1, r2);

        FluentActions.Invoking(() => Collection.Create(D, CollectionType.Single, new[] { (r1, 50m), (r2, 50m) }, 100m, 0m, null, null, null))
            .Should().Throw<DomainException>().WithMessage("*exactly one rep*");
        FluentActions.Invoking(() => Collection.Create(D, CollectionType.Group, new[] { (r1, 100m) }, 100m, 0m, null, null, null))
            .Should().Throw<DomainException>().WithMessage("*at least two reps*");
        FluentActions.Invoking(() => Collection.Create(D, CollectionType.Group, new[] { (r1, 60m), (r2, 50m) }, 100m, 0m, null, null, null))
            .Should().Throw<DomainException>().WithMessage("*must add up to cash + bank*");
        FluentActions.Invoking(() => Collection.Create(D, CollectionType.Group, new[] { (r1, 100m), (r2, 0m) }, 100m, 0m, null, null, null))
            .Should().Throw<DomainException>().WithMessage("*greater than zero*");
        FluentActions.Invoking(() => Collection.Create(D, CollectionType.Group, new[] { (r1, 50m), (r1, 50m) }, 100m, 0m, null, null, null))
            .Should().Throw<DomainException>().WithMessage("*appears once*");
    }

    [Fact]
    public void A_collection_needs_an_amount_and_an_iban_exactly_when_there_is_a_bank_amount()
    {
        var rep = new[] { (RepresentativeId.New(), 0m) };

        FluentActions.Invoking(() => Collection.Create(D, CollectionType.Single, rep, 0m, 0m, null, null, null))
            .Should().Throw<DomainException>().WithMessage("*cash or bank amount*");
        FluentActions.Invoking(() => Collection.Create(D, CollectionType.Single, rep, 0m, 500m, null, null, null))
            .Should().Throw<DomainException>().WithMessage("*requires the IBAN*");

        var bank = Collection.Create(D, CollectionType.Single, rep, 200m, 500m, IbanOption.Iban16, null, null);
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

    [Fact]
    public void A_real_income_sheet_line_keeps_paid_within_total_required_and_derives_the_remaining()
    {
        var rep = RepresentativeId.New(); var lab = LaboratoryId.New();
        var line = RepLabIncome.Create(rep, lab, D, 12, 1000m, 700m, 150m, "  partly paid ");
        line.Remaining.Amount.Should().Be(300m);
        line.Notes.Should().Be("partly paid");
        line.IsEmpty.Should().BeFalse();

        FluentActions.Invoking(() => line.Update(-1, 1000m, 0m, 0m, null)).Should().Throw<DomainException>().WithMessage("*Samples*");
        FluentActions.Invoking(() => line.Update(1, -5m, 0m, 0m, null)).Should().Throw<DomainException>().WithMessage("*Total required*");
        FluentActions.Invoking(() => line.Update(1, 100m, 120m, 0m, null)).Should().Throw<DomainException>().WithMessage("*cannot exceed*");
        FluentActions.Invoking(() => line.Update(1, 100m, 50m, -1m, null)).Should().Throw<DomainException>().WithMessage("*Delayed payment*");
        line.Remaining.Amount.Should().Be(300m, "a refused update leaves the line untouched");

        line.Update(0, 0m, 0m, 0m, "   ");
        line.IsEmpty.Should().BeTrue("all zero and no notes — the sheet drops such a line");
    }
}
