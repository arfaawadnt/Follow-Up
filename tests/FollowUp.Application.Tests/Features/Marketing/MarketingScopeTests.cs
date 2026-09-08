using FluentAssertions;
using FollowUp.Application.Common.Exceptions;
using FollowUp.Application.Features.Laboratories.CreateLaboratory;
using FollowUp.Application.Features.Marketing;
using FollowUp.Application.Tests.Common;
using FollowUp.Domain.Identity;
using FollowUp.Domain.Marketing;
using FollowUp.Domain.Representatives;

namespace FollowUp.Application.Tests.Features.Marketing;

/// <summary>
/// Finding M-4 / MSG-005: completing or cancelling a marketing visit must enforce the caller's org scope on
/// the owning lab — the UpdateMarketing privilege alone must not authorize acting on another scope's visit
/// (IDOR). Mirrors Schedule.
/// </summary>
public class MarketingScopeTests
{
    private static async Task<(FakeLaboratoryRepository labs, FakeMarketingVisitRepository visits, Guid visitId)> Seed()
    {
        var labs = new FakeLaboratoryRepository();
        var creator = new FakeCurrentUser { Privileges = new HashSet<string> { Privileges.AddLabs } }; // Global
        await new CreateLaboratoryHandler(labs, creator, new FakeSetupQueries()).Handle(new CreateLaboratoryCommand
        {
            Code = "MGL-8001",
            Name = "Cairo Lab",
            Segment = "B",
            Governorate = "Cairo",
            WorkDays = new[] { "Monday" },
            VisitTimes = new[] { "09:00" },
        }, CancellationToken.None);

        var visits = new FakeMarketingVisitRepository();
        var visit = MarketingVisit.Schedule(1, labs.Store[0].Id, RepresentativeId.New(), MarketingPurpose.Pitch, new DateOnly(2026, 8, 20));
        visits.Add(visit);
        return (labs, visits, visit.Id.Value);
    }

    private static FakeCurrentUser GizaUser() => new()
    {
        Privileges = new HashSet<string> { Privileges.UpdateMarketing },
        Scope = OrgScope.Create(new[] { "*" }, new[] { "Giza" }, new[] { "*" }, new[] { "*" }, new[] { "*" }, new[] { "*" }),
    };

    [Fact]
    public async Task Complete_is_denied_outside_scope()
    {
        var (labs, visits, id) = await Seed();
        var handler = new CompleteMarketingVisitHandler(visits, labs, GizaUser(), new FakeClock(DateTimeOffset.UtcNow));

        var act = () => handler.Handle(new CompleteMarketingVisitCommand(id, "Signed"), CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task Cancel_is_denied_outside_scope()
    {
        var (labs, visits, id) = await Seed();
        var handler = new CancelMarketingVisitHandler(visits, labs, GizaUser());

        var act = () => handler.Handle(new CancelMarketingVisitCommand(id, "no reason"), CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>();
    }
}
