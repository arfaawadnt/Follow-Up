using FollowUp.Domain.Common;

namespace FollowUp.Domain.Reference;

/// <summary>
/// A key/value application setting (SRS FR-2). Secret-bearing settings are flagged so Infrastructure can
/// redact them on read and mask them on write — the domain simply records whether a key is secret.
/// The key is the aggregate identity.
/// </summary>
public sealed class AppSetting : AggregateRoot<string>, IAuditable
{
    private AppSetting() { } // EF

    private AppSetting(string key, string? value, bool isSecret) : base(key)
    {
        Value = value;
        IsSecret = isSecret;
    }

    public string Key => Id;
    public string? Value { get; private set; }
    public bool IsSecret { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public string CreatedBy { get; private set; } = null!;
    public DateTimeOffset? UpdatedAt { get; private set; }
    public string? UpdatedBy { get; private set; }

    public static AppSetting Create(string key, string? value, bool isSecret)
    {
        if (string.IsNullOrWhiteSpace(key)) throw new DomainException("Setting key is required.");
        return new AppSetting(key.Trim(), value, isSecret);
    }

    public void SetValue(string? value) => Value = value;
}
