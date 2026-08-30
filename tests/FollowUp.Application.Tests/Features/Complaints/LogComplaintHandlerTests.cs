using FluentAssertions;
using FollowUp.Application.Common.Exceptions;
using FollowUp.Application.Features.Complaints.Commands;
using FollowUp.Application.Tests.Common;
using FollowUp.Domain.Common;
using FollowUp.Domain.Laboratories;
using FollowUp.Domain.Representatives;

namespace FollowUp.Application.Tests.Features.Complaints;

/// <summary>
/// CMP-12: complaint.representative_id was an unconstrained Guid with no existence check, so a complaint could be
/// logged against a representative that does not exist. LogComplaintHandler now rejects an unknown id with a 404;
/// the fk_complaint_representative constraint is the matching database guard.
/// </summary>
public class LogComplaintHandlerTests
{
    private static (FakeComplaintRepository, FakeLaboratoryRepository, FakeRepresentativeRepository, Laboratory) Seed()
    {
        var lab = Laboratory.Register(LabCode.Create("MGL-9"), "Lab", "B");
        var labs = new FakeLaboratoryRepository();
        labs.Store.Add(lab);
        return (new FakeComplaintRepository(), labs, new FakeRepresentativeRepository(), lab);
    }

    private static LogComplaintCommand Cmd(Guid labId, Guid? repId) => new()
    {
        LaboratoryId = labId, Category = "Result Quality", ViaChannel = "Phone", Details = "d", RepresentativeId = repId,
    };

    [Fact]
    public async Task Logging_against_an_unknown_representative_is_rejected()
    {
        var (complaints, labs, reps, lab) = Seed();
        var handler = new LogComplaintHandler(complaints, labs, reps, new FakeCurrentUser());

        var act = () => handler.Handle(Cmd(lab.Id.Value, Guid.NewGuid()), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
        complaints.Store.Should().BeEmpty();
    }

    [Fact]
    public async Task Logging_with_a_known_representative_or_none_succeeds()
    {
        var (complaints, labs, reps, lab) = Seed();
        var rep = Representative.Register("Rep", RepresentativeType.Collector, GoalDuration.Monthly,
            salary: new Money(3000m), target: new Money(2000m));
        reps.Store.Add(rep);
        var handler = new LogComplaintHandler(complaints, labs, reps, new FakeCurrentUser());

        (await handler.Handle(Cmd(lab.Id.Value, rep.Id.Value), CancellationToken.None)).Should().StartWith("CMP-");
        (await handler.Handle(Cmd(lab.Id.Value, null), CancellationToken.None)).Should().StartWith("CMP-");
        complaints.Store.Should().HaveCount(2);
    }
}
