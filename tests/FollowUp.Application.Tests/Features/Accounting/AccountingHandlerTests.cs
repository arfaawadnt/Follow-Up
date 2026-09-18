using FollowUp.Application.Common.Abstractions;
using FluentAssertions;
using FollowUp.Application.Common.Exceptions;
using FollowUp.Application.Common.Security;
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
        var handler = new CreateTreasuryEntryHandler(entries, treasuries, reasons, new FakeCurrentUser(), new FakeTreasuryAccess());

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
        var handler = new CreateTreasuryEntryHandler(new FakeTreasuryEntryRepository(), treasuries, reasons, new FakeCurrentUser(), new FakeTreasuryAccess());

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
        var handler = new CreatePenaltyHandler(repo, labs, new FakeRepresentativeRepository(), users, new FakeCurrentUser());

        var id = await handler.Handle(Penalty(lab.Id.Value, "DataEntry", clerk.Id.Value, null), CancellationToken.None);

        var p = repo.Store.Single(x => x.Id.Value == id);
        p.PenaltyAmount.Amount.Should().Be(-180m, "right 120 − wrong 300: the statement debits the right test and credits the wrong one");
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
        await FluentActions.Awaiting(() => new CreatePenaltyHandler(repo, labs, reps, new FakeAppUserRepository(), new FakeCurrentUser())
                .Handle(Penalty(lab.Id.Value, "Rep", null, Guid.NewGuid()), CancellationToken.None))
            .Should().ThrowAsync<NotFoundException>();

        // The lab is in the Giza-scoped caller's scope but the rep (Cairo) is not → 403 by the record-scope rule, nothing stored.
        var giza = new FakeCurrentUser { Scope = OrgScope.Create(new[] { "*" }, new[] { "Giza" }, new[] { "*" }, new[] { "*" }, new[] { "*" }, new[] { "*" }) };
        await FluentActions.Awaiting(() => new CreatePenaltyHandler(repo, labs, reps, new FakeAppUserRepository(), giza)
                .Handle(Penalty(lab.Id.Value, "Rep", null, rep.Id.Value), CancellationToken.None))
            .Should().ThrowAsync<ForbiddenException>();
        repo.Store.Should().BeEmpty();

        // Global caller → stored with the representative link and no user link.
        var id = await new CreatePenaltyHandler(repo, labs, reps, new FakeAppUserRepository(), new FakeCurrentUser())
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
        var handler = new CreatePenaltyHandler(new FakePenaltyRecordRepository(), labs, new FakeRepresentativeRepository(), users, new FakeCurrentUser());

        await FluentActions.Awaiting(() => handler.Handle(Penalty(lab.Id.Value, "Technician", Guid.NewGuid(), null), CancellationToken.None))
            .Should().ThrowAsync<NotFoundException>("an unknown user id");
        await FluentActions.Awaiting(() => handler.Handle(Penalty(lab.Id.Value, "Technician", inactive.Id.Value, null), CancellationToken.None))
            .Should().ThrowAsync<FollowUp.Application.Common.Exceptions.ValidationException>("a deactivated user cannot be blamed for new errors");
    }

    [Fact]
    public async Task Create_penalty_for_an_unknown_lab_is_not_found()
    {
        var handler = new CreatePenaltyHandler(new FakePenaltyRecordRepository(), new FakeLaboratoryRepository(), new FakeRepresentativeRepository(), new FakeAppUserRepository(), new FakeCurrentUser());
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
        v.Validate(Penalty(lab, "LabRequest", null, null)).IsValid.Should().BeTrue("a lab request names nobody");
        v.Validate(new CreatePenaltyCommand(D, lab, "A", "P", "T1", "Wrong", 300m, null, null, 0m, "LabRequest", null, null)).IsValid.Should().BeTrue("a lab request may name one test only");
        v.Validate(new CreatePenaltyCommand(D, lab, "A", "P", null, null, 0m, null, null, 0m, "LabRequest", null, null)).IsValid.Should().BeFalse("a lab request needs at least one test");
        v.Validate(new CreatePenaltyCommand(D, lab, "A", "P", "T1", "Wrong", 300m, null, null, 0m, "DataEntry", someone, null)).IsValid.Should().BeFalse("a staff penalty needs both tests");
        v.Validate(new CreatePenaltyCommand(D, lab, "A", "P", "T1", null, 300m, "T2", "Right", 100m, "DataEntry", someone, null)).IsValid.Should().BeFalse("a test code needs its name");
        v.Validate(Penalty(lab, "LabRequest", someone, null)).Errors.Should().Contain(e => e.PropertyName == nameof(CreatePenaltyCommand.PerformedByUserId), "a lab request names no user");
        v.Validate(Penalty(lab, "LabRequest", null, someone)).Errors.Should().Contain(e => e.PropertyName == nameof(CreatePenaltyCommand.PerformedByRepId), "a lab request names no rep");
    }

    [Fact]
    public async Task A_lab_request_penalty_is_recorded_without_a_person_and_can_be_retyped_both_ways()
    {
        var labs = new FakeLaboratoryRepository(); var lab = Lab(); lab.PlaceInHierarchy(null, "Giza", null, "Nasr City"); labs.Store.Add(lab);
        var users = new FakeAppUserRepository(); var clerk = SystemUser(); users.Store.Add(clerk);
        var repo = new FakePenaltyRecordRepository(); var me = new FakeCurrentUser();

        var id = await new CreatePenaltyHandler(repo, labs, new FakeRepresentativeRepository(), users, me)
            .Handle(Penalty(lab.Id.Value, "LabRequest", null, null), CancellationToken.None);
        var p = repo.Store.Single(x => x.Id.Value == id);
        p.UserType.Should().BeSameAs(PenaltyUser.LabRequest); p.PerformedByUserId.Should().BeNull(); p.PerformedByRepId.Should().BeNull();
        p.PenaltyAmount.Amount.Should().Be(-180m, "right 120 − wrong 300, the same rule as every other type");

        // Re-typed to DataEntry it names the clerk; back to LabRequest it names nobody again.
        var update = new UpdatePenaltyHandler(repo, labs, new FakeRepresentativeRepository(), users, me);
        await update.Handle(new UpdatePenaltyCommand(id, D, "ACC-9", "Patient", "T1", "Wrong", 300m, "T2", "Right", 120m, "DataEntry", clerk.Id.Value, null), CancellationToken.None);
        p.PerformedByUserId.Should().Be(clerk.Id);
        await update.Handle(new UpdatePenaltyCommand(id, D, "ACC-9", "Patient", "T1", "Wrong", 300m, "T2", "Right", 120m, "LabRequest", null, null), CancellationToken.None);
        p.PerformedByUserId.Should().BeNull();
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
        v.Validate(new SuggestDeductionQuery(Guid.NewGuid(), "Penalty", D, D)).IsValid.Should().BeFalse("penalties left the deductions business (2026-09-18)");
        v.Validate(new SuggestDeductionQuery(Guid.NewGuid(), "PercentageDeal", D, D)).IsValid.Should().BeTrue();
        v.Validate(new SuggestDeductionQuery(Guid.NewGuid(), "PercentageDeal", D, D.AddDays(-1))).IsValid.Should().BeFalse("end before start");
    }

    // ---- Penalties post to the rep statement, never to the deductions (2026-09-18) ----

    [Fact]
    public async Task Recording_editing_and_deleting_a_penalty_never_touches_the_deductions()
    {
        var areas = new FakeAreaRepository(); var area = Area.Create("Nasr City", CityId.New(), false); areas.Store.Add(area);
        var labs = new FakeLaboratoryRepository(); var lab = Lab(); lab.PlaceInHierarchy(null, "Cairo", "Cairo", area.Name); labs.Store.Add(lab);
        var reps = new FakeRepresentativeRepository(); var rep = Rep(); reps.Store.Add(rep);
        var penalties = new FakePenaltyRecordRepository(); var users = new FakeAppUserRepository(); var me = new FakeCurrentUser();

        var id = await new CreatePenaltyHandler(penalties, labs, reps, users, me).Handle(Penalty(lab.Id.Value, "Rep", null, rep.Id.Value), CancellationToken.None);
        await new UpdatePenaltyHandler(penalties, labs, reps, users, me)
            .Handle(new UpdatePenaltyCommand(id, D, "ACC-9", "Patient", "T1", "Wrong", 400m, "T2", "Right", 120m, "Rep", null, rep.Id.Value), CancellationToken.None);
        penalties.Store.Single().PenaltyAmount.Amount.Should().Be(-280m, "right − wrong");
        await new DeletePenaltyHandler(penalties, labs, me).Handle(new DeletePenaltyCommand(id), CancellationToken.None);
        penalties.Store.Should().BeEmpty();
    }

    [Fact]
    public async Task Editing_an_automated_deduction_adjusts_value_and_notes_only()
    {
        var areas = new FakeAreaRepository(); var area = Area.Create("Nasr City", CityId.New(), false); areas.Store.Add(area);
        var deductions = new FakeDeductionRepository();
        var deal = Deduction.AutoDeal(area.Id, new YearMonth(2026, 9), new DateOnly(2026, 9, 12), 250m, "income 2,500 × 10%"); deductions.Store.Add(deal);
        var me = new FakeCurrentUser();

        // An automated row: the area / reason / period sent by the client are ignored, value + notes (+ basis) apply.
        await new UpdateDeductionHandler(deductions, areas, me).Handle(
            new UpdateDeductionCommand(deal.Id.Value, D, "Transportation", 300m, "agreed", new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), "Suggested: income 3,000 × 10%"), CancellationToken.None);
        deal.Reason.Should().BeSameAs(DeductionReason.PercentageDeal);
        deal.PeriodFrom.Should().Be(new DateOnly(2026, 9, 1));
        deal.Value.Amount.Should().Be(300m); deal.Notes.Should().Be("agreed"); deal.IsAdjusted.Should().BeTrue();
        deal.SystemNote.Should().Be("Suggested: income 3,000 × 10%");

        // An automated deal row may be deleted (the automation recreates it for a running month).
        await new DeleteDeductionHandler(deductions, areas, me).Handle(new DeleteDeductionCommand(deal.Id.Value), CancellationToken.None);
        deductions.Store.Should().NotContain(deal);
    }

    // ---- Collection ----

    /// <summary>A Lab Responsible — the only type that collects — optionally placed in a serving branch.</summary>
    private static Representative LabResp(string name = "Responsible", string? branch = null)
    {
        var r = Representative.Register(name, RepresentativeType.LabResponsible, GoalDuration.Monthly, Money.Zero, Money.Zero);
        if (branch is not null) r.AssignScope(branch, "Cairo");
        return r;
    }
    private static FakeCollectionRouting Routing(params Representative[] reps) { var f = new FakeCollectionRouting(); foreach (var r in reps) f.Branches[r.Id] = r.Branch; return f; }
    private static CollectionShareInput[] Shares(params (Representative Rep, decimal Amount)[] s) => s.Select(x => new CollectionShareInput(x.Rep.Id.Value, x.Amount)).ToArray();

    [Fact]
    public async Task Create_group_collection_resolves_every_rep_stores_the_shares_and_the_bank_iban()
    {
        var reps = new FakeRepresentativeRepository(); var r1 = LabResp("A"); var r2 = LabResp("B"); reps.Store.Add(r1); reps.Store.Add(r2);
        var repo = new FakeCollectionRepository();
        var handler = new CreateCollectionHandler(repo, reps, Routing(r1, r2), new FakeTreasuryRepository(), new FakeTreasuryEntryRepository(), new FakeCurrentUser());

        var id = await handler.Handle(new CreateCollectionCommand(D, "Group", Shares((r1, 1500m), (r2, 2000m)), 1000m, 2500m, "18", "Cashier", null), CancellationToken.None);

        var c = repo.Store.Single(x => x.Id.Value == id);
        c.Type.Should().BeSameAs(CollectionType.Group);
        c.RepIds.Should().Equal(r1.Id, r2.Id);
        c.ShareOf(r1.Id).Amount.Should().Be(1500m); c.ShareOf(r2.Id).Amount.Should().Be(2000m);
        c.Iban.Should().BeSameAs(IbanOption.Iban18);
        c.Total.Amount.Should().Be(3500m);
    }

    [Fact]
    public async Task Create_collection_refuses_an_unknown_rep_and_a_rep_who_is_not_a_lab_responsible()
    {
        var reps = new FakeRepresentativeRepository(); var collector = Rep("Collector"); reps.Store.Add(collector);
        var repo = new FakeCollectionRepository();
        var handler = new CreateCollectionHandler(repo, reps, new FakeCollectionRouting(), new FakeTreasuryRepository(), new FakeTreasuryEntryRepository(), new FakeCurrentUser());

        await FluentActions.Awaiting(() => handler.Handle(new CreateCollectionCommand(D, "Single", new[] { new CollectionShareInput(Guid.NewGuid(), 100m) }, 100m, 0m, null, null, null), CancellationToken.None))
            .Should().ThrowAsync<NotFoundException>();
        await FluentActions.Awaiting(() => handler.Handle(new CreateCollectionCommand(D, "Single", Shares((collector, 100m)), 100m, 0m, null, null, null), CancellationToken.None))
            .Should().ThrowAsync<ValidationException>("only Lab Responsible reps collect");
        repo.Store.Should().BeEmpty();
    }

    [Fact]
    public void Collection_validator_enforces_rep_count_shares_amount_and_iban_rules()
    {
        var v = new CreateCollectionValidator();
        var r1 = Guid.NewGuid(); var r2 = Guid.NewGuid();
        CollectionShareInput[] one(decimal a) => new[] { new CollectionShareInput(r1, a) };
        CollectionShareInput[] two(decimal a, decimal b) => new[] { new CollectionShareInput(r1, a), new CollectionShareInput(r2, b) };
        v.Validate(new CreateCollectionCommand(D, "Single", two(50m, 50m), 100m, 0m, null, null, null)).IsValid.Should().BeFalse("single with two reps");
        v.Validate(new CreateCollectionCommand(D, "Group", one(100m), 100m, 0m, null, null, null)).IsValid.Should().BeFalse("group with one rep");
        v.Validate(new CreateCollectionCommand(D, "Group", two(60m, 50m), 100m, 0m, null, null, null)).IsValid.Should().BeFalse("shares do not add up to the total");
        v.Validate(new CreateCollectionCommand(D, "Group", two(100m, 0m), 100m, 0m, null, null, null)).IsValid.Should().BeFalse("a group share must be positive");
        v.Validate(new CreateCollectionCommand(D, "Group", new[] { new CollectionShareInput(r1, 50m), new CollectionShareInput(r1, 50m) }, 100m, 0m, null, null, null)).IsValid.Should().BeFalse("a rep appears once");
        v.Validate(new CreateCollectionCommand(D, "Group", two(60m, 40m), 100m, 0m, null, null, null)).IsValid.Should().BeTrue("shares add up to cash + bank");
        v.Validate(new CreateCollectionCommand(D, "Single", one(0m), 0m, 0m, null, null, null)).IsValid.Should().BeFalse("no amount");
        v.Validate(new CreateCollectionCommand(D, "Single", one(500m), 0m, 500m, null, null, null)).IsValid.Should().BeFalse("bank without IBAN");
        v.Validate(new CreateCollectionCommand(D, "Single", one(500m), 0m, 500m, "99", null, null)).IsValid.Should().BeFalse("IBAN outside 12/16/18");
        v.Validate(new CreateCollectionCommand(D, "Single", one(500m), 0m, 500m, "16", null, null)).IsValid.Should().BeTrue();
        v.Validate(new CreateCollectionCommand(D, "Single", one(0m), 100m, 0m, null, null, null)).IsValid.Should().BeTrue("cash needs no IBAN; a single share is taken as the total");
    }

    // ---- Collection → treasury mirroring + per-treasury rights ----

    private static CreateCollectionCommand Cash(Representative rep, decimal cash = 1000m) =>
        new(D, "Single", Shares((rep, cash)), cash, 0m, null, "Cashier", null);
    private static TreasuryGrant Grant(Role role, Treasury t, bool view, bool validate, bool update) => TreasuryGrant.Create(role.Id, t.Id, view, validate, update);
    private static Role SomeRole() => Role.Create("Cashier", new[] { Privileges.ViewAccounting }, "en", "light", OrgScope.Global);

    [Fact]
    public async Task Recording_a_cash_collection_mirrors_it_as_a_pending_debit_of_the_treasury_serving_the_reps_branch()
    {
        var reps = new FakeRepresentativeRepository(); var rep = LabResp("Rep One", "Giza"); reps.Store.Add(rep);
        var routing = Routing(rep);
        var treasuries = new FakeTreasuryRepository();
        var cairo = Treasury.Create("Cairo Main", new[] { "Cairo" }); var giza = Treasury.Create("Giza Main", new[] { "Giza" }); var gizaB = Treasury.Create("Giza B", new[] { "giza" });
        treasuries.Store.AddRange(new[] { cairo, giza, gizaB });
        var entries = new FakeTreasuryEntryRepository(); var collections = new FakeCollectionRepository(); var me = new FakeCurrentUser();

        var id = await new CreateCollectionHandler(collections, reps, routing, treasuries, entries, me).Handle(Cash(rep), CancellationToken.None);

        var e = entries.Store.Should().ContainSingle().Subject;
        e.TreasuryId.Should().Be(gizaB.Id, "among the active treasuries covering the branch (case-insensitively), the first by name");
        e.Origin.Should().BeSameAs(TreasuryEntryOrigin.AutoCollection);
        e.ValidationStatus.Should().BeSameAs(TreasuryValidationStatus.Pending);
        e.Debit.Amount.Should().Be(1000m); e.CollectionId!.Value.Value.Should().Be(id);
        e.SystemNote.Should().Contain("Rep One").And.Contain("Cashier");

        // Cash edit refreshes the mirror; deleting the collection removes it while still pending.
        await new UpdateCollectionHandler(collections, reps, routing, treasuries, entries, me)
            .Handle(new UpdateCollectionCommand(id, D, "Single", Shares((rep, 800m)), 800m, 0m, null, "Cashier", null), CancellationToken.None);
        entries.Store.Single().Debit.Amount.Should().Be(800m);
        await new DeleteCollectionHandler(collections, reps, entries, me).Handle(new DeleteCollectionCommand(id), CancellationToken.None);
        entries.Store.Should().BeEmpty(); collections.Store.Should().BeEmpty();
    }

    [Fact]
    public async Task A_collection_with_no_receiving_treasury_or_no_cash_is_recorded_without_a_mirror_and_a_validated_mirror_blocks_deletion()
    {
        var reps = new FakeRepresentativeRepository(); var nowhere = LabResp("Nowhere"); var alexRep = LabResp("Alex Rep", "Alex"); reps.Store.Add(nowhere); reps.Store.Add(alexRep);
        var routing = Routing(nowhere, alexRep);
        var treasuries = new FakeTreasuryRepository(); var alex = Treasury.Create("Alex", new[] { "Alex" }); treasuries.Store.Add(alex);
        var entries = new FakeTreasuryEntryRepository(); var collections = new FakeCollectionRepository(); var me = new FakeCurrentUser();
        var handler = new CreateCollectionHandler(collections, reps, routing, treasuries, entries, me);

        await handler.Handle(Cash(nowhere), CancellationToken.None);                                                   // rep resolves to no serving branch
        await handler.Handle(new CreateCollectionCommand(D, "Single", Shares((alexRep, 500m)), 0m, 500m, "16", null, null), CancellationToken.None); // bank only
        entries.Store.Should().BeEmpty("nothing to place; collections are never blocked");
        collections.Store.Should().HaveCount(2);

        var id = await handler.Handle(Cash(alexRep), CancellationToken.None);
        var mirror = entries.Store.Should().ContainSingle().Subject;
        mirror.Validate(1000m, null, "cashier", DateTimeOffset.UtcNow);
        await FluentActions.Awaiting(() => new DeleteCollectionHandler(collections, reps, entries, me).Handle(new DeleteCollectionCommand(id), CancellationToken.None))
            .Should().ThrowAsync<ConflictException>().WithMessage("*already validated*");
        collections.Store.Should().Contain(x => x.Id.Value == id);
    }

    [Fact]
    public async Task Validation_needs_the_treasurys_Validate_right_and_confirms_or_corrects_the_received_cash()
    {
        var treasuries = new FakeTreasuryRepository(); var t = Treasury.Create("Giza", new[] { "Giza" }); treasuries.Store.Add(t);
        var rep = LabResp("Rep", "Giza");
        var c = Collection.Create(D, CollectionType.Single, new[] { (rep.Id, 1000m) }, 1000m, 0m, null, null, null);
        var entries = new FakeTreasuryEntryRepository(); var mirror = TreasuryEntry.FromCollection(t.Id, c, new[] { rep.FullName }); entries.Store.Add(mirror);
        var role = SomeRole(); var me = new FakeCurrentUser { RoleId = role.Id, Username = "cashier" };
        var clock = new FakeClock(new DateTimeOffset(2026, 9, 16, 9, 0, 0, TimeSpan.Zero));

        // View only → 403; a manual entry → 409; Validate right → validated with the corrected amount, by the caller.
        await FluentActions.Awaiting(() => new ValidateTreasuryEntryHandler(entries, treasuries, me, new FakeTreasuryAccess(Grant(role, t, true, false, false)), clock)
                .Handle(new ValidateTreasuryEntryCommand(mirror.Id.Value, 1000m, null), CancellationToken.None))
            .Should().ThrowAsync<ForbiddenException>();
        var manual = TreasuryEntry.Create(t.Id, D, 5m, 0m, TreasuryReasonId.New(), null); entries.Store.Add(manual);
        await FluentActions.Awaiting(() => new ValidateTreasuryEntryHandler(entries, treasuries, me, new FakeTreasuryAccess(Grant(role, t, true, true, false)), clock)
                .Handle(new ValidateTreasuryEntryCommand(manual.Id.Value, 5m, null), CancellationToken.None))
            .Should().ThrowAsync<ConflictException>();
        await new ValidateTreasuryEntryHandler(entries, treasuries, me, new FakeTreasuryAccess(Grant(role, t, true, true, false)), clock)
            .Handle(new ValidateTreasuryEntryCommand(mirror.Id.Value, 950m, "50 short"), CancellationToken.None);
        mirror.ValidationStatus.Should().BeSameAs(TreasuryValidationStatus.Validated);
        mirror.Debit.Amount.Should().Be(950m); mirror.ValidatedBy.Should().Be("cashier"); mirror.ValidatedAt.Should().Be(clock.UtcNow);
        mirror.HasDiscrepancy.Should().BeTrue();

        // Post-validation correction needs the Update right; a mirror can never be deleted from the treasury side.
        var reasons = new FakeTreasuryReasonRepository();
        await FluentActions.Awaiting(() => new UpdateTreasuryEntryHandler(entries, treasuries, reasons, me, new FakeTreasuryAccess(Grant(role, t, true, true, false)))
                .Handle(new UpdateTreasuryEntryCommand(mirror.Id.Value, D, 960m, 0m, Guid.Empty, "recount"), CancellationToken.None))
            .Should().ThrowAsync<ForbiddenException>();
        await new UpdateTreasuryEntryHandler(entries, treasuries, reasons, me, new FakeTreasuryAccess(Grant(role, t, true, false, true)))
            .Handle(new UpdateTreasuryEntryCommand(mirror.Id.Value, D, 960m, 0m, Guid.Empty, "recount"), CancellationToken.None);
        mirror.Debit.Amount.Should().Be(960m); mirror.Notes.Should().Be("recount");
        await FluentActions.Awaiting(() => new DeleteTreasuryEntryHandler(entries, treasuries, me, new FakeTreasuryAccess(Grant(role, t, true, true, true)))
                .Handle(new DeleteTreasuryEntryCommand(mirror.Id.Value), CancellationToken.None))
            .Should().ThrowAsync<ConflictException>().WithMessage("*Collection page*");

        // Recording a manual entry needs the Update right too.
        var reason = TreasuryReason.Create("Fuel"); reasons.Store.Add(reason);
        await FluentActions.Awaiting(() => new CreateTreasuryEntryHandler(entries, treasuries, reasons, me, new FakeTreasuryAccess(Grant(role, t, true, true, false)))
                .Handle(new CreateTreasuryEntryCommand(t.Id.Value, D, 100m, 0m, reason.Id.Value, null), CancellationToken.None))
            .Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task Setting_a_roles_treasury_rights_upserts_and_removes_rows_and_refuses_the_built_in_role()
    {
        var roles = new FakeRoleRepository(); var role = SomeRole(); roles.Store.Add(role);
        var admin = Role.Create("Admin", Privileges.All, "en", "light", OrgScope.Global, isBuiltIn: true); roles.Store.Add(admin);
        var treasuries = new FakeTreasuryRepository(); var a = Treasury.Create("A", new[] { "X" }); var b = Treasury.Create("B", new[] { "Y" }); treasuries.Store.AddRange(new[] { a, b });
        var grants = new FakeTreasuryGrantRepository(); grants.Store.Add(TreasuryGrant.Create(role.Id, b.Id, true, true, true));
        var handler = new SetTreasuryGrantsHandler(grants, treasuries, roles, new FakeCurrentUser());

        await handler.Handle(new SetTreasuryGrantsCommand(role.Id.Value, new[]
        {
            new TreasuryGrantInput(a.Id.Value, false, true, false),   // new row (Validate implies View)
            new TreasuryGrantInput(b.Id.Value, false, false, false),  // all rights removed → row deleted
        }), CancellationToken.None);

        var row = grants.Store.Should().ContainSingle().Subject;
        row.TreasuryId.Should().Be(a.Id); row.CanView.Should().BeTrue(); row.CanValidate.Should().BeTrue(); row.CanUpdate.Should().BeFalse();

        await FluentActions.Awaiting(() => handler.Handle(new SetTreasuryGrantsCommand(admin.Id.Value, Array.Empty<TreasuryGrantInput>()), CancellationToken.None))
            .Should().ThrowAsync<ConflictException>();
        await FluentActions.Awaiting(() => handler.Handle(new SetTreasuryGrantsCommand(role.Id.Value, new[] { new TreasuryGrantInput(Guid.NewGuid(), true, false, false) }), CancellationToken.None))
            .Should().ThrowAsync<NotFoundException>();
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

    // ---- Statement by dimension ----

    [Fact]
    public async Task Statement_by_an_unknown_dimension_is_a_validation_error()
    {
        var handler = new GetStatementHandler(new StubAccountingQueries(), new FakeCurrentUser());
        await FluentActions.Awaiting(() => handler.Handle(new GetStatementQuery("Branch", Guid.NewGuid(), D, D), CancellationToken.None))
            .Should().ThrowAsync<ValidationException>();
        await FluentActions.Awaiting(() => handler.Handle(new GetStatementQuery(StatementBy.Area, Guid.NewGuid(), D, D), CancellationToken.None))
            .Should().ThrowAsync<NotFoundException>("the stub knows no subject");
    }

    /// <summary>Read-side stub: every statement subject is unknown (null).</summary>
    private sealed class StubAccountingQueries : IAccountingQueries
    {
        public Task<StatementDto?> StatementAsync(string by, Guid id, DateOnly from, DateOnly to, OrgScope scope, CancellationToken ct) => Task.FromResult<StatementDto?>(null);
        public Task<IReadOnlyList<TreasuryReasonDto>> TreasuryReasonsAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<TreasuryDto>> TreasuriesAsync(OrgScope scope, TreasuryAccessMap access, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<TreasuryEntryDto>> TreasuryEntriesAsync(DateOnly from, DateOnly to, Guid? treasuryId, OrgScope scope, TreasuryAccessMap access, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<TreasuryGrantDto>> TreasuryGrantsAsync(RoleId roleId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<PenaltyDto>> PenaltiesAsync(DateOnly from, DateOnly to, Guid? laboratoryId, Guid? areaId, OrgScope scope, bool canSeeEncrypted, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<PenaltyActorDto>> PenaltyActorsAsync(PenaltyUser userType, OrgScope scope, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<DeductionDto>> DeductionsAsync(DateOnly from, DateOnly to, Guid? areaId, OrgScope scope, CancellationToken ct) => throw new NotSupportedException();
        public Task<DeductionSuggestionDto> SuggestDeductionAsync(Guid areaId, DeductionReason reason, DateOnly from, DateOnly to, OrgScope scope, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<CollectionDto>> CollectionsAsync(DateOnly from, DateOnly to, Guid? repId, OrgScope scope, CancellationToken ct) => throw new NotSupportedException();
        public Task<RepStatementDto?> RepStatementAsync(Guid repId, DateOnly from, DateOnly to, OrgScope scope, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<RealIncomeRepDto>> RealIncomeRepsAsync(Guid areaId, OrgScope scope, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<RealIncomeLabDto>> RealIncomeLabsAsync(Guid areaId, OrgScope scope, bool canSeeEncrypted, CancellationToken ct) => throw new NotSupportedException();
        public Task<RealIncomeSheetDto?> RealIncomeSheetAsync(Guid areaId, DateOnly date, Guid repId, OrgScope scope, bool canSeeEncrypted, CancellationToken ct) => throw new NotSupportedException();
    }

    // ---- Real income sheet: LDM sync on demand ----

    [Fact]
    public async Task Sync_ldm_for_the_sheet_runs_the_lab_statistics_feed_for_that_single_day()
    {
        var runner = new RecordingOracleRunner();
        var r = await new SyncRealIncomeLdmHandler(runner).Handle(new SyncRealIncomeLdmCommand(D), CancellationToken.None);
        runner.LabStatsCalls.Should().Equal((D, D, true));
        r.Ran.Should().BeTrue();
        new SyncRealIncomeLdmValidator().Validate(new SyncRealIncomeLdmCommand(default)).IsValid.Should().BeFalse("a date is required");
    }

    private sealed class RecordingOracleRunner : IOracleSyncRunner
    {
        public readonly List<(DateOnly From, DateOnly To, bool Manual)> LabStatsCalls = new();
        public Task<OracleSyncResult> RunLabStatsAsync(DateOnly from, DateOnly to, bool manual, CancellationToken ct)
        { LabStatsCalls.Add((from, to, manual)); return Task.FromResult(new OracleSyncResult(true, "ok", 0, 3)); }
        public Task<OracleSyncResult> RunAsync(bool manual, CancellationToken ct) => throw new NotSupportedException();
        public Task<OracleSyncResult> RunTestStatsAsync(DateOnly from, DateOnly to, bool manual, CancellationToken ct) => throw new NotSupportedException();
        public Task<OracleSyncResult> RunDetailedStatsAsync(DateOnly from, DateOnly to, bool manual, CancellationToken ct) => throw new NotSupportedException();
        public Task<OracleSyncResult> RunNightlyStatsAsync(DateOnly from, DateOnly to, bool manual, CancellationToken ct) => throw new NotSupportedException();
    }

    // ---- Real income sheet ----

    [Fact]
    public async Task Saving_the_real_income_sheet_creates_updates_and_removes_lines_and_needs_a_lab_responsible()
    {
        var reps = new FakeRepresentativeRepository(); var rep = LabResp("Resp"); var collector = Rep("Collector"); reps.Store.Add(rep); reps.Store.Add(collector);
        var labs = new FakeLaboratoryRepository(); var a = Lab(); var b = Lab(); labs.Store.AddRange(new[] { a, b });
        var repo = new FakeRepLabIncomeRepository();
        var handler = new SaveRealIncomeSheetHandler(repo, reps, labs, new FakeCurrentUser());
        RealIncomeRowInput Row(Laboratory lab, int samples, decimal required, decimal paid, decimal delayed = 0m, string? notes = null) =>
            new(lab.Id.Value, samples, required, paid, delayed, notes);

        // Create: two labs, one of them all-zero (skipped).
        await handler.Handle(new SaveRealIncomeSheetCommand(D, rep.Id.Value, new[] { Row(a, 10, 1000m, 600m, 50m, "first"), Row(b, 0, 0m, 0m) }), CancellationToken.None);
        var line = repo.Store.Should().ContainSingle().Subject;
        line.LaboratoryId.Should().Be(a.Id); line.Paid.Amount.Should().Be(600m); line.Remaining.Amount.Should().Be(400m); line.DelayedPayment.Amount.Should().Be(50m);

        // Update the existing line, add the second lab; then clear the first (removed) while keeping the second.
        await handler.Handle(new SaveRealIncomeSheetCommand(D, rep.Id.Value, new[] { Row(a, 10, 1000m, 1000m), Row(b, 3, 200m, 200m) }), CancellationToken.None);
        repo.Store.Should().HaveCount(2); repo.Store.Single(x => x.LaboratoryId == a.Id).Remaining.Amount.Should().Be(0m);
        await handler.Handle(new SaveRealIncomeSheetCommand(D, rep.Id.Value, new[] { Row(a, 0, 0m, 0m) }), CancellationToken.None);
        repo.Store.Should().ContainSingle(x => x.LaboratoryId == b.Id, "an all-zero row removes its line; rows not sent are untouched");

        // Only a Lab Responsible has a sheet; an unknown lab is not found.
        await FluentActions.Awaiting(() => handler.Handle(new SaveRealIncomeSheetCommand(D, collector.Id.Value, new[] { Row(a, 1, 10m, 10m) }), CancellationToken.None))
            .Should().ThrowAsync<ValidationException>();
        await FluentActions.Awaiting(() => handler.Handle(new SaveRealIncomeSheetCommand(D, rep.Id.Value, new[] { new RealIncomeRowInput(Guid.NewGuid(), 1, 10m, 10m, 0m, null) }), CancellationToken.None))
            .Should().ThrowAsync<NotFoundException>();

        var v = new SaveRealIncomeSheetValidator();
        v.Validate(new SaveRealIncomeSheetCommand(D, rep.Id.Value, new[] { Row(a, 1, 100m, 120m) })).IsValid.Should().BeFalse("paid above total required");
        v.Validate(new SaveRealIncomeSheetCommand(D, rep.Id.Value, new[] { Row(a, 1, 100m, 50m), Row(a, 1, 100m, 50m) })).IsValid.Should().BeFalse("a lab appears once");
        v.Validate(new SaveRealIncomeSheetCommand(D, rep.Id.Value, new[] { Row(a, 1, 100m, 50m, 20m) })).IsValid.Should().BeTrue();
    }
}
