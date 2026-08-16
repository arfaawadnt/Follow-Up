using FluentAssertions;
using FollowUp.Application.Common.Behaviors;
using FollowUp.Application.Common.Exceptions;
using FollowUp.Application.Common.Messaging;
using FollowUp.Domain.Identity;
using MediatR;

namespace FollowUp.Application.Tests.Common;

public class AuthorizationBehaviorTests
{
    private sealed record GuardedRequest : IRequest<Unit>, IAuthorizedRequest
    {
        public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ManageUsers };
    }

    private static Task<Unit> Next() => Task.FromResult(Unit.Value);

    [Fact]
    public async Task Throws_unauthorized_when_not_authenticated()
    {
        var behavior = new AuthorizationBehavior<GuardedRequest, Unit>(new FakeCurrentUser { IsAuthenticated = false });
        var act = () => behavior.Handle(new GuardedRequest(), Next, CancellationToken.None);
        await act.Should().ThrowAsync<UnauthorizedException>();
    }

    [Fact]
    public async Task Throws_forbidden_when_privilege_missing()
    {
        var user = new FakeCurrentUser { Privileges = new HashSet<string> { Privileges.ViewDashboard } };
        var behavior = new AuthorizationBehavior<GuardedRequest, Unit>(user);
        var act = () => behavior.Handle(new GuardedRequest(), Next, CancellationToken.None);
        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task Passes_when_privilege_held()
    {
        var user = new FakeCurrentUser { Privileges = new HashSet<string> { Privileges.ManageUsers } };
        var behavior = new AuthorizationBehavior<GuardedRequest, Unit>(user);
        var result = await behavior.Handle(new GuardedRequest(), Next, CancellationToken.None);
        result.Should().Be(Unit.Value);
    }
}
