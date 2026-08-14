using FluentAssertions;
using FollowUp.Domain.Identity;
using FollowUp.Domain.Laboratories;

namespace FollowUp.Domain.Tests.Identity;

public class OrgScopeTests
{
    [Fact]
    public void Global_allows_any_record()
    {
        OrgScope.Global.Allows("HubA", "Cairo", "Nasr", "Zone1", "Gold", "A").Should().BeTrue();
    }

    [Fact]
    public void Empty_dimension_denies_all_fail_closed()
    {
        OrgScope.Deny.Allows("HubA", "Cairo", "Nasr", "Zone1", "Gold", "A").Should().BeFalse();
    }

    [Fact]
    public void Specific_scope_matches_only_listed_values()
    {
        var scope = OrgScope.Create(
            branches: new[] { "*" }, governorates: new[] { "Cairo" }, cities: new[] { "*" },
            areas: new[] { "*" }, categories: new[] { "*" }, segments: new[] { "*" });

        scope.Allows("HubA", "Cairo", "Nasr", "Z1", "Gold", "A").Should().BeTrue();
        scope.Allows("HubA", "Giza", "Dokki", "Z2", "Gold", "A").Should().BeFalse();
    }

    [Fact]
    public void IsWithin_enforces_anti_amplification()
    {
        var narrow = OrgScope.Create(new[] { "*" }, new[] { "Cairo" }, new[] { "*" },
            new[] { "*" }, new[] { "*" }, new[] { "*" });

        narrow.IsWithin(OrgScope.Global).Should().BeTrue();          // narrower ⊆ global
        OrgScope.Global.IsWithin(narrow).Should().BeFalse();          // global ⊄ narrow
    }
}

public class PrivilegeExpansionTests
{
    [Fact]
    public void Manage_expands_to_fine_grained_leaves()
    {
        var effective = Privileges.Expand(new[] { Privileges.ManageLabs });

        effective.Should().Contain(new[] { Privileges.AddLabs, Privileges.UpdateLabs, Privileges.ViewLabLocation });
    }

    [Fact]
    public void ViewReports_cross_grants_stats_reads()
    {
        var effective = Privileges.Expand(new[] { Privileges.ViewReports });

        effective.Should().Contain(new[] { Privileges.ViewLabStats, Privileges.ViewTeststats });
    }
}

public class LabCodeTests
{
    [Fact]
    public void Code_is_normalized_upper_case()
    {
        LabCode.Create(" mgl-0042 ").Value.Should().Be("MGL-0042");
    }

    [Fact]
    public void Enc_alias_is_deterministic_and_formatted()
    {
        var a = LabCode.Create("MGL-0042").ToEncryptedAlias();
        var b = LabCode.Create("mgl-0042").ToEncryptedAlias();

        a.Should().Be(b);
        a.Should().MatchRegex(@"^ENC-[0-9A-F]{4}-[0-9A-F]{4}$");
    }
}

public class AppUserLockoutTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 15, 8, 0, 0, TimeSpan.FromHours(2));

    private static AppUser NewUser() =>
        AppUser.Create("jdoe", new PasswordHash("PBKDF2-SHA256", 100_000, "c2FsdA==", "aGFzaA=="), RoleId.New());

    [Fact]
    public void Account_locks_after_max_attempts()
    {
        var user = NewUser();
        for (var i = 0; i < 10; i++)
            user.RegisterFailedLogin(10, TimeSpan.FromMinutes(15), Now);

        user.IsLockedOut(Now).Should().BeTrue();
        user.IsLockedOut(Now.AddMinutes(16)).Should().BeFalse();
    }

    [Fact]
    public void Successful_login_clears_failures()
    {
        var user = NewUser();
        user.RegisterFailedLogin(10, TimeSpan.FromMinutes(15), Now);
        user.RegisterSuccessfulLogin();

        user.FailedLoginCount.Should().Be(0);
        user.IsLockedOut(Now).Should().BeFalse();
    }
}
