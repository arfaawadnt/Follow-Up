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
    public void Reopen_from_resolved_clears_resolution()
    {
        var complaint = NewComplaint();
        complaint.Resolve("manager", Now); // Open -> Resolved (direct)
        complaint.Reopen();

        complaint.Status.Should().Be(ComplaintStatus.Open);
        complaint.ResolvedAt.Should().BeNull();
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

        complaint.CheckValidity(true, "confirmed with the lab");

        complaint.Stage.Should().Be(ComplaintStage.ValidityChecked);
        complaint.IsValid.Should().BeTrue();
        complaint.ValidityNotes.Should().Be("confirmed with the lab");
        complaint.Status.Should().Be(ComplaintStatus.Open); // stage payloads never touch status
    }

    [Fact]
    public void CheckValidity_invalid_routes_to_RejectedInvalid()
    {
        var complaint = NewComplaint();

        complaint.CheckValidity(false, "not reproducible");

        complaint.Stage.Should().Be(ComplaintStage.RejectedInvalid);
        complaint.IsValid.Should().BeFalse();
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
}
