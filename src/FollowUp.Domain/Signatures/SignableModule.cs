namespace FollowUp.Domain.Signatures;

/// <summary>
/// The closed set of record types that can carry an electronic signature (SIG-10). "module" was a free string
/// validated only for non-emptiness, with the sole valid value ("complaint") scattered as a literal. Centralising
/// the known modules here makes this the single authority, so the signature factory and the sign/verify
/// validators can reject an unknown module up front rather than only failing later at scope resolution.
/// </summary>
public static class SignableModule
{
    /// <summary>A complaint record (SRS FR-19 resolution e-signature).</summary>
    public const string Complaint = "complaint";

    public static readonly IReadOnlyCollection<string> All = new[] { Complaint };

    public static bool IsKnown(string? module) =>
        module is not null && All.Any(m => string.Equals(m, module, StringComparison.OrdinalIgnoreCase));
}
