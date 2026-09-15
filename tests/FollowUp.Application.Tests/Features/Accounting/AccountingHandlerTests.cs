using FluentAssertions;
using FollowUp.Application.Common.Exceptions;
using FollowUp.Application.Features.Accounting;
using FollowUp.Application.Tests.Common;
using FollowUp.Domain.Accounting;
using FollowUp.Domain.Common;
using FollowUp.Domain.Identity;
using FollowUp.Domain.Laboratories;
using FollowUp.Domain.Reference;
using FollowUp.Domain.Representatives;

namespace FollowUp.Application.Tests.Features.Accounting;

/// <summary>
/// Accounting handlers and validators: records are stored through the domain factories, references are resolved and
/// scope-checked, inactive configuration is refused, and the validators reject the shapes the domain forbids before a
/// handler ever runs.
/// </summary>
public class AccountingHandlerTests
{
    private static readonly DateOnly D = new(2026, 9, 13);
    private static Representative Rep(string name = "Collector") => Representative.Register(name, RepresentativeType.Collector, GoalDuration.Monthly, Money.Zero, Money.Zero);
    private static Laboratory Lab() => Laboratory.Register(LabCode.Create($"MGL-{Random.Shared.Next(1000, 9999)}"), "Lab", "B");

    // ---- Treasury ----

    [Fact]
    public async Task Create_treasury_entry_stores_a_one_sided_movement_against_an_active_treasury_and_reason()
    {
        var treasuries = new FakeTreasuryRepository(); var t = Treasury.Create("Main", new[] { "Cairo" }); treasuries.Store.Add(t);
        var reasons = new FakeTreasuryReasonRepository(); var reason = TreasuryReason.Create("Fuel"); reasons.Store.Add(reason);
        var entries = new FakeTreasuryEntryRepository();
        var handler = new CreateTreasuryEntryHandler(entries, treasuries, reasons, new FakeCurrentUser());

        var id = await handler.Handle(new CreateTreasuryEntryCommand(t.Id.Value, D, 0m, 250m, reason.Id.Value, "diesel"), CancellationToken.None);

        var e = entries.Store.Single(x => x.Id.Value == id);
        e.Credit.Amount.Should().Be(250m); e.Debit.Amount.Should().Be(0m);
        e.TreasuryId.Should().Be(t.Id); e.ReasonId.Should().Be(reason.Id);
    }

    [Fact]
    public async Task Create_treasury_entry_refuses_an_inactive_treasury_or_reason()
    {
        var treasuries = new FakeTreasuryRepository(); var t = Treasury.Create("Main", new[] { "Cairo" }); t.Activate(false); treasuries.Store.Add(t);
        var reasons = new FakeTreasuryReasonRepository(); var reason = TreasuryReason.Create("Fuel"); reasons.Store.Add(reason);
        var handler = new CreateTreasuryEntryHandler(new FakeTreasuryEntryRepository(), treasuries, reasons, new FakeCurrentUser());

        await FluentActions.Awaiting(() => handler.Handle(new CreateTreasuryEntryCommand(t.Id.Value, D, 100m, 0m, reason.Id.Value, null), CancellationToken.None))
            .Should().ThrowAsync<ConflictException>().WithMessage("*treasury is inactive*");
    }

    [Fact]
    public void Treasury_entry_validator_requires_exactly_one_side()
    {
        var v = new CreateTreasuryEntryValidator();
        v.Validate(new CreateTreasuryEntryCommand(Guid.NewGuid(), D, 10m, 10m, Guid.NewGuid(), null)).IsValid.Should().BeFalse("both sides");
        v.Validate(new CreateTreasuryEntryCommand(Guid.NewGuid(), D, 0m, 0m, Guid.NewGuid(), null)).IsValid.Should().BeFalse("neither side");
        v.Validate(new CreateTreasuryEntryCommand(Guid.NewGuid(), D, 10m, 0m, Guid.NewGuid(), null)).IsValid.Should().BeTrue();
    }

    // ---- Penalty ----

    private static AppUser SystemUser(bool active = true)
    {
        var u = AppUser.Create("clerk", new FakePasswordHasher().Hash("pw12345678"), RoleId.New());
        if (!active) u.Deactivate();
        return u;
    }
    private static CreatePenaltyCommand Penalty(Guid labId, string userType, Guid? performedByUserId, Guid? performedByRepId) =>
        new(D, labId, "ACC-9", "Patient", "T1", "Wrong", 300m, "T2", "Right", 120m, userType, performedByUserId, performedByRepId);

    [Fact]
    public async Task Create_penalty_stores_the_record_against_an_in_scope_lab_attributed_to_a_system_user()
    {
        var labs = new FakeLaboratoryRepository(); var lab = Lab(); labs.Store.Add(lab);
        var users = new FakeAppUserRepository(); var clerk = SystemUser(); users.Store.Add(clerk);
        var repo = new FakePenaltyRecordRepository();
        var handler = new CreatePenaltyHandler(repo, labs, new FakeRepresentativeRepository(), users, new FakeDeductionRepository(), new FakeAreaRepository(), new FakeCurrentUser());

        var id = await handler.Handle(Penalty(lab.Id.Value, "DataEntry", clerk.Id.Value, null), CancellationToken.None);

        var p = repo.Store.Single(x => x.Id.Value == id);
        p.PenaltyAmount.Amount.Should().Be(180m);
        p.UserType.Should().BeSameAs(PenaltyUser.DataEntry);
        p.PerformedByUserId.Should().Be(clerk.Id);
        p.PerformedByRepId.Should().BeNull();
    }

    [Fact]
    public async Task Create_penalty_attributed_to_a_rep_requires_the_rep_to_exist_and_be_in_scope()
    {
        var labs = new FakeLaboratoryRepository(); var lab = Lab(); lab.PlaceInHierarchy(null, "Giza", null, null); labs.Store.Add(lab);
        var reps = new FakeRepresentativeRepository(); var rep = Rep(); rep.AssignScope(null, "Cairo"); reps.Store.Add(rep);
        var repo = new FakePenaltyRecordRepository();

        // Unknown representative → 404.
        await FluentActions.Awaiting(() => new CreatePenaltyHandler(repo, labs, reps, new FakeAppUserRepository(), new FakeDeductionRepository(), new FakeAreaRepository(), new FakeCurrentUser())
                .Handle(Penalty(lab.Id.Value, "Rep", null, Guid.NewGuid()), CancellationToken.None))
            .Should().ThrowAsync<NotFoundException>();

        // The lab is in the Giza-scoped caller's scope but the rep (Cairo) is not → 403 by the record-scope rule, nothing stored.
        var giza = new FakeCurrentUser { Scope = OrgScope.Create(new[] { "*" }, new[] { "Giza" }, new[] { "*" }, new[] { "*" }, new[] { "*" }, new[] { "*" }) };
        await FluentActions.Awaiting(() => new CreatePenaltyHandler(repo, labs, reps, new FakeAppUserRepository(), new FakeDeductionRepository(), new FakeAreaRepository(), giza)
                .Handle(Penalty(lab.Id.Value, "Rep", null, rep.Id.Value), CancellationToken.None))
            .Should().ThrowAsync<ForbiddenException>();
        repo.Store.Should().BeEmpty();

        // Global caller → stored with the representative link and no user link.
        var id = await new CreatePenaltyHandler(repo, labs, reps, new FakeAppUserRepository(), new FakeDeductionRepository(), new FakeAreaRepository(), new FakeCurrentUser())
            .Handle(Penalty(lab.Id.Value, "Rep", null, rep.Id.Value), CancellationToken.None);
        var stored = repo.Store.Single(x => x.Id.Value == id);
        stored.UserType.Should().BeSameAs(PenaltyUser.Rep);
        stored.PerformedByRepId.Should().Be(rep.Id);
        stored.PerformedByUserId.Should().BeNull();
    }

    [Fact]
    public async Task Create_penalty_attributed_to_a_system_user_requires_an_existing_active_user()
    {
        var labs = new FakeLaboratoryRepository(); var lab = Lab(); labs.Store.Add(lab);
        var users = new FakeAppUserRepository(); var inactive = SystemUser(active: false); users.Store.Add(inactive);
        var handler = new CreatePenaltyHandler(new FakePenaltyRecordRepository(), labs, new FakeRepresentativeRepository(), users, new FakeDeductionRepository(), new FakeAreaRepository(), new FakeCurrentUser());

        await FluentActions.Awaiting(() => handler.Handle(Penalty(lab.Id.Value, "Technician", Guid.NewGuid(), null), CancellationToken.None))
            .Should().ThrowAsync<NotFoundException>("an unknown user id");
        await FluentActions.Awaiting(() => handler.Handle(Penalty(lab.Id.Value, "Technician", inactive.Id.Value, null), CancellationToken.None))
            .Should().ThrowAsync<FollowUp.Application.Common.Exceptions.ValidationException>("a deactivated user cannot be blamed for new errors");
    }

    [Fact]
    public async Task Create_penalty_for_an_unknown_lab_is_not_found()
    {
        var handler = new CreatePenaltyHandler(new FakePenaltyRecordRepository(), new FakeLaboratoryRepository(), new FakeRepresentativeRepository(), new FakeAppUserRepository(), new FakeDeductionRepository(), new FakeAreaRepository(), new FakeCurrentUser());
        await FluentActions.Awaiting(() => handler.Handle(Penalty(Guid.NewGuid(), "Rep", null, Guid.NewGuid()), CancellationToken.None))
            .Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public void Penalty_validator_requires_exactly_the_person_matching_the_user_type()
    {
        var v = new CreatePenaltyValidator();
        var lab = Guid.NewGuid(); var someone = Guid.NewGuid();

        v.Validate(Penalty(lab, "Janitor", someone, null)).Errors.Should().Contain(e => e.PropertyName == nameof(CreatePenaltyCommand.UserType));
        v.Validate(Penalty(lab, "Rep", null, null)).Errors.Should().Contain(e => e.PropertyName == nameof(CreatePenaltyCommand.PerformedByRepId), "Rep needs a representative");
        v.Validate(Penalty(lab, "Rep", someone, someone)).Errors.Should().Contain(e => e.PropertyName == nameof(CreatePenaltyCommand.PerformedByUserId), "Rep cannot also name a user");
        v.Validate(Penalty(lab, "DataEntry", null, null)).Errors.Should().Contain(e => e.PropertyName == nameof(CreatePenaltyCommand.PerformedByUserId), "DataEntry needs a system user");
        v.Validate(Penalty(lab, "Technician", someone, someone)).Errors.Should().Contain(e => e.PropertyName == nameof(CreatePenaltyCommand.PerformedByRepId), "Technician cannot also name a rep");

        v.Validate(Penalty(lab, "Rep", null, someone)).IsValid.Should().BeTrue();
        v.Validate(Penalty(lab, "DataEntry", someone, null)).IsValid.Should().BeTrue();
        v.Validate(Penalty(lab, "Technician", someone, null)).IsValid.Should().BeTrue();
    }

    // ---- Deduction ----

    [Fact]
    public async Task Create_deduction_stores_the_reason_value_and_period_against_an_in_scope_area()
    {
        var areas = new FakeAreaRepository(); var area = Area.Create("Nasr City", CityId.New(), false); areas.Store.Add(area);
        var repo = new FakeDeductionRepository();
        var handler = new CreateDeductionHandler(repo, areas, new FakeCurrentUser());

        var id = await handler.Handle(new CreateDeductionCommand(D, area.Id.Value, "PercentageDeal", 987.65m, "Sept deal",
            new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30)), CancellationToken.None);

        var d = repo.Store.Single(x => x.Id.Value == id);
        d.Reason.Should().BeSameAs(DeductionReason.PercentageDeal);
        d.Value.Amount.Should().Be(987.65m);
        d.PeriodTo.Should().Be(new DateOnly(2026, 9, 30));
    }

    [Fact]
    public void Suggest_deduction_validator_only_allows_the_computed_reasons()
    {
        var v = new SuggestDeductionValidator();
        v.Validate(new SuggestDeductionQuery(Guid.NewGuid(), "Transportation", D, D)).IsValid.Should().BeFalse("typed manually, nothing to suggest");
        v.Validate(new SuggestDeductionQuery(Guid.NewGuid(), "Penalty", D, D)).IsValid.Should().BeTrue();
        v.Validate(new SuggestDeductionQuery(Guid.NewGuid(), "PercentageDeal", D, D.AddDays(-1))).IsValid.Should().BeFalse("end before start");
    }

    // ---- Penalty → deduction automation ----

    private static (FakeLaboratoryRepository labs, Laboratory lab, FakeAreaRepository areas, Area area, FakeRepresentativeRepository reps, Representative rep) PlacedLab()
    {
        var areas = new FakeAreaRepository(); var area = Area.Create("Nasr City", CityId.New(), false); areas.Store.Add(area);
        var labs = new FakeLaboratoryRepository(); var lab = Lab(); lab.PlaceInHierarchy(null, "Cairo", "Cairo", area.Name); labs.Store.Add(lab);
        var reps = new FakeRepresentativeRepository(); var rep = Rep(); reps.Store.Add(rep);
        return (labs, lab, areas, area, reps, rep);
    }

    [Fact]
    public async Task Recording_a_penalty_mirrors_it_as_an_auto_deduction_on_the_labs_area_and_keeps_it_in_step()
    {
        var (labs, lab, areas, area, reps, rep) = PlacedLab();
        var penalties = new FakePenaltyRecordRepository(); var deductions = new FakeDeductionRepository();
        var users = new FakeAppUserRepository(); var me = new FakeCurrentUser();

        var id = await new CreatePenaltyHandler(penalties, labs, reps, users, deductions, areas, me)
            .Handle(Penalty(lab.Id.Value, "Rep", null, rep.Id.Value), CancellationToken.None);

        var d = deductions.Store.Should().ContainSingle().Subject;
        d.Origin.Should().BeSameAs(DeductionOrigin.AutoPenalty);
        d.AreaId.Should().Be(area.Id, "the lab's area, resolved by name");
        d.PenaltyRecordId!.Value.Value.Should().Be(id);
        d.Value.Amount.Should().Be(180m);
        d.SystemNote.Should().Contain("ACC-9");

        // Editing the penalty refreshes the mirror (value + details) instead of creating a second one.
        await new UpdatePenaltyHandler(penalties, labs, reps, users, deductions, areas, me)
            .Handle(new UpdatePenaltyCommand(id, D, "ACC-9", "Patient", "T1", "Wrong", 400m, "T2", "Right", 120m, "Rep", null, rep.Id.Value), CancellationToken.None);
        deductions.Store.Should().ContainSingle().Which.Value.Amount.Should().Be(280m);

        // Deleting the penalty removes its mirror.
        await new DeletePenaltyHandler(penalties, labs, deductions, me).Handle(new DeletePenaltyCommand(id), CancellationToken.None);
        deductions.Store.Should().BeEmpty();
        penalties.Store.Should().BeEmpty();
    }

    [Fact]
    public async Task A_penalty_on_a_lab_without_a_resolvable_area_is_recorded_but_gets_no_deduction()
    {
        var labs = new FakeLaboratoryRepository(); var lab = Lab(); labs.Store.Add(lab); // no area
        var reps = new FakeRepresentativeRepository(); var rep = Rep(); reps.Store.Add(rep);
        var penalties = new FakePenaltyRecordRepository(); var deductions = new FakeDeductionRepository();

        await new CreatePenaltyHandler(penalties, labs, reps, new FakeAppUserRepository(), deductions, new FakeAreaRepository(), new FakeCurrentUser())
            .Handle(Penalty(lab.Id.Value, "Rep", null, rep.Id.Value), CancellationToken.None);

        penalties.Store.Should().ContainSingle("the penalty itself is never blocked");
        deductions.Store.Should().BeEmpty("nothing to attribute it to; the daily automation links it once the lab is placed");
    }

    [Fact]
    public async Task Editing_an_automated_deduction_adjusts_value_and_notes_only_and_deleting_a_penalty_mirror_is_refused()
    {
        var (_, _, areas, area, _, rep) = PlacedLab();
        var penalty = PenaltyRecord.Create(LaboratoryId.New(), D, "A", "P", "T1", "W", 300m, "T2", "R", 120m, PenaltyUser.Rep, null, rep.Id);
        var deductions = new FakeDeductionRepository();
        var mirror = Deduction.FromPenalty(area.Id, penalty, "Lab"); deductions.Store.Add(mirror);
        var deal = Deduction.AutoDeal(area.Id, new YearMonth(2026, 9), new DateOnly(2026, 9, 12), 250m, "income 2,500 × 10%"); deductions.Store.Add(deal);
        var me = new FakeCurrentUser();

        // An automated row: the area / reason / period sent by the client are ignored, value + notes (+ basis) apply.
        await new UpdateDeductionHandler(deductions, areas, me).Handle(
            new UpdateDeductionCommand(deal.Id.Value, D, "Transportation", 300m, "agreed", new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), "Suggested: income 3,000 × 10%"), CancellationToken.None);
        deal.Reason.Should().BeSameAs(DeductionReason.PercentageDeal);
        deal.PeriodFrom.Should().Be(new DateOnly(2026, 9, 1));
        deal.Value.Amount.Should().Be(300m); deal.Notes.Should().Be("agreed"); deal.IsAdjusted.Should().BeTrue();
        deal.SystemNote.Should().Be("Suggested: income 3,000 × 10%");

        // A penalty mirror cannot be deleted from the Deductions page — the penalty owns it.
        await FluentActions.Awaiting(() => new DeleteDeductionHandler(deductions, areas, me).Handle(new DeleteDeductionCommand(mirror.Id.Value), CancellationToken.None))
            .Should().ThrowAsync<ConflictException>().WithMessage("*Penalty Statement*");
        deductions.Store.Should().Contain(mirror);

        // An automated deal row may be deleted (the automation recreates it for a running month).
        await new DeleteDeductionHandler(deductions, areas, me).Handle(new DeleteDeductionCommand(deal.Id.Value), CancellationToken.None);
        deductions.Store.Should().NotContain(deal);
    }

    // ---- Collection ----

    [Fact]
    public async Task Create_collection_resolves_every_rep_and_stores_the_bank_iban()
    {
        var labs = new FakeLaboratoryRepository(); var lab = Lab(); labs.Store.Add(lab);
        var reps = new FakeRepresentativeRepository(); var r1 = Rep("A"); var r2 = Rep("B"); reps.Store.Add(r1); reps.Store.Add(r2);
        var repo = new FakeCollectionRepository();
        var handler = new CreateCollectionHandler(repo, labs, reps, new FakeCurrentUser());

        var id = await handler.Handle(new CreateCollectionCommand(D, lab.Id.Value, "Group", new[] { r1.Id.Value, r2.Id.Value },
            1000m, 2500m, "18", "Cashier", null), CancellationToken.None);

        var c = repo.Store.Single(x => x.Id.Value == id);
        c.Type.Should().BeSameAs(CollectionType.Group);
        c.RepIds.Should().BeEquivalentTo(new[] { r1.Id, r2.Id });
        c.Iban.Should().BeSameAs(IbanOption.Iban18);
        c.Total.Amount.Should().Be(3500m);
    }

    [Fact]
    public async Task Create_collection_with_an_unknown_rep_is_not_found_and_stores_nothing()
    {
        var labs = new FakeLaboratoryRepository(); var lab = Lab(); labs.Store.Add(lab);
        var repo = new FakeCollectionRepository();
        var handler = new CreateCollectionHandler(repo, labs, new FakeRepresentativeRepository(), new FakeCurrentUser());

        await FluentActions.Awaiting(() => handler.Handle(new CreateCollectionCommand(D, lab.Id.Value, "Single", new[] { Guid.NewGuid() }, 100m, 0m, null, null, null), CancellationToken.None))
            .Should().ThrowAsync<NotFoundException>();
        repo.Store.Should().BeEmpty();
    }

    [Fact]
    public void Collection_validator_enforces_rep_count_amount_and_iban_rules()
    {
        var v = new CreateCollectionValidator();
        var lab = Guid.NewGuid(); var r1 = Guid.NewGuid(); var r2 = Guid.NewGuid();
        v.Validate(new CreateCollectionCommand(D, lab, "Single", new[] { r1, r2 }, 100m, 0m, null, null, null)).IsValid.Should().BeFalse("single with two reps");
        v.Validate(new CreateCollectionCommand(D, lab, "Group", new[] { r1 }, 100m, 0m, null, null, null)).IsValid.Should().BeFalse("group with one rep");
        v.Validate(new CreateCollectionCommand(D, lab, "Single", new[] { r1 }, 0m, 0m, null, null, null)).IsValid.Should().BeFalse("no amount");
        v.Validate(new CreateCollectionCommand(D, lab, "Single", new[] { r1 }, 0m, 500m, null, null, null)).IsValid.Should().BeFalse("bank without IBAN");
        v.Validate(new CreateCollectionCommand(D, lab, "Single", new[] { r1 }, 0m, 500m, "99", null, null)).IsValid.Should().BeFalse("IBAN outside 12/16/18");
        v.Validate(new CreateCollectionCommand(D, lab, "Single", new[] { r1 }, 0m, 500m, "16", null, null)).IsValid.Should().BeTrue();
        v.Validate(new CreateCollectionCommand(D, lab, "Single", new[] { r1 }, 100m, 0m, null, null, null)).IsValid.Should().BeTrue("cash needs no IBAN");
    }

    // ---- Rep income ----

    [Fact]
    public async Task Create_rep_income_entry_stores_a_positive_amount_for_an_in_scope_rep()
    {
        var reps = new FakeRepresentativeRepository(); var rep = Rep(); reps.Store.Add(rep);
        var repo = new FakeRepIncomeEntryRepository();
        var handler = new CreateRepIncomeEntryHandler(repo, reps, new FakeCurrentUser());

        var id = await handler.Handle(new CreateRepIncomeEntryCommand(D, rep.Id.Value, 1500m, "cash"), CancellationToken.None);

        repo.Store.Single(x => x.Id.Value == id).Amount.Amount.Should().Be(1500m);
        new CreateRepIncomeEntryValidator().Validate(new CreateRepIncomeEntryCommand(D, rep.Id.Value, 0m, null)).IsValid.Should().BeFalse("must be positive");
    }
}
