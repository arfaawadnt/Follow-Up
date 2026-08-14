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
}
