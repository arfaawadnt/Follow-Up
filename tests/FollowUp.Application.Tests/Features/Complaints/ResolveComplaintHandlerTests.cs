using FluentAssertions;
using FollowUp.Application.Common.Exceptions;
using FollowUp.Application.Features.Complaints.Commands;
using FollowUp.Application.Tests.Common;
using FollowUp.Domain.Common;
using FollowUp.Domain.Complaints;
using FollowUp.Domain.Laboratories;

namespace FollowUp.Application.Tests.Features.Complaints;

public class ResolveComplaintHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 15, 10, 0, 0, TimeSpan.FromHours(2));

    private static (FakeComplaintRepository, FakeLaboratoryRepository, Complaint) Seed()
    {
        var lab = Laboratory.Register(LabCode.Create("MGL-1"), "Lab", "B");
        var labs = new FakeLaboratoryRepository();
        labs.Store.Add(lab);

        var complaint = Complaint.Log(1, lab.Id, "TAT", "Phone", "Ops", "late");
        complaint.Start();
        var complaints = new FakeComplaintRepository();
        complaints.Store.Add(complaint);
        return (complaints, labs, complaint);
    }

    [Fact]
    public async Task Resolves_when_enforcement_is_off()
    {
        var (complaints, labs, complaint) = Seed();
        var handler = new ResolveComplaintHandler(complaints, labs, new FakeCurrentUser(), new FakeClock(Now),
            new FakeSignatureGate { Enforced = false });

        await handler.Handle(new ResolveComplaintCommand(complaint.Id.Value), CancellationToken.None);

        complaint.Status.Should().Be(ComplaintStatus.Resolved);
    }

    [Fact]
    public async Task Refuses_resolution_when_enforced_and_unsigned()
    {
        var (complaints, labs, complaint) = Seed();
        var handler = new ResolveComplaintHandler(complaints, labs, new FakeCurrentUser(), new FakeClock(Now),
            new FakeSignatureGate { Enforced = true, HasValid = false });

        var act = () => handler.Handle(new ResolveComplaintCommand(complaint.Id.Value), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("*signature*");
        complaint.Status.Should().Be(ComplaintStatus.InProgress);
    }

    [Fact]
    public async Task Resolves_when_enforced_and_signed()
    {
        var (complaints, labs, complaint) = Seed();
        var handler = new ResolveComplaintHandler(complaints, labs, new FakeCurrentUser(), new FakeClock(Now),
            new FakeSignatureGate { Enforced = true, HasValid = true });

        await handler.Handle(new ResolveComplaintCommand(complaint.Id.Value), CancellationToken.None);

        complaint.Status.Should().Be(ComplaintStatus.Resolved);
    }
}

public class AdvanceComplaintStageHandlerTests
{
    private static (FakeComplaintRepository, FakeLaboratoryRepository, Complaint) Seed()
    {
        var lab = Laboratory.Register(LabCode.Create("MGL-2"), "Lab", "B");
        var labs = new FakeLaboratoryRepository();
        labs.Store.Add(lab);

        var complaint = Complaint.Log(2, lab.Id, "Result Quality", "Phone", "Ops", "wrong result");
        var complaints = new FakeComplaintRepository();
        complaints.Store.Add(complaint);
        return (complaints, labs, complaint);
    }

    [Fact]
    public async Task Validity_stage_routes_to_CheckValidity()
    {
        var (complaints, labs, complaint) = Seed();
        var handler = new AdvanceComplaintStageHandler(complaints, labs, new FakeCurrentUser());

        await handler.Handle(new AdvanceComplaintStageCommand(complaint.Id.Value, "ValidityChecked",
            Notes: "verified", IsValid: true), CancellationToken.None);

        complaint.Stage.Should().Be(ComplaintStage.ValidityChecked);
        complaint.IsValid.Should().BeTrue();
        complaint.ValidityNotes.Should().Be("verified");
    }

    [Fact]
    public async Task Invalid_validity_routes_to_RejectedInvalid()
    {
        var (complaints, labs, complaint) = Seed();
        var handler = new AdvanceComplaintStageHandler(complaints, labs, new FakeCurrentUser());

        await handler.Handle(new AdvanceComplaintStageCommand(complaint.Id.Value, "ValidityChecked",
            IsValid: false), CancellationToken.None);

        complaint.Stage.Should().Be(ComplaintStage.RejectedInvalid);
    }

    [Fact]
    public async Task Investigation_and_outcome_stages_store_their_payloads()
    {
        var (complaints, labs, complaint) = Seed();
        var handler = new AdvanceComplaintStageHandler(complaints, labs, new FakeCurrentUser());

        await handler.Handle(new AdvanceComplaintStageCommand(complaint.Id.Value, "Investigation",
            Notes: "courier delay"), CancellationToken.None);
        complaint.Stage.Should().Be(ComplaintStage.Investigation);
        complaint.InvestigationNotes.Should().Be("courier delay");

        await handler.Handle(new AdvanceComplaintStageCommand(complaint.Id.Value, "BusinessOutcome",
            OutcomeType: "Training", Summary: "retrained"), CancellationToken.None);
        complaint.Stage.Should().Be(ComplaintStage.BusinessOutcome);
        complaint.OutcomeType.Should().Be("Training");
    }

    [Fact]
    public void Validator_enforces_per_stage_payloads()
    {
        var validator = new AdvanceComplaintStageValidator();

        validator.Validate(new AdvanceComplaintStageCommand(Guid.NewGuid(), "ValidityChecked"))
            .IsValid.Should().BeFalse(); // IsValid decision missing
        validator.Validate(new AdvanceComplaintStageCommand(Guid.NewGuid(), "Investigation"))
            .IsValid.Should().BeFalse(); // notes missing
        validator.Validate(new AdvanceComplaintStageCommand(Guid.NewGuid(), "BusinessOutcome"))
            .IsValid.Should().BeFalse(); // outcome type missing
        validator.Validate(new AdvanceComplaintStageCommand(Guid.NewGuid(), "Acknowledged"))
            .IsValid.Should().BeTrue();
    }
}
