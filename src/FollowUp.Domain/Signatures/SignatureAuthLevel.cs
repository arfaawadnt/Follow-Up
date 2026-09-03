namespace FollowUp.Domain.Signatures;

/// <summary>
/// The authentication assurance recorded with a signature (standard element 313, finding SIG-11). The signing
/// ceremony re-authenticates with a password, so <see cref="Password"/> is the only level produced today. The
/// closed set exists so the recorded value is a defined constant rather than a magic string, and so stronger
/// levels (e.g. MFA / step-up) can be added the moment the auth context can attest to them. (Named
/// SignatureAuthLevel to avoid colliding with ElectronicSignature.AuthLevel.)
/// </summary>
public static class SignatureAuthLevel
{
    public const string Password = "password";

    public static readonly IReadOnlyCollection<string> All = new[] { Password };

    public static bool IsKnown(string? level) =>
        level is not null && All.Any(l => string.Equals(l, level, StringComparison.OrdinalIgnoreCase));
}
