using FluentAssertions;
using FollowUp.Infrastructure.Security;

namespace FollowUp.IntegrationTests;

/// <summary>
/// Finding M-15/M-16: the at-rest secret encryptor round-trips values, marks ciphertext with its version
/// prefix, and reads a legacy plaintext value transparently. Uses the same master secret the integration
/// fixture configures, so the process-wide protector key stays consistent for the DB-backed tests.
/// </summary>
public class SecretProtectorTests
{
    // Matches IntegrationFixture's Auth:SigningSecret, which AddInfrastructure uses to configure SecretProtector,
    // so this test leaves the process-wide protector key exactly as the DB-backed tests expect.
    private const string FixtureSecret = "integration-test-signing-secret-value-0123456789";

    [Fact]
    public void Encrypts_and_round_trips_without_exposing_the_plaintext()
    {
        SecretProtector.Configure(FixtureSecret);
        const string secret = "Host=oracle;User Id=svc;Password=hunter2";

        var enc = SecretProtector.Protect(secret);

        enc.Should().NotBeNull();
        enc.Should().NotBe(secret);
        enc!.Should().NotContain("hunter2");
        SecretProtector.IsProtected(enc).Should().BeTrue();
        SecretProtector.Unprotect(enc).Should().Be(secret);
    }

    [Fact]
    public void Reads_legacy_plaintext_transparently_and_does_not_double_encrypt()
    {
        SecretProtector.Configure(FixtureSecret);

        SecretProtector.IsProtected("Host=legacy;Password=old").Should().BeFalse();
        SecretProtector.Unprotect("Host=legacy;Password=old").Should().Be("Host=legacy;Password=old");

        var once = SecretProtector.Protect("secret");
        SecretProtector.Protect(once).Should().Be(once, "already-encrypted values are not re-encrypted");
    }
}
