using FluentAssertions;
using FollowUp.Application.Common.Exceptions;
using FollowUp.Application.Features.Representatives.CreateRepresentative;
using FollowUp.Application.Tests.Common;
using FollowUp.Domain.Identity;

namespace FollowUp.Application.Tests.Features.Representatives;

/// <summary>
/// Finding M-2 / LAB-004: creating a representative must reject a target attribution outside the caller's
/// org scope, exactly as CreateLaboratory does — otherwise a scoped caller can plant out-of-scope reps.
/// </summary>
public class CreateRepresentativeScopeTests
{
    [Fact]
    public async Task Rejects_creation_outside_scope()
    {
        var reps = new FakeRepresentativeRepository();
        var user = new FakeCurrentUser
        {
            Privileges = new HashSet<string> { Privileges.AddReps },
            Scope = OrgScope.Create(new[] { "*" }, new[] { "Giza" }, new[] { "*" }, new[] { "*" }, new[] { "*" }, new[] { "*" }),
        };
        var handler = new CreateRepresentativeHandler(reps, user);

        var act = () => handler.Handle(new CreateRepresentativeCommand { FullName = "Planted Rep", Governorate = "Cairo" }, CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>();
        reps.Store.Should().BeEmpty("an out-of-scope rep must not be created");
    }
}
