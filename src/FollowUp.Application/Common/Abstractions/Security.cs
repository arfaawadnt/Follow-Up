using FollowUp.Domain.Identity;

namespace FollowUp.Application.Common.Abstractions;

/// <summary>
/// Password hashing/verification (PBKDF2-SHA256, 100k iterations, per-user salt, constant-time compare —
/// SRS NFR-SEC-3). The crypto lives in Infrastructure; the application only asks for hash/verify.
/// </summary>
public interface IPasswordHasher
{
    PasswordHash Hash(string password);
    bool Verify(string password, PasswordHash hash);
}

/// <summary>Result of issuing a session token.</summary>
public sealed record IssuedToken(string Token, string TokenHash, DateTimeOffset ExpiresAt);

/// <summary>
/// Signed session-token service (compact HMAC-SHA256 payload, ~10h expiry — SRS NFR-SEC-3). Not a JWT and
/// no refresh token. Carries the session id; the payload is validated and the session re-checked per request.
/// </summary>
public interface ITokenService
{
    IssuedToken Issue(AppUserId userId, UserSessionId sessionId, DateTimeOffset issuedAt);

    /// <summary>Validates signature/expiry and returns the embedded session id, or null when invalid.</summary>
    UserSessionId? ReadSessionId(string token);

    /// <summary>Stable hash of a token for storage/lookup (the raw token is never persisted).</summary>
    string HashToken(string token);
}
