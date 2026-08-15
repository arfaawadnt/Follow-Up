namespace FollowUp.Application.Common.Abstractions;

/// <summary>Outbound email (SMTP). HTML bodies must have their variables escaped by the sender (JOBS-003).</summary>
public interface IEmailSender
{
    Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken ct);
}

/// <summary>Outbound WhatsApp template message via the Meta Cloud API (SRS External Interfaces).</summary>
public interface IWhatsAppSender
{
    Task SendAsync(string toPhone, string templateName, IReadOnlyList<string> parameters, CancellationToken ct);
}

/// <summary>A row returned by an allow-listed Oracle read query.</summary>
public sealed record OracleRow(IReadOnlyDictionary<string, object?> Values);

/// <summary>
/// Read-only Oracle access (SRS FR-17). Executes only allow-listed, fingerprint-validated SELECTs; the
/// connection string is config-managed and never surfaced. Implemented in Infrastructure.
/// </summary>
public interface IOracleReader
{
    Task<IReadOnlyList<OracleRow>> ExecuteAsync(string queryName, CancellationToken ct);
}

/// <summary>
/// Resolves a Google-Maps short link to its redirect target with an SSRF guard (host allow-list, no
/// auto-follow, 5s timeout — SRS FR-21). Implemented in Infrastructure.
/// </summary>
public interface IMapLinkResolver
{
    Task<string?> ResolveAsync(string shortUrl, CancellationToken ct);
}
