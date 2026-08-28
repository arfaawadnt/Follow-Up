using FluentAssertions;
using FollowUp.Application.Common.Exceptions;
using FollowUp.Application.Features.Auth;
using FollowUp.Application.Features.Signatures;
using FollowUp.Application.Tests.Common;
using FollowUp.Domain.Complaints;
using FollowUp.Domain.Identity;
using FollowUp.Domain.Laboratories;

namespace FollowUp.Application.Tests.Features.Auth;

public class LoginHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 15, 8, 0, 0, TimeSpan.Zero);

    private static (FakeAppUserRepository users, FakeRoleRepository roles, AppUser user) Seed(string password)
    {
        var hasher = new FakePasswordHasher();
        var role = Role.Create("Admin", new[] { Privileges.ViewDashboard, Privileges.ManageLabs }, "en", "light", OrgScope.Global);
        var roles = new FakeRoleRepository();
        roles.Store.Add(role);
        var user = AppUser.Create("admin", hasher.Hash(password), role.Id);
        var users = new FakeAppUserRepository();
        users.Store.Add(user);
        return (users, roles, user);
    }

    private static LoginHandler Handler(FakeAppUserRepository users, FakeRoleRepository roles, FakeUserSessionRepository sessions) =>
        new(users, roles, sessions, new FakePasswordHasher(), new FakeTokenService(), new FakeAuthPolicy(), new FakeClock(Now), new FakeFailedLoginRecorder());

    [Fact]
    public async Task Successful_login_returns_token_and_effective_privileges()
    {
        var (users, roles, _) = Seed("secret123");
        var sessions = new FakeUserSessionRepository();

        var result = await Handler(users, roles, sessions)
            .Handle(new LoginCommand("admin", "secret123", "127.0.0.1", "x"), CancellationToken.None);

        result.Token.Should().NotBeNullOrEmpty();
        result.RoleName.Should().Be("Admin");
        result.Privileges.Should().Contain(new[] { Privileges.AddLabs, Privileges.UpdateLabs }); // ManageLabs expanded
        sessions.Store.Should().ContainSingle();
    }

    [Fact]
    public async Task Bad_password_is_rejected_and_counts_toward_lockout()
    {
        var (users, roles, user) = Seed("secret123");
        var sessions = new FakeUserSessionRepository();
        var handler = Handler(users, roles, sessions);

        var act = () => handler.Handle(new LoginCommand("admin", "wrong", null, null), CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedException>();
        user.FailedLoginCount.Should().Be(1);
    }

    [Fact]
    public async Task Locked_account_is_refused()
    {
        var (users, roles, user) = Seed("secret123");
        for (var i = 0; i < 10; i++) user.RegisterFailedLogin(10, TimeSpan.FromMinutes(15), Now);
        var sessions = new FakeUserSessionRepository();

        var act = () => Handler(users, roles, sessions).Handle(new LoginCommand("admin", "secret123", null, null), CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedException>().WithMessage("*locked*");
    }
}

public class SignatureHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 15, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Sign_then_verify_detects_a_later_record_change()
    {
        var hasher = new FakePasswordHasher();
        var users = new FakeAppUserRepository();
        var user = AppUser.Create("signer", hasher.Hash("pw12345678"), RoleId.New());
        users.Store.Add(user);
        var caller = new FakeCurrentUser { UserId = user.Id, Username = "signer" };
        var sigs = new FakeElectronicSignatureRepository();
        var recordHasher = new FakeRecordHasher { Hash = "HASH-1", Version = 1 };

        // A real in-scope complaint so the verify handler's record-scope guard (SIG-3) is satisfied.
        var lab = Laboratory.Register(LabCode.Create("MGL-SIG"), "Lab", "B");
        var complaint = Complaint.Log(1, lab.Id, "Result Quality", "Phone", null, "details");
        var labs = new FakeLaboratoryRepository();
        labs.Add(lab);
        var complaints = new FakeComplaintRepository();
        complaints.Add(complaint);
        var recordId = complaint.Id.Value.ToString();

        var sign = new SignRecordHandler(sigs, users, hasher, recordHasher, caller, new FakeClock(Now), complaints, labs);
        await sign.Handle(new SignRecordCommand("complaint", recordId, "Approval", "ok", "pw12345678"), CancellationToken.None);
        sigs.Store.Should().ContainSingle();

        var verify = new VerifySignatureHandler(sigs, recordHasher, caller, complaints, labs);
        (await verify.Handle(new VerifySignatureQuery("complaint", recordId), CancellationToken.None))
            .StillValid.Should().BeTrue();

        // Simulate a material record change -> signature no longer valid.
        recordHasher.Hash = "HASH-2"; recordHasher.Version = 2;
        var after = await verify.Handle(new VerifySignatureQuery("complaint", recordId), CancellationToken.None);
        after.Signed.Should().BeTrue();
        after.StillValid.Should().BeFalse();
    }

    [Fact]
    public async Task Sign_requires_correct_password()
    {
        var hasher = new FakePasswordHasher();
        var users = new FakeAppUserRepository();
        var user = AppUser.Create("signer", hasher.Hash("correct-pw"), RoleId.New());
        users.Store.Add(user);
        var caller = new FakeCurrentUser { UserId = user.Id };
        var sign = new SignRecordHandler(new FakeElectronicSignatureRepository(), users, hasher,
            new FakeRecordHasher(), caller, new FakeClock(Now), new FakeComplaintRepository(), new FakeLaboratoryRepository());

        var act = () => sign.Handle(new SignRecordCommand("complaint", "c-1", "Approval", null, "wrong-pw"), CancellationToken.None);
        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task Sign_is_refused_when_the_record_is_outside_the_signers_scope()
    {
        var hasher = new FakePasswordHasher();
        var users = new FakeAppUserRepository();
        var user = AppUser.Create("signer", hasher.Hash("pw12345678"), RoleId.New());
        users.Store.Add(user);
        var lab = Laboratory.Register(LabCode.Create("MGL-OOS"), "Lab", "B");
        var complaint = Complaint.Log(1, lab.Id, "Result Quality", "Phone", null, "details");
        var labs = new FakeLaboratoryRepository();
        labs.Add(lab);
        var complaints = new FakeComplaintRepository();
        complaints.Add(complaint);
        var recordId = complaint.Id.Value.ToString();

        // Signer scoped to Giza; the lab is not in that scope (SRS FR-19: sign is within scope, finding SIG-2).
        var giza = OrgScope.Create(
            new[] { "*" }, new[] { "Giza" }, new[] { "*" }, new[] { "*" }, new[] { "*" }, new[] { "*" });
        var caller = new FakeCurrentUser { UserId = user.Id, Username = "signer", Scope = giza };
        var sign = new SignRecordHandler(new FakeElectronicSignatureRepository(), users, hasher,
            new FakeRecordHasher(), caller, new FakeClock(Now), complaints, labs);

        // Correct password (re-auth passes) but out of scope -> refused.
        var act = () => sign.Handle(
            new SignRecordCommand("complaint", recordId, "Approval", null, "pw12345678"), CancellationToken.None);
        await act.Should().ThrowAsync<ForbiddenException>();
    }
}
