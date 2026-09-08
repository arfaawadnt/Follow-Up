using FluentAssertions;
using FollowUp.Application.Common.Exceptions;
using FollowUp.Application.Features.DailyBoard.Commands;
using FollowUp.Application.Features.LabCheckIn;
using FollowUp.Application.Features.Marketing;
using FollowUp.Application.Features.Outsource;
using FollowUp.Application.Features.Transfers;
using FollowUp.Application.Tests.Common;
using FollowUp.Domain.Laboratories;
using FollowUp.Domain.Marketing;
using FollowUp.Domain.Operations;
using FollowUp.Domain.Representatives;

namespace FollowUp.Application.Tests.Features.Operations;

public class OperationalModulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 15, 11, 0, 0, TimeSpan.FromHours(2));
    private static readonly DateOnly Today = new(2026, 8, 15);

    private static (FakeLaboratoryRepository labs, Laboratory lab) SeedLab()
    {
        var lab = Laboratory.Register(LabCode.Create("MGL-9"), "Lab", "A");
        var labs = new FakeLaboratoryRepository();
        labs.Store.Add(lab);
        return (labs, lab);
    }

    [Fact]
    public async Task Confirm_transfer_records_driver_and_moves_state()
    {
        var (labs, lab) = SeedLab();
        var rep = Representative.Register("Driver Rep", RepresentativeType.Transfer, GoalDuration.Monthly,
            new Domain.Common.Money(0), new Domain.Common.Money(0));
        var reps = new FakeRepresentativeRepository();
        reps.Store.Add(rep);

        var visit = DailyVisit.Schedule(lab.Id, null, Today, new TimeOnly(9, 0));
        visit.CheckIn(5, "collector", Now);
        var visits = new FakeDailyVisitRepository();
        visits.Store.Add(visit);

        var handler = new ConfirmTransferHandler(visits, labs, reps, new FakeCurrentUser(), new FakeClock(Now));
        await handler.Handle(new ConfirmTransferCommand
        {
            VisitId = visit.Id.Value,
            TransferRepId = rep.Id.Value,
            DriverName = "Ahmed",
            DriverMobile = "01000000000",
            CarPlate = "XYZ-1",
        }, CancellationToken.None);

        visit.Transfer!.DriverName.Should().Be("Ahmed");
        visit.TransferConfirmedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Check_in_reassigning_a_nonexistent_collector_is_rejected()
    {
        // BRD-7: the collector override took any GUID; a non-existent rep failed late at the Restrict FK as a
        // 500 (or a valid wrong rep was silently credited). The handler must verify existence up front.
        var (labs, lab) = SeedLab();
        var visit = DailyVisit.Schedule(lab.Id, null, Today, new TimeOnly(9, 0));
        var visits = new FakeDailyVisitRepository();
        visits.Store.Add(visit);
        var reps = new FakeRepresentativeRepository(); // empty -> the override rep does not exist

        var handler = new CheckInVisitHandler(visits, labs, new FakeOutsourceSampleRepository(), reps,
            new FakeVisitAttachmentRepository(), new FakeCurrentUser(), new FakeClock(Now));

        var act = () => handler.Handle(
            new CheckInVisitCommand(visit.Id.Value, 5) { CollectorRepId = Guid.NewGuid() }, CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
        visit.Status.Should().Be(VisitStatus.Pending); // nothing was checked in
    }

    [Fact]
    public async Task Check_in_reassigning_an_existing_collector_succeeds()
    {
        var (labs, lab) = SeedLab();
        var rep = Representative.Register("Collector Rep", RepresentativeType.Collector, GoalDuration.Monthly,
            new Domain.Common.Money(0), new Domain.Common.Money(0));
        var reps = new FakeRepresentativeRepository();
        reps.Store.Add(rep);
        var visit = DailyVisit.Schedule(lab.Id, null, Today, new TimeOnly(9, 0));
        var visits = new FakeDailyVisitRepository();
        visits.Store.Add(visit);

        var handler = new CheckInVisitHandler(visits, labs, new FakeOutsourceSampleRepository(), reps,
            new FakeVisitAttachmentRepository(), new FakeCurrentUser(), new FakeClock(Now));

        await handler.Handle(
            new CheckInVisitCommand(visit.Id.Value, 5) { CollectorRepId = rep.Id.Value }, CancellationToken.None);

        visit.Status.Should().Be(VisitStatus.Visited);
        visit.CollectorRepId.Should().Be(rep.Id);
    }

    [Fact]
    public async Task Manual_record_creates_a_collected_visit_and_binds_attachments()
    {
        var (labs, lab) = SeedLab();
        var visits = new FakeDailyVisitRepository();
        var attachments = new FakeVisitAttachmentRepository();
        var att = VisitAttachment.CreatePending("stored.pdf", "form.pdf", "application/pdf", 1234);
        // Simulate the audit interceptor stamping the uploader (CreatedBy) on the prior upload request, so the
        // owner-check in BindAttachmentsAsync (finding M-1) binds the caller's own pending attachment.
        typeof(VisitAttachment).GetProperty(nameof(VisitAttachment.CreatedBy))!
            .SetValue(att, "tester"); // FakeCurrentUser default username
        attachments.Store.Add(att);

        var handler = new RecordManualVisitHandler(visits, labs, new FakeOutsourceSampleRepository(),
            new FakeRepresentativeRepository(), attachments, new FakeCurrentUser(), new FakeClock(Now));

        var id = await handler.Handle(
            new RecordManualVisitCommand(lab.Id.Value, 7) { AttachmentIds = new[] { att.Id.Value } },
            CancellationToken.None);

        var visit = visits.Store.Single();
        visit.Id.Value.Should().Be(id);
        visit.Status.Should().Be(VisitStatus.Visited);   // recorded straight to Collected
        visit.SampleCount.Should().Be(7);
        att.VisitId.Should().Be(id);                     // pending attachment bound to the new visit
        att.LaboratoryId.Should().Be(lab.Id);
    }

    [Fact]
    public async Task Manual_record_advances_to_the_next_free_slot_when_the_second_is_taken()
    {
        // OPS-006: manual entries auto-assign a whole-second slot; when that second is already taken they must
        // advance to the next free one rather than collide on the (lab, date, time) unique index.
        var (labs, lab) = SeedLab();
        var clock = new FakeClock(Now);
        var occupied = new TimeOnly(clock.CairoNow.TimeOfDay.Hours, clock.CairoNow.TimeOfDay.Minutes, clock.CairoNow.TimeOfDay.Seconds);
        var visits = new FakeDailyVisitRepository();
        visits.Store.Add(DailyVisit.Schedule(lab.Id, null, clock.CairoToday, occupied)); // slot already taken

        var handler = new RecordManualVisitHandler(visits, labs, new FakeOutsourceSampleRepository(),
            new FakeRepresentativeRepository(), new FakeVisitAttachmentRepository(), new FakeCurrentUser(), clock);

        var id = await handler.Handle(new RecordManualVisitCommand(lab.Id.Value, 3), CancellationToken.None);

        var created = visits.Store.Single(v => v.Id.Value == id);
        created.ScheduledTime.Should().Be(occupied.Add(TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public async Task Confirm_receipt_receives_and_derives_active()
    {
        var (labs, lab) = SeedLab();
        var visit = DailyVisit.Schedule(lab.Id, null, Today, new TimeOnly(9, 0));
        visit.CheckIn(5, "c", Now);
        visit.ConfirmTransfer(RepresentativeId.New(), new TransferDetails("A", "0100", null), Now);
        var visits = new FakeDailyVisitRepository();
        visits.Store.Add(visit);

        var handler = new ConfirmReceiptHandler(visits, labs, new FakeCurrentUser(), new FakeClock(Now));
        await handler.Handle(new ConfirmReceiptCommand(visit.Id.Value), CancellationToken.None);

        visit.Status.Should().Be(VisitStatus.Received);
        lab.Status.Should().Be(LaboratoryStatus.Active);
    }

    [Fact]
    public async Task Confirm_receipt_rejects_a_rep_linked_caller_who_did_not_collect_the_visit()
    {
        // OPS-007: ConfirmReceipt only checked lab scope, unlike Confirm/BatchTransfer which also enforce
        // owner-if-rep-linked. A rep-linked account may only act on visits it collected.
        var (labs, lab) = SeedLab();
        var collector = RepresentativeId.New();
        var visit = DailyVisit.Schedule(lab.Id, collector, Today, new TimeOnly(9, 0));
        visit.CheckIn(5, "c", Now);
        visit.ConfirmTransfer(RepresentativeId.New(), new TransferDetails("A", "0100", null), Now);
        var visits = new FakeDailyVisitRepository();
        visits.Store.Add(visit);

        // Caller is linked to a different representative than the collector.
        var caller = new FakeCurrentUser { RepresentativeId = RepresentativeId.New() };
        var handler = new ConfirmReceiptHandler(visits, labs, caller, new FakeClock(Now));

        var act = () => handler.Handle(new ConfirmReceiptCommand(visit.Id.Value), CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>();
        visit.Status.Should().NotBe(VisitStatus.Received); // receipt did not happen
    }

    [Fact]
    public async Task Outsource_rejects_duplicate_for_same_lab_and_date()
    {
        var (labs, lab) = SeedLab();
        var repo = new FakeOutsourceSampleRepository();
        var handler = new CreateOutsourceSampleHandler(repo, labs, new FakeCurrentUser());
        var cmd = new CreateOutsourceSampleCommand
        {
            LaboratoryId = lab.Id.Value,
            VisitDate = Today,
            DestinationLab = "Ext",
            Quantity = 2,
        };
        await handler.Handle(cmd, CancellationToken.None);

        var act = () => handler.Handle(cmd, CancellationToken.None);
        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task Marketing_complete_sets_outcome()
    {
        var (labs, lab) = SeedLab();
        var reps = new FakeRepresentativeRepository();
        var rep = Representative.Register("Mkt", RepresentativeType.Marketing, GoalDuration.Monthly,
            new Domain.Common.Money(0), new Domain.Common.Money(0));
        reps.Store.Add(rep);
        var repo = new FakeMarketingVisitRepository();

        var scheduleHandler = new ScheduleMarketingVisitHandler(repo, labs, reps, new FakeCurrentUser());
        var id = await scheduleHandler.Handle(new ScheduleMarketingVisitCommand
        {
            LaboratoryId = lab.Id.Value,
            RepresentativeId = rep.Id.Value,
            Purpose = "Pitch",
            ScheduledDate = Today,
            ScheduledTime = new TimeOnly(11, 0),
            Plan = "bring the new brochure",
        }, CancellationToken.None);

        repo.Store[0].Reference.Should().Be("MV1"); // sequential number assigned by the handler
        repo.Store[0].ScheduledTime.Should().Be(new TimeOnly(11, 0));
        repo.Store[0].Plan.Should().Be("bring the new brochure");

        var completeHandler = new CompleteMarketingVisitHandler(repo, labs, new FakeCurrentUser(), new FakeClock(Now));
        await completeHandler.Handle(new CompleteMarketingVisitCommand(id, "Signed renewal"), CancellationToken.None);

        repo.Store[0].Status.Should().Be(MarketingVisitStatus.Completed);
        repo.Store[0].Outcome.Should().Be("Signed renewal");
    }

    [Fact]
    public async Task Marketing_numbers_are_sequential_across_visits()
    {
        var (labs, lab) = SeedLab();
        var reps = new FakeRepresentativeRepository();
        var rep = Representative.Register("Mkt", RepresentativeType.Marketing, GoalDuration.Monthly,
            new Domain.Common.Money(0), new Domain.Common.Money(0));
        reps.Store.Add(rep);
        var repo = new FakeMarketingVisitRepository();
        var handler = new ScheduleMarketingVisitHandler(repo, labs, reps, new FakeCurrentUser());

        await handler.Handle(new ScheduleMarketingVisitCommand
        { LaboratoryId = lab.Id.Value, RepresentativeId = rep.Id.Value, Purpose = "Routine", ScheduledDate = Today }, CancellationToken.None);
        await handler.Handle(new ScheduleMarketingVisitCommand
        { LaboratoryId = lab.Id.Value, RepresentativeId = rep.Id.Value, Purpose = "Renewal", ScheduledDate = Today }, CancellationToken.None);

        repo.Store.Select(v => v.Reference).Should().ContainInOrder("MV1", "MV2");
    }
}
