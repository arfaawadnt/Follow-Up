using FluentAssertions;
using FollowUp.Application.Features.LabCheckIn;
using FollowUp.Application.Features.SampleTracking;
using FollowUp.Application.Features.Transfers;
using FollowUp.Application.Tests.Common;
using FollowUp.Domain.Common;
using FollowUp.Domain.Laboratories;
using FollowUp.Domain.Operations;
using FollowUp.Domain.Representatives;

namespace FollowUp.Application.Tests.Features.Operations;

public class BatchOperationsTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 26, 11, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 8, 26);

    private static (FakeLaboratoryRepository labs, Laboratory lab) SeedLab()
    {
        var lab = Laboratory.Register(LabCode.Create("MGL-77"), "Batch Lab", "A");
        var labs = new FakeLaboratoryRepository();
        labs.Store.Add(lab);
        return (labs, lab);
    }

    [Fact]
    public async Task Batch_transfer_confirms_every_line_with_driver_details()
    {
        var (labs, lab) = SeedLab();
        var rep = Representative.Register("Transfer Rep", RepresentativeType.Transfer, GoalDuration.Monthly,
            new Money(0), new Money(0));
        var reps = new FakeRepresentativeRepository();
        reps.Store.Add(rep);

        var visits = new FakeDailyVisitRepository();
        var v1 = DailyVisit.Schedule(lab.Id, null, Today, new TimeOnly(9, 0));
        var v2 = DailyVisit.Schedule(lab.Id, null, Today, new TimeOnly(12, 0));
        v1.CheckIn(5, "c", Now); v2.CheckIn(7, "c", Now);
        visits.Store.AddRange(new[] { v1, v2 });

        var handler = new ConfirmTransfersBatchHandler(visits, labs, reps, new FakeCurrentUser(), new FakeClock(Now));
        var confirmed = await handler.Handle(new ConfirmTransfersBatchCommand(new[]
        {
            new TransferConfirmationLine(v1.Id.Value, rep.Id.Value, "Ali", "0100", "ABC-1"),
            new TransferConfirmationLine(v2.Id.Value, rep.Id.Value, "Ali", "0100", null),
        }), CancellationToken.None);

        confirmed.Should().Be(2);
        v1.TransferConfirmedAt.Should().NotBeNull();
        v2.Transfer!.DriverName.Should().Be("Ali");
    }

    [Fact]
    public async Task Batch_receipt_receives_each_visit_and_derives_lab_active()
    {
        var (labs, lab) = SeedLab();
        var visits = new FakeDailyVisitRepository();
        var v1 = DailyVisit.Schedule(lab.Id, null, Today, new TimeOnly(9, 0));
        v1.CheckIn(5, "c", Now);
        v1.ConfirmTransfer(RepresentativeId.New(), new TransferDetails("D", "0100", null), Now);
        visits.Store.Add(v1);

        var handler = new ConfirmReceiptsBatchHandler(visits, labs, new FakeCurrentUser(), new FakeClock(Now));
        var received = await handler.Handle(new ConfirmReceiptsBatchCommand(new[] { v1.Id.Value }), CancellationToken.None);

        received.Should().Be(1);
        v1.Status.Should().Be(VisitStatus.Received);
        lab.Status.Should().Be(LaboratoryStatus.Active); // BR-5
    }

    [Fact]
    public async Task Assignments_batch_upserts_rows_and_records_steps_for_the_chosen_users()
    {
        var repo = new FakeSampleTrackingRepository();
        var handler = new SaveSampleAssignmentsHandler(repo, new FakeCurrentUser(), new FakeClock(Now));

        var saved = await handler.Handle(new SaveSampleAssignmentsCommand(new[]
        {
            new AssignmentLine("Dokki", Today, 40, "entry.user", "review.user", null, "rush batch"),
        }), CancellationToken.None);

        saved.Should().Be(1);
        var row = repo.Store.Single();
        row.Count.Should().Be(40);
        row.DataEntry!.User.Should().Be("entry.user");
        row.Review!.User.Should().Be("review.user");
        row.Sort.Should().BeNull();
        row.Notes.Should().Be("rush batch");
        row.IsComplete.Should().BeFalse();
    }

    [Fact]
    public async Task Assignments_batch_refuses_sort_before_review()
    {
        var repo = new FakeSampleTrackingRepository();
        var handler = new SaveSampleAssignmentsHandler(repo, new FakeCurrentUser(), new FakeClock(Now));

        var act = () => handler.Handle(new SaveSampleAssignmentsCommand(new[]
        {
            new AssignmentLine("Dokki", Today, 40, "entry.user", null, "sort.user", null),
        }), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>();
    }

    [Fact]
    public async Task Assignments_batch_count_only_line_keeps_steps_untouched()
    {
        var repo = new FakeSampleTrackingRepository();
        var existing = Domain.Operations.SampleTracking.Open("Miami", Today);
        existing.RecordDataEntry(10, "someone", Now);
        repo.Store.Add(existing);

        var handler = new SaveSampleAssignmentsHandler(repo, new FakeCurrentUser(), new FakeClock(Now));
        await handler.Handle(new SaveSampleAssignmentsCommand(new[]
        {
            new AssignmentLine("Miami", Today, 25, null, null, null, null),
        }), CancellationToken.None);

        existing.Count.Should().Be(25);
        existing.DataEntry!.User.Should().Be("someone"); // unchanged
    }
}
