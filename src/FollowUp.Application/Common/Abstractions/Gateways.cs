namespace FollowUp.Application.Common.Abstractions;

/// <summary>A file attached to an outbound email (e.g. an .xlsx export of the report data).</summary>
public sealed record EmailAttachment(string FileName, byte[] Content, string ContentType = "application/octet-stream");

/// <summary>Outbound email (SMTP). HTML bodies must have their variables escaped by the sender (JOBS-003).</summary>
public interface IEmailSender
{
    Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken ct);

    /// <summary>Sends an HTML email with file attachments (used by the daily statistics reports).</summary>
    Task SendAsync(string toEmail, string subject, string htmlBody, IReadOnlyList<EmailAttachment> attachments, CancellationToken ct);
}

/// <summary>Outbound WhatsApp template message via the Meta Cloud API (SRS External Interfaces).</summary>
public interface IWhatsAppSender
{
    Task SendAsync(string toPhone, string templateName, IReadOnlyList<string> parameters, CancellationToken ct);
}

/// <summary>A row returned by an allow-listed Oracle read query.</summary>
public sealed record OracleRow(IReadOnlyDictionary<string, object?> Values);

/// <summary>A half-open date window bound to a query's <c>:from_date</c>/<c>:to_date</c> parameters
/// (<c>reg_date &gt;= FromDate AND reg_date &lt; ToExclusive</c>).</summary>
public sealed record OracleDateWindow(DateTime FromDate, DateTime ToExclusive);

/// <summary>
/// Read-only Oracle access (SRS FR-17). Executes only allow-listed, fingerprint-validated SELECTs; the
/// connection string is config-managed and never surfaced. Implemented in Infrastructure.
/// </summary>
public interface IOracleReader
{
    /// <summary>Runs the feed with its default (env-configured) rolling window.</summary>
    Task<IReadOnlyList<OracleRow>> ExecuteAsync(string queryName, CancellationToken ct);

    /// <summary>Runs the feed over an explicit date window (used by the date-scoped test-statistics sync).</summary>
    Task<IReadOnlyList<OracleRow>> ExecuteAsync(string queryName, OracleDateWindow window, CancellationToken ct);
}

/// <summary>
/// Resolves a Google-Maps short link to its redirect target with an SSRF guard (host allow-list, no
/// auto-follow, 5s timeout — SRS FR-21). Implemented in Infrastructure.
/// </summary>
public interface IMapLinkResolver
{
    Task<string?> ResolveAsync(string shortUrl, CancellationToken ct);
}
