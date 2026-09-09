using FluentAssertions;
using FollowUp.Application.Common.Exceptions;
using FollowUp.Application.Features.UserAdmin.Users;
using FollowUp.Application.Tests.Common;
using FollowUp.Domain.Common;
using FollowUp.Domain.Identity;

namespace FollowUp.Application.Tests.Features.UserAdmin;

/// <summary>
/// IDN-6: the built-in administrator was protected from deletion by a hardcoded "admin" username check and not
/// at all from demotion. The protection is now an AppUser.IsBuiltIn flag enforced on both delete and role change
/// (the tests use a non-"admin" username so they exercise the flag, not the old literal).
/// </summary>
public class BuiltInAdminTests
{
    private static AppUser BuiltInAdmin()
    {
        var admin = AppUser.Create("root", new FakePasswordHasher().Hash("pw12345678"), RoleId.New());
        admin.MarkAsBuiltIn();
        return admin;
    }

    [Fact]
    public void The_built_in_admins_role_cannot_be_changed_but_a_same_role_update_is_allowed()
    {
        var admin = BuiltInAdmin();

        admin.ChangeRole(admin.RoleId); // re-passing the current role (e.g. a profile update) is fine

        var act = () => admin.ChangeRole(RoleId.New());
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public async Task The_built_in_admin_cannot_be_deleted_by_another_user_admin()
    {
        var admin = BuiltInAdmin();
        var users = new FakeAppUserRepository();
        users.Store.Add(admin);
        var handler = new DeleteUserHandler(users, new FakeUserSessionRepository(), new FakeCurrentUser(),
            new FakeClock(Now)); // a different ManageUsers holder

        var act = () => handler.Handle(new DeleteUserCommand(admin.Id.Value), CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>();
        users.Store.Should().Contain(admin);
        admin.IsActive.Should().BeTrue("the built-in admin is neither deleted nor deactivated");
    }

    [Fact]
    public async Task Deleting_a_user_deactivates_it_and_revokes_its_sessions()
    {
        // IAM-009: delete is a soft-delete — the row is retained (audit subject) but the account is deactivated
        // and its live sessions revoked, rather than hard-deleted.
        var user = AppUser.Create("bob", new FakePasswordHasher().Hash("pw12345678"), RoleId.New());
        var users = new FakeAppUserRepository();
        users.Store.Add(user);

        var sessions = new FakeUserSessionRepository();
        var session = UserSession.Issue(UserSessionId.New(), user.Id, "hash", Now, Now.AddHours(10), null, null);
        sessions.Store.Add(session);

        var handler = new DeleteUserHandler(users, sessions, new FakeCurrentUser(), new FakeClock(Now.AddMinutes(5)));

        await handler.Handle(new DeleteUserCommand(user.Id.Value), CancellationToken.None);

        users.Store.Should().Contain(user, "a soft-deleted user is retained for audit history");
        user.IsActive.Should().BeFalse("delete deactivates the account");
        session.IsActive(Now.AddHours(1)).Should().BeFalse("the user's live sessions are revoked");
    }

    private static readonly DateTimeOffset Now = new(2026, 8, 15, 9, 0, 0, TimeSpan.Zero);
}
