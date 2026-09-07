using FluentAssertions;
using FollowUp.Application.Common.Exceptions;
using FollowUp.Application.Features.Laboratories.CreateLaboratory;
using FollowUp.Application.Features.Outsource;
using FollowUp.Application.Tests.Common;
using FollowUp.Domain.Identity;
using FollowUp.Domain.Operations;

namespace FollowUp.Application.Tests.Features.Outsource;

/// <summary>
/// Finding B-2 / OPS-001/OPS-002: advancing or deleting an outsource sample must enforce the caller's org
/// scope on the owning lab — the OutsourceSamples privilege alone must not authorize acting on another
/// scope's record. Mirrors the Create/Update handlers' EnsureInScope.
/// </summary>
public class OutsourceScopeTests
{
    private static async Task<(FakeLaboratoryRepository labs, FakeOutsourceSampleRepository samples, Guid sampleId)> SeedCairoSample()
    {
        var labs = new FakeLaboratoryRepository();
        var creator = new FakeCurrentUser { Privileges = new HashSet<string> { Privileges.AddLabs } }; // Global scope
        await new CreateLaboratoryHandler(labs, creator, new FakeSetupQueries()).Handle(new CreateLaboratoryCommand
        {
            Code = "MGL-9001", Name = "Cairo Lab", Segment = "B", Governorate = "Cairo",
            WorkDays = new[] { "Monday" }, VisitTimes = new[] { "09:00" },
        }, CancellationToken.None);

        var samples = new FakeOutsourceSampleRepository();
        var sample = OutsourceSample.Create(labs.Store[0].Id, new DateOnly(2026, 8, 15), "DestLab", 3);
        samples.Add(sample);
        return (labs, samples, sample.Id.Value);
    }

    private static FakeCurrentUser GizaUser() => new()
    {
        Privileges = new HashSet<string> { Privileges.OutsourceSamples },
        Scope = OrgScope.Create(new[] { "*" }, new[] { "Giza" }, new[] { "*" }, new[] { "*" }, new[] { "*" }, new[] { "*" }),
    };

    [Fact]
    public async Task Advance_status_is_denied_outside_scope()
    {
        var (labs, samples, id) = await SeedCairoSample();
        var handler = new AdvanceOutsourceStatusHandler(samples, labs, GizaUser(), new FakeClock(DateTimeOffset.UtcNow));

        var act = () => handler.Handle(new AdvanceOutsourceStatusCommand(id, "Sent"), CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>();
        samples.Store[0].Status.Should().Be(OutsourceStatus.Collected, "the status must not change on a denied call");
    }

    [Fact]
    public async Task Delete_is_denied_outside_scope()
    {
        var (labs, samples, id) = await SeedCairoSample();
        var handler = new DeleteOutsourceSampleHandler(samples, labs, GizaUser());

        var act = () => handler.Handle(new DeleteOutsourceSampleCommand(id), CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>();
        samples.Store.Should().ContainSingle("a denied delete must not remove the record");
    }
}
