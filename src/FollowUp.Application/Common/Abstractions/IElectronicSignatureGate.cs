namespace FollowUp.Application.Common.Abstractions;

/// <summary>
/// Decides whether a record's e-signature requirement is satisfied (SRS FR-11/FR-19). Implemented in
/// Infrastructure: reads the enforcement setting and checks for a valid signature bound to the record's
/// current version/content hash. Keeps the crypto/config out of the application handler while still letting
/// the aggregate enforce the resolve gate.
/// </summary>
public interface IElectronicSignatureGate
{
    /// <summary>True when signatures are enforced for the given module (e.g. complaints).</summary>
    Task<bool> IsEnforcedAsync(string module, CancellationToken ct);

    /// <summary>True when a currently-valid signature is bound to the record at its present state.</summary>
    Task<bool> HasValidSignatureAsync(string module, string recordId, CancellationToken ct);
}
