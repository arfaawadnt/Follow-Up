using FluentAssertions;
using FollowUp.Domain.Common;
using FollowUp.Domain.Laboratories;
using FollowUp.Domain.Operations;
using FollowUp.Domain.Representatives;

namespace FollowUp.Domain.Tests.Operations;

public class DailyVisitTests
{
    private static DailyVisit NewPendingVisit() =>
        DailyVisit.Schedule(LaboratoryId.New(), RepresentativeId.New(),
            new DateOnly(2026, 8, 15), new TimeOnly(9, 0));

    private static readonly DateTimeOffset Now = new(2026, 8, 15, 9, 5, 0, TimeSpan.FromHours(2));

    [Fact]
    public void Schedule_starts_pending()
    {
        NewPendingVisit().Status.Should().Be(VisitStatus.Pending);
    }

    [Fact]
    public void CheckIn_moves_to_visited_and_raises_event()
    {
        var visit = NewPendingVisit();

        visit.CheckIn(12, "collector1", Now);

        visit.Status.Should().Be(VisitStatus.Visited);
        visit.SampleCount.Should().Be(12);
        visit.DomainEvents.OfType<VisitCheckedIn>().Should().ContainSingle(e => e.SampleCount == 12);
    }

    [Fact]
    public void CheckIn_with_negative_sample_count_is_rejected()
    {
        var visit = NewPendingVisit();
        var act = () => visit.CheckIn(-1, "collector1", Now);
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Miss_from_pending_is_allowed_but_from_visited_is_illegal()
    {
        var missable = NewPendingVisit();
        missable.Miss();
        missable.Status.Should().Be(VisitStatus.Missed);

        var visited = NewPendingVisit();
        visited.CheckIn(3, "c", Now);
        var act = () => visited.Miss();
        act.Should().Throw<IllegalStateTransitionException>();
    }

    [Fact]
    public void Undo_reverts_a_checkin_when_not_transferred()
    {
        var visit = NewPendingVisit();
        visit.CheckIn(5, "c", Now);

        visit.Undo();

        visit.Status.Should().Be(VisitStatus.Pending);
        visit.SampleCount.Should().BeNull();
    }

    [Fact]
    public void Undo_is_refused_once_transferred()
    {
        var visit = NewPendingVisit();
        visit.CheckIn(5, "c", Now);
        visit.ConfirmTransfer(RepresentativeId.New(),
            new TransferDetails("Ahmed", "01000000000", "ABC-123"), Now);

        var act = () => visit.Undo();

        act.Should().Throw<DomainException>().WithMessage("*transferred*");
    }

    [Fact]
    public void Receive_marks_received_and_rolls_to_monthly_only_when_verified()
    {
        var visit = NewPendingVisit();
        visit.CheckIn(7, "c", Now);
        visit.ConfirmTransfer(RepresentativeId.New(), new TransferDetails("A", "0100", null), Now);
        visit.ReceiveAtLab(Now.AddHours(1));

        visit.Status.Should().Be(VisitStatus.Received);
        visit.RollsToMonthly.Should().BeFalse();

        visit.SetVerified(true);
        visit.RollsToMonthly.Should().BeTrue();
    }

    [Fact]
    public void Transfer_requires_a_visited_visit()
    {
        var visit = NewPendingVisit();
        var act = () => visit.ConfirmTransfer(RepresentativeId.New(), new TransferDetails("A", "0100", null), Now);
        act.Should().Throw<DomainException>();
    }
}
