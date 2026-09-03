using FluentAssertions;
using FollowUp.Domain.Common;
using FollowUp.Domain.Complaints;
using FollowUp.Domain.Laboratories;

namespace FollowUp.Domain.Tests.Complaints;

public class ComplaintTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 15, 10, 0, 0, TimeSpan.FromHours(2));

    private static Complaint NewComplaint() =>
        Complaint.Log(42, LaboratoryId.New(), "TurnaroundTime", "Phone", "Ops", "late results");

    [Fact]
    public void Log_creates_open_complaint_with_reference_and_event()
    {
        var complaint = NewComplaint();

        complaint.Status.Should().Be(ComplaintStatus.Open);
        complaint.Stage.Should().Be(ComplaintStage.Logged);
        complaint.Reference.Should().Be("CMP-42");
        complaint.DomainEvents.OfType<ComplaintLogged>().Should().ContainSingle();
    }

    [Fact]
    public void Start_then_resolve_follows_the_state_machine()
    {
        var complaint = NewComplaint();

        complaint.Start();
        complaint.Status.Should().Be(ComplaintStatus.InProgress);

        complaint.Resolve("manager", Now);
        complaint.Status.Should().Be(ComplaintStatus.Resolved);
        complaint.DomainEvents.OfType<ComplaintResolved>().Should().ContainSingle();
    }

    [Fact]
    public void Resolving_without_bound_signature_is_refused_when_enforced()
    {
        var complaint = NewComplaint();
        complaint.Start();

        var act = () => complaint.Resolve("manager", Now, eSignatureSatisfied: false);

        act.Should().Throw<DomainException>().WithMessage("*signature*");
        complaint.Status.Should().Be(ComplaintStatus.InProgress);
    }

    [Fact]
    public void Illegal_transition_throws_409_mapped_exception()
    {
        var complaint = NewComplaint(); // Open
        // Open -> Open is not a legal edge.
        var act = () => complaint.Reopen();
        act.Should().Throw<IllegalStateTransitionException>();
    }

    [Fact]
    public void Reopen_from_resolved_resets_stage_to_investigation_and_keeps_the_summary()
    {
        var complaint = NewComplaint();
        complaint.SetResolutionSummary("closed after contacting the lab");
        complaint.Resolve("manager", Now); // Open -> Resolved (direct)
        complaint.Reopen();

        complaint.Status.Should().Be(ComplaintStatus.Open);
        complaint.ResolvedAt.Should().BeNull();
        // CMP-20: reopened complaints flow forward from Investigation, not dead-end at Resolution; the prior
        // resolution summary is kept as an audit-trail record.
        complaint.Stage.Should().Be(ComplaintStage.Investigation);
        complaint.ResolutionSummary.Should().Be("closed after contacting the lab");
    }

    [Fact]
    public void MoveToStage_never_changes_status()
    {
        var complaint = NewComplaint();
        complaint.MoveToStage(ComplaintStage.Investigation);

        complaint.Stage.Should().Be(ComplaintStage.Investigation);
        complaint.Status.Should().Be(ComplaintStatus.Open); // stage is metadata only (CMP-STAGE fix)
    }

    [Fact]
    public void MoveToStage_cannot_jump_to_a_gated_terminal_stage()
    {
        var complaint = NewComplaint(); // Open / Logged

        // Resolution carries the resolve + optional e-signature gate; RejectedInvalid carries the validity
        // decision. A bare stage move must not reach either, or the resolve-gate is bypassable (CMP-2).
        var toResolution = () => complaint.MoveToStage(ComplaintStage.Resolution);
        toResolution.Should().Throw<IllegalStateTransitionException>();

        var toRejected = () => complaint.MoveToStage(ComplaintStage.RejectedInvalid);
        toRejected.Should().Throw<IllegalStateTransitionException>();

        complaint.Stage.Should().Be(ComplaintStage.Logged); // nothing changed
    }

    [Fact]
    public void Resolved_complaint_narrative_is_frozen()
    {
        var complaint = NewComplaint();
        complaint.Resolve("manager", Now); // Open -> Resolved (direct)

        // No stage edit is permitted once the complaint is closed (CMP-2).
        var investigate = () => complaint.RecordInvestigation("late edit");
        investigate.Should().Throw<IllegalStateTransitionException>();

        var recheck = () => complaint.CheckValidity(true, "late", "u", Now);
        recheck.Should().Throw<IllegalStateTransitionException>();

        var outcome = () => complaint.RecordOutcome("x", null);
        outcome.Should().Throw<IllegalStateTransitionException>();

        var move = () => complaint.MoveToStage(ComplaintStage.Acknowledged);
        move.Should().Throw<IllegalStateTransitionException>();
    }

    [Fact]
    public void SetIntake_records_representative_and_received_time()
    {
        var complaint = NewComplaint();
        var repId = Guid.NewGuid();

        complaint.SetIntake(repId, Now);

        complaint.RepresentativeId.Should().Be(repId);
        complaint.ReceivedAt.Should().Be(Now);
    }

    [Fact]
    public void CheckValidity_valid_advances_to_ValidityChecked()
    {
        var complaint = NewComplaint();

        complaint.CheckValidity(true, "confirmed with the lab", "checker", Now);

        complaint.Stage.Should().Be(ComplaintStage.ValidityChecked);
        complaint.IsValid.Should().BeTrue();
        complaint.ValidityNotes.Should().Be("confirmed with the lab");
        complaint.Status.Should().Be(ComplaintStatus.Open); // a valid verdict never touches status
    }

    [Fact]
    public void CheckValidity_invalid_closes_the_complaint()
    {
        var complaint = NewComplaint();

        complaint.CheckValidity(false, "not reproducible", "checker", Now);

        // CMP-21: an invalid verdict auto-closes the complaint (no e-sign resolve, drops out of Open KPIs).
        complaint.Stage.Should().Be(ComplaintStage.RejectedInvalid);
        complaint.IsValid.Should().BeFalse();
        complaint.Status.Should().Be(ComplaintStatus.Resolved);
        complaint.ResolvedBy.Should().Be("checker");
        complaint.ResolvedAt.Should().Be(Now);
    }

    [Fact]
    public void RecordInvestigation_requires_notes()
    {
        var complaint = NewComplaint();

        var act = () => complaint.RecordInvestigation("  ");
        act.Should().Throw<DomainException>();

        complaint.RecordInvestigation("root cause: courier delay");
        complaint.Stage.Should().Be(ComplaintStage.Investigation);
        complaint.InvestigationNotes.Should().Be("root cause: courier delay");
    }

    [Fact]
    public void RecordOutcome_requires_type_and_advances_stage()
    {
        var complaint = NewComplaint();

        var act = () => complaint.RecordOutcome(" ", null);
        act.Should().Throw<DomainException>();

        complaint.RecordOutcome("Corrective Action", "retrained the courier");
        complaint.Stage.Should().Be(ComplaintStage.BusinessOutcome);
        complaint.OutcomeType.Should().Be("Corrective Action");
        complaint.OutcomeSummary.Should().Be("retrained the courier");
    }

    [Fact]
    public void Resolution_summary_survives_resolve()
    {
        var complaint = NewComplaint();
        complaint.Start();

        complaint.SetResolutionSummary("credited the affected order");
        complaint.Resolve("manager", Now);

        complaint.ResolutionSummary.Should().Be("credited the affected order");
        complaint.Status.Should().Be(ComplaintStatus.Resolved);
    }

    [Fact]
    public void Content_version_advances_on_every_material_change_and_a_revert_never_lowers_it()
    {
        // SIG-4: a monotonic content version, so an edit-and-revert (A→B→A) can never restore an earlier version
        // and resurrect a signature bound to that earlier state.
        var complaint = NewComplaint();
        complaint.ContentVersion.Should().Be(1u);

        complaint.RecordInvestigation("root cause A");
        var atA = complaint.ContentVersion;
        atA.Should().BeGreaterThan(1u);

        complaint.RecordInvestigation("root cause B");
        complaint.ContentVersion.Should().BeGreaterThan(atA);

        complaint.RecordInvestigation("root cause A"); // revert the field to its earlier value
        complaint.ContentVersion.Should().BeGreaterThan(atA, "reverting content must not restore an earlier version");
    }
}
