namespace FollowUp.Application.Common.Abstractions;

/// <summary>
/// Login-protection policy (SRS FR-1/NFR-SEC-4): account-lockout thresholds, configurable rather than
/// hardcoded. Provided by Infrastructure from settings (defaults: 10 failures / 15 minutes).
/// </summary>
public interface IAuthPolicy
{
    int MaxFailedAttempts { get; }
    TimeSpan LockoutWindow { get; }
    TimeSpan TokenLifetime { get; }
}

/// <summary>
/// Computes the tamper-evident content hash and current version of a signable record (SRS FR-19).
/// Implemented per signable module in Infrastructure; keeps record serialization out of the application.
/// </summary>
public interface IRecordHasher
{
    /// <summary>Returns the record's canonical content hash and row-version, or null when it does not exist.</summary>
    Task<(string ContentHash, uint Version)?> ComputeAsync(string module, string recordId, CancellationToken ct);
}
