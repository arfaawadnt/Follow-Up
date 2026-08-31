using FluentAssertions;
using FollowUp.Application.Common.Exceptions;
using FollowUp.Application.Features.UserAdmin.Users;
using FollowUp.Application.Tests.Common;
using FollowUp.Domain.Identity;

namespace FollowUp.Application.Tests.Features.UserAdmin;

public class ChangeOwnPasswordHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 15, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Changing_password_revokes_the_users_other_sessions_but_keeps_the_current_one()
    {
        var hasher = new FakePasswordHasher();
        var user = AppUser.Create("bob", hasher.Hash("old-pw-123"), RoleId.New());
        var users = new FakeAppUserRepository();
        users.Store.Add(user);

        var currentSession = UserSessionId.New();
        var otherSession = UserSessionId.New();
        var sessions = new FakeUserSessionRepository();
        sessions.Store.Add(UserSession.Issue(currentSession, user.Id, "hash-current", Now, Now.AddHours(10), null, null));
        sessions.Store.Add(UserSession.Issue(otherSession, user.Id, "hash-other", Now, Now.AddHours(10), null, null));

        var caller = new FakeCurrentUser { UserId = user.Id, SessionId = currentSession };
        var handler = new ChangeOwnPasswordHandler(users, sessions, caller, hasher, new FakeClock(Now.AddMinutes(5)));

        await handler.Handle(new ChangeOwnPasswordCommand("old-pw-123", "new-pw-456"), CancellationToken.None);

        var later = Now.AddHours(1);
        sessions.Store.Single(s => s.Id == otherSession).IsActive(later)
            .Should().BeFalse("the other session must be revoked so a stolen token cannot survive the change");
        sessions.Store.Single(s => s.Id == currentSession).IsActive(later)
            .Should().BeTrue("the caller's current session is preserved");
        hasher.Verify("new-pw-456", user.Password).Should().BeTrue();
    }

    [Fact]
    public async Task Wrong_current_password_is_rejected_and_changes_nothing()
    {
        var hasher = new FakePasswordHasher();
        var user = AppUser.Create("bob", hasher.Hash("old-pw-123"), RoleId.New());
        var users = new FakeAppUserRepository();
        users.Store.Add(user);
        var caller = new FakeCurrentUser { UserId = user.Id, SessionId = UserSessionId.New() };
        var handler = new ChangeOwnPasswordHandler(users, new FakeUserSessionRepository(), caller, hasher, new FakeClock(Now));

        var act = () => handler.Handle(new ChangeOwnPasswordCommand("wrong-old", "new-pw-456"), CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>();
        hasher.Verify("old-pw-123", user.Password).Should().BeTrue();
    }
}
