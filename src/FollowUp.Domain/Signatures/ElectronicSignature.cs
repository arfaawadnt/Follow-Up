using FollowUp.Domain.Common;

namespace FollowUp.Domain.Signatures;

public readonly record struct ElectronicSignatureId(Guid Value)
{
    public static ElectronicSignatureId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}

/// <summary>
/// A tamper-evident electronic signature (SRS FR-19; architect e-signature rules). Binds signer identity
/// and authentication level, intent/meaning, the target record's module + id + <b>version</b>, a timestamp,
/// a declared reason, and a <b>server-computed content hash</b>. A material change to the record produces a
/// new version and therefore requires a new signature: <see cref="StillValidFor"/> reports whether a given
/// current hash/version still matches what was signed.
/// </summary>
public sealed class ElectronicSignature : AggregateRoot<ElectronicSignatureId>
{
    private ElectronicSignature() { } // EF

    private ElectronicSignature(ElectronicSignatureId id, string module, string recordId, uint recordVersion,
        Guid signerUserId, string signerUsername, string authLevel, SignatureMeaning meaning, string? reason,
        string contentHash, DateTimeOffset signedAt, string? signerIp)
        : base(id)
    {
        Module = module;
        RecordId = recordId;
        RecordVersion = recordVersion;
        SignerUserId = signerUserId;
        SignerUsername = signerUsername;
        AuthLevel = authLevel;
        Meaning = meaning;
        Reason = reason;
        ContentHash = contentHash;
        SignedAt = signedAt;
        SignerIp = signerIp;
    }

    public string Module { get; private set; } = null!;
    public string RecordId { get; private set; } = null!;
    public uint RecordVersion { get; private set; }
    public Guid SignerUserId { get; private set; }
    public string SignerUsername { get; private set; } = null!;
    public string AuthLevel { get; private set; } = null!;
    public SignatureMeaning Meaning { get; private set; } = null!;
    public string? Reason { get; private set; }
    public string ContentHash { get; private set; } = null!;
    public DateTimeOffset SignedAt { get; private set; }
    public string? SignerIp { get; private set; }

    public static ElectronicSignature Create(string module, string recordId, uint recordVersion,
        Guid signerUserId, string signerUsername, string authLevel, SignatureMeaning meaning, string? reason,
        string contentHash, DateTimeOffset signedAt, string? signerIp)
    {
        if (!SignableModule.IsKnown(module)) throw new DomainException($"'{module}' is not a signable module."); // SIG-10 closed set
        if (!SignatureAuthLevel.IsKnown(authLevel)) throw new DomainException($"'{authLevel}' is not a known auth level."); // SIG-11 closed set
        if (string.IsNullOrWhiteSpace(recordId)) throw new DomainException("Signature record id is required.");
        if (string.IsNullOrWhiteSpace(contentHash)) throw new DomainException("Signature content hash is required.");
        return new ElectronicSignature(ElectronicSignatureId.New(), module, recordId, recordVersion, signerUserId,
            signerUsername, authLevel, meaning, reason, contentHash, signedAt, signerIp);
    }

    /// <summary>True when the record still hashes to the signed digest at the signed version (tamper check).</summary>
    public bool StillValidFor(string currentContentHash, uint currentVersion) =>
        currentVersion == RecordVersion &&
        string.Equals(currentContentHash, ContentHash, StringComparison.Ordinal);
}
