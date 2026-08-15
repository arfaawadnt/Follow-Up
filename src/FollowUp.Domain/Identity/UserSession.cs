using FollowUp.Domain.Common;

namespace FollowUp.Domain.Identity;

public readonly record struct UserSessionId(Guid Value)
{
    public static UserSessionId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}

/// <summary>
/// A server-side session row (SRS FR-1). The HMAC bearer token carries the session id; revocation and
/// last-seen are checked/updated from this row on every request, so a logout invalidates the token on the
/// very next call. The token itself is never stored — only a hash, for defence in depth.
/// </summary>
public sealed class UserSession : AggregateRoot<UserSessionId>
{
    private UserSession() { } // EF

    private UserSession(UserSessionId id, AppUserId userId, string tokenHash,
        DateTimeOffset issuedAt, DateTimeOffset expiresAt, string? ip, string? userAgent)
        : base(id)
    {
        UserId = userId;
        TokenHash = tokenHash;
        IssuedAt = issuedAt;
        ExpiresAt = expiresAt;
        LastSeenAt = issuedAt;
        Ip = ip;
        UserAgent = userAgent;
    }

    public AppUserId UserId { get; private set; }
    public string TokenHash { get; private set; } = null!;
    public DateTimeOffset IssuedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset LastSeenAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public string? Ip { get; private set; }
    public string? UserAgent { get; private set; }

    public static UserSession Issue(AppUserId userId, string tokenHash, DateTimeOffset issuedAt,
        DateTimeOffset expiresAt, string? ip, string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(tokenHash))
            throw new DomainException("Session token hash is required.");
        if (expiresAt <= issuedAt)
            throw new DomainException("Session expiry must be after issuance.");
        return new UserSession(UserSessionId.New(), userId, tokenHash, issuedAt, expiresAt, ip, userAgent);
    }

    public bool IsActive(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now;

    public void Touch(DateTimeOffset now)
    {
        if (now > LastSeenAt) LastSeenAt = now;
    }

    public void Revoke(DateTimeOffset now) => RevokedAt ??= now;
}
