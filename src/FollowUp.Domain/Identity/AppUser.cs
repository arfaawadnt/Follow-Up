using FollowUp.Domain.Common;
using FollowUp.Domain.Representatives;

namespace FollowUp.Domain.Identity;

public readonly record struct AppUserId(Guid Value)
{
    public static AppUserId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}

/// <summary>
/// A login account (SRS FR-1/FR-2). Holds credentials, role membership, preferences, an optional link to
/// a <see cref="Representative"/> (one login per rep), and per-account lockout state. Passwords are never
/// held in clear — only a <see cref="PasswordHash"/>. Session lifecycle lives in <c>UserSession</c>.
/// </summary>
public sealed class AppUser : AggregateRoot<AppUserId>, IAuditable
{
    private AppUser() { } // EF

    private AppUser(AppUserId id, string username, PasswordHash password, RoleId roleId)
        : base(id)
    {
        Username = username;
        Password = password;
        RoleId = roleId;
        Language = "en";
        IsActive = true;
    }

    public string Username { get; private set; } = null!;
    public PasswordHash Password { get; private set; } = null!;
    public RoleId RoleId { get; private set; }
    public string? Email { get; private set; }
    public string? Phone { get; private set; }
    public string Language { get; private set; } = "en";
    public RepresentativeId? RepresentativeId { get; private set; }
    public bool IsActive { get; private set; }

    // Lockout state (SRS FR-1 / NFR-SEC-4).
    public int FailedLoginCount { get; private set; }
    public DateTimeOffset? LockedUntil { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public string CreatedBy { get; private set; } = null!;
    public DateTimeOffset? UpdatedAt { get; private set; }
    public string? UpdatedBy { get; private set; }

    public static AppUser Create(string username, PasswordHash password, RoleId roleId)
    {
        if (string.IsNullOrWhiteSpace(username))
            throw new DomainException("Username is required.");
        return new AppUser(AppUserId.New(), username.Trim(), password, roleId);
    }

    public void SetProfile(string? email, string? phone) { Email = email; Phone = phone; }
    public void SetLanguage(string language) => Language = string.IsNullOrWhiteSpace(language) ? "en" : language.Trim();
    public void ChangeRole(RoleId roleId) => RoleId = roleId;
    public void LinkRepresentative(RepresentativeId? repId) => RepresentativeId = repId;
    public void SetPassword(PasswordHash password) => Password = password;
    public void Deactivate() => IsActive = false;
    public void Reactivate() => IsActive = true;

    public bool IsLockedOut(DateTimeOffset now) => LockedUntil is { } until && until > now;

    /// <summary>
    /// Records a failed login; once <paramref name="maxAttempts"/> is reached the account is locked for
    /// <paramref name="lockoutWindow"/> (SRS FR-1: default 10 fails / 15 min, configurable).
    /// </summary>
    public void RegisterFailedLogin(int maxAttempts, TimeSpan lockoutWindow, DateTimeOffset now)
    {
        FailedLoginCount++;
        if (FailedLoginCount >= maxAttempts)
            LockedUntil = now.Add(lockoutWindow);
    }

    /// <summary>Clears failure/lockout state after a successful authentication.</summary>
    public void RegisterSuccessfulLogin()
    {
        FailedLoginCount = 0;
        LockedUntil = null;
    }

    /// <summary>Administrative unlock (SRS FR-1 acceptance b).</summary>
    public void Unlock()
    {
        FailedLoginCount = 0;
        LockedUntil = null;
    }
}
