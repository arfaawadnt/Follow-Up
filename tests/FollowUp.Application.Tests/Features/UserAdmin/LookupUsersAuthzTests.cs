using FluentAssertions;
using FollowUp.Application.Features.UserAdmin.Queries;
using FollowUp.Domain.Identity;

namespace FollowUp.Application.Tests.Features.UserAdmin;

/// <summary>
/// IDN-9: username enumeration via LookupUsersQuery was open to any authenticated caller. It is now gated behind
/// its consumers' privileges (ANY-of), so a caller holding none of them is refused by the authorization behavior.
/// </summary>
public class LookupUsersAuthzTests
{
    [Fact]
    public void The_username_lookup_is_no_longer_open_to_any_authenticated_caller()
    {
        var required = new LookupUsersQuery().RequiredPrivileges;

        required.Should().NotBeEmpty();
        required.Should().Contain(Privileges.SampleTracking).And.Contain(Privileges.ManageUsers);
    }
}
