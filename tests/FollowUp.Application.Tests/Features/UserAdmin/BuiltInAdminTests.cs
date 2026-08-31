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
        var handler = new DeleteUserHandler(users, new FakeCurrentUser()); // a different ManageUsers holder

        var act = () => handler.Handle(new DeleteUserCommand(admin.Id.Value), CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>();
        users.Store.Should().Contain(admin);
    }
}
