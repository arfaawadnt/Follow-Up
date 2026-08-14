using System.Security.Cryptography;
using System.Text;
using FollowUp.Domain.Common;

namespace FollowUp.Domain.Laboratories;

/// <summary>
/// A laboratory's business code (e.g. <c>MGL-0042</c>). Unique case-insensitively (BR-1); the
/// normalized upper-case form is what the unique index and equality use.
/// </summary>
public sealed class LabCode : ValueObject
{
    public string Value { get; }

    private LabCode(string value) => Value = value;

    public static LabCode Create(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            throw new DomainException("Laboratory code is required.");

        var normalized = raw.Trim().ToUpperInvariant();
        if (normalized.Length > 32)
            throw new DomainException("Laboratory code must be 32 characters or fewer.");

        return new LabCode(normalized);
    }

    /// <summary>
    /// The deterministic <c>ENC-XXXX-XXXX</c> display alias shown to callers lacking
    /// <c>ShowEncryptedLabs</c> (BR-7). Derived purely from the code, so it is stable across requests.
    /// </summary>
    public string ToEncryptedAlias()
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(Value));
        // 8 hex chars split into two 4-char groups — deterministic, non-reversible.
        var hex = Convert.ToHexString(hash);
        return $"ENC-{hex[..4]}-{hex[4..8]}";
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;
}
