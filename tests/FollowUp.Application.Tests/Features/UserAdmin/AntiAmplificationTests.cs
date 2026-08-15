using FluentAssertions;
using FollowUp.Application.Common.Exceptions;
using FollowUp.Application.Features.UserAdmin.Roles;
using FollowUp.Application.Features.UserAdmin.Users;
using FollowUp.Application.Tests.Common;
using FollowUp.Domain.Identity;

namespace FollowUp.Application.Tests.Features.UserAdmin;

public class AntiAmplificationTests
{
    private static FakeCurrentUser CallerWith(params string[] privileges) => new()
    {
        Privileges = new HashSet<string>(privileges),
        Scope = OrgScope.Global,
    };

    [Fact]
    public async Task Cannot_create_role_granting_a_privilege_the_caller_lacks()
    {
        var roles = new FakeRoleRepository();
        // Caller can manage users but does NOT hold ManageLoyalty.
        var caller = CallerWith(Privileges.ManageUsers);
        var handler = new CreateRoleHandler(roles, caller);

        var cmd = new CreateRoleCommand
        {
            Name = "Finance",
            Privileges = new[] { Domain.Identity.Privileges.ManageLoyalty },
            Scope = ScopeInput.Empty with { Branches = new[] { "*" }, Governorates = new[] { "*" }, Cities = new[] { "*" }, Areas = new[] { "*" }, Categories = new[] { "*" }, Segments = new[] { "*" } },
        };

        var act = () => handler.Handle(cmd, CancellationToken.None);
        await act.Should().ThrowAsync<ForbiddenException>().WithMessage("*privileges you do not hold*");
    }

    [Fact]
    public async Task Cannot_grant_scope_broader_than_own()
    {
        var roles = new FakeRoleRepository();
        // Caller scoped to Cairo only.
        var caller = new FakeCurrentUser
        {
            Privileges = new HashSet<string> { Privileges.ManageUsers, Privileges.ManageLabs, Privileges.AddLabs, Privileges.UpdateLabs, Privileges.ViewLabLocation },
            Scope = OrgScope.Create(new[] { "*" }, new[] { "Cairo" }, new[] { "*" }, new[] { "*" }, new[] { "*" }, new[] { "*" }),
        };
        var handler = new CreateRoleHandler(roles, caller);

        var cmd = new CreateRoleCommand
        {
            Name = "Wide",
            Privileges = Array.Empty<string>(),
            Scope = ScopeInput.Empty with { Branches = new[] { "*" }, Governorates = new[] { "*" }, Cities = new[] { "*" }, Areas = new[] { "*" }, Categories = new[] { "*" }, Segments = new[] { "*" } },
        };

        var act = () => handler.Handle(cmd, CancellationToken.None);
        await act.Should().ThrowAsync<ForbiddenException>().WithMessage("*broader than your own*");
    }

    [Fact]
    public async Task Cannot_change_own_role()
    {
        var roles = new FakeRoleRepository();
        var users = new FakeAppUserRepository();
        var role = Role.Create("Ops", new[] { Privileges.ViewDashboard }, "en", "light", OrgScope.Global);
        roles.Store.Add(role);
        var newRole = Role.Create("Admin2", new[] { Privileges.ViewDashboard }, "en", "light", OrgScope.Global);
        roles.Store.Add(newRole);

        var user = AppUser.Create("me", new PasswordHash("F", 1, "c2E=", "aA=="), role.Id);
        users.Store.Add(user);

        var caller = new FakeCurrentUser { UserId = user.Id, RoleId = role.Id, Privileges = new HashSet<string> { Privileges.ManageUsers, Privileges.ViewDashboard } };
        var handler = new UpdateUserHandler(users, roles, caller);

        var act = () => handler.Handle(new UpdateUserCommand { Id = user.Id.Value, RoleId = newRole.Id.Value, Language = "en" }, CancellationToken.None);
        await act.Should().ThrowAsync<ForbiddenException>().WithMessage("*your own role*");
    }

    [Fact]
    public async Task Can_create_role_within_grant()
    {
        var roles = new FakeRoleRepository();
        var caller = CallerWith(Privileges.ManageUsers, Privileges.ViewDashboard);
        var handler = new CreateRoleHandler(roles, caller);

        var cmd = new CreateRoleCommand
        {
            Name = "Viewer",
            Privileges = new[] { Domain.Identity.Privileges.ViewDashboard },
            Scope = ScopeInput.Empty with { Branches = new[] { "*" }, Governorates = new[] { "*" }, Cities = new[] { "*" }, Areas = new[] { "*" }, Categories = new[] { "*" }, Segments = new[] { "*" } },
        };

        var id = await handler.Handle(cmd, CancellationToken.None);
        id.Should().NotBeEmpty();
        roles.Store.Should().ContainSingle();
    }
}
