using System.Security.Cryptography;
using System.Text;
using FollowUp.Application.Common.Abstractions;
using FollowUp.Domain.Identity;

namespace FollowUp.Infrastructure.Security;

/// <summary>
/// Compact signed session token (SRS NFR-SEC-3): a base64url payload of user/session/expiry, signed with
/// HMAC-SHA256 using a secret from configuration. Not a JWT and no refresh token; ~10h lifetime. The raw
/// token is never stored — only its SHA-256 hash (for session lookup/revocation).
/// </summary>
public sealed class HmacTokenService : ITokenService
{
    private readonly byte[] _key;
    private readonly TimeSpan _lifetime;

    public HmacTokenService(AuthOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.SigningSecret))
            throw new InvalidOperationException("Auth:SigningSecret is not configured (fail-fast, NFR-SEC-3).");
        _key = Encoding.UTF8.GetBytes(options.SigningSecret);
        _lifetime = TimeSpan.FromHours(options.TokenLifetimeHours);
    }

    public IssuedToken Issue(AppUserId userId, UserSessionId sessionId, DateTimeOffset issuedAt)
    {
        var expiresAt = issuedAt.Add(_lifetime);
        var payload = $"{userId.Value:N}.{sessionId.Value:N}.{expiresAt.ToUnixTimeSeconds()}";
        var payloadB64 = Base64Url(Encoding.UTF8.GetBytes(payload));
        var sigB64 = Base64Url(Sign(payloadB64));
        var token = $"{payloadB64}.{sigB64}";
        return new IssuedToken(token, HashToken(token), expiresAt);
    }

    public UserSessionId? ReadSessionId(string token)
    {
        var parts = token.Split('.');
        if (parts.Length != 2) return null;

        var expectedSig = Base64Url(Sign(parts[0]));
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expectedSig), Encoding.UTF8.GetBytes(parts[1])))
            return null;

        string payload;
        try { payload = Encoding.UTF8.GetString(FromBase64Url(parts[0])); }
        catch { return null; }

        var fields = payload.Split('.');
        if (fields.Length != 3) return null;
        if (!long.TryParse(fields[2], out var expUnix)) return null;
        if (DateTimeOffset.FromUnixTimeSeconds(expUnix) <= DateTimeOffset.UtcNow) return null;
        if (!Guid.TryParseExact(fields[1], "N", out var sessionGuid)) return null;

        return new UserSessionId(sessionGuid);
    }

    public string HashToken(string token) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private byte[] Sign(string data)
    {
        using var hmac = new HMACSHA256(_key);
        return hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string s)
    {
        var padded = s.Replace('-', '+').Replace('_', '/');
        padded = (padded.Length % 4) switch { 2 => padded + "==", 3 => padded + "=", _ => padded };
        return Convert.FromBase64String(padded);
    }
}
