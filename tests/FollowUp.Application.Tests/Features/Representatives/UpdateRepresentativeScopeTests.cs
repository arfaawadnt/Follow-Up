using FluentAssertions;
using FollowUp.Application.Common.Exceptions;
using FollowUp.Application.Features.Representatives.UpdateRepresentative;
using FollowUp.Application.Tests.Common;
using FollowUp.Domain.Common;
using FollowUp.Domain.Identity;
using FollowUp.Domain.Representatives;

namespace FollowUp.Application.Tests.Features.Representatives;

/// <summary>
/// Finding B-3 / LAB-003: updating a representative must enforce the caller's org scope on the rep — the
/// UpdateReps/ManageReps privilege alone must not authorize mutating a rep attributed to another scope.
/// </summary>
public class UpdateRepresentativeScopeTests
{
    [Fact]
    public async Task Update_is_denied_when_rep_is_outside_scope()
    {
        var reps = new FakeRepresentativeRepository();
        var rep = Representative.Register("Original Name", RepresentativeType.Collector, GoalDuration.Monthly, Money.Zero, Money.Zero);
        rep.AssignScope(null, "Cairo", null, null); // attributed to Cairo
        reps.Add(rep);

        var user = new FakeCurrentUser
        {
            Privileges = new HashSet<string> { Privileges.UpdateReps },
            Scope = OrgScope.Create(new[] { "*" }, new[] { "Giza" }, new[] { "*" }, new[] { "*" }, new[] { "*" }, new[] { "*" }),
        };
        var handler = new UpdateRepresentativeHandler(reps, user);

        var act = () => handler.Handle(new UpdateRepresentativeCommand
        {
            Id = rep.Id.Value,
            RowVersion = rep.RowVersion,
            FullName = "Hacked Name",
            Salary = 9999,
            Target = 0,
        }, CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>();
        reps.Store[0].FullName.Should().Be("Original Name", "a denied update must not mutate the rep");
    }
}
