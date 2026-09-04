using System.Net;
using System.Net.Mail;
using System.Text;
using System.Text.Json;
using FollowUp.Application.Common.Abstractions;
using FollowUp.Application.Common.Abstractions.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FollowUp.Infrastructure.Gateways;

/// <summary>
/// SMTP email sender (SRS External Interfaces). Prefers the in-app <c>SmtpConfig</c> (operator-editable through
/// the Mail Gateway screen); when no DB row exists it falls back to the legacy <c>Smtp</c> config section so
/// existing env-var deployments keep working. HTML bodies must be escaped by the caller (JOBS-003). When SMTP is
/// unconfigured/disabled it logs and no-ops so a dev environment without a mail server still runs.
/// </summary>
public sealed class SmtpEmailSender : IEmailSender
{
    private readonly ISmtpConfigRepository _configRepo;
    private readonly IConfiguration _config;
    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(ISmtpConfigRepository configRepo, IConfiguration config, ILogger<SmtpEmailSender> logger)
    {
        _configRepo = configRepo;
        _config = config;
        _logger = logger;
    }

    public Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken ct) =>
        SendAsync(toEmail, subject, htmlBody, System.Array.Empty<EmailAttachment>(), ct);

    public async Task SendAsync(string toEmail, string subject, string htmlBody, IReadOnlyList<EmailAttachment> attachments, CancellationToken ct)
    {
        var s = await ResolveAsync(ct);
        if (string.IsNullOrWhiteSpace(s.Host))
        {
            _logger.LogWarning("SMTP not configured; skipping email to {To}", toEmail);
            return;
        }

        using var message = new MailMessage(string.IsNullOrWhiteSpace(s.From) ? "noreply@megalab.local" : s.From, toEmail)
        {
            Subject = subject,
            Body = htmlBody,
            IsBodyHtml = true,
            BodyEncoding = Encoding.UTF8,
            SubjectEncoding = Encoding.UTF8,
        };
        var streams = new List<MemoryStream>();
        foreach (var a in attachments ?? System.Array.Empty<EmailAttachment>())
        {
            var stream = new MemoryStream(a.Content);
            streams.Add(stream);
            message.Attachments.Add(new Attachment(stream, a.FileName, a.ContentType));
        }
        try
        {
            using var client = new SmtpClient(s.Host, s.Port) { EnableSsl = s.UseSsl };
            if (!string.IsNullOrWhiteSpace(s.User))
                client.Credentials = new NetworkCredential(s.User, s.Password);

            await client.SendMailAsync(message, ct);
        }
        finally
        {
            foreach (var stream in streams) stream.Dispose();
        }
    }

    private async Task<(string? Host, int Port, string? From, bool UseSsl, string? User, string? Password)> ResolveAsync(CancellationToken ct)
    {
        var db = await _configRepo.GetAsync(ct);
        if (db is not null) // an in-app config exists — it is authoritative (disabled/blank means don't send)
            return db.Enabled && !string.IsNullOrWhiteSpace(db.Host)
                ? (db.Host, db.Port, db.FromAddress, db.UseSsl, db.User, db.Password)
                : (null, 587, null, true, null, null);

        var sec = _config.GetSection("Smtp"); // legacy env/appsettings fallback
        return (sec["Host"], int.TryParse(sec["Port"], out var p) ? p : 587, sec["From"],
            !bool.TryParse(sec["UseSsl"], out var ssl) || ssl, sec["User"], sec["Password"]);
    }
}

/// <summary>
/// WhatsApp template sender via the Meta Cloud API (SRS External Interfaces). Reads the <c>WhatsApp</c> config
/// section (Token, PhoneNumberId, ApiBase). No-ops with a log when unconfigured.
/// </summary>
public sealed class WhatsAppSender : IWhatsAppSender
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<WhatsAppSender> _logger;

    public WhatsAppSender(IHttpClientFactory httpFactory, IConfiguration config, ILogger<WhatsAppSender> logger)
    {
        _httpFactory = httpFactory;
        _config = config;
        _logger = logger;
    }

    public async Task SendAsync(string toPhone, string templateName, IReadOnlyList<string> parameters, CancellationToken ct)
    {
        var section = _config.GetSection("WhatsApp");
        var token = section["Token"];
        var phoneNumberId = section["PhoneNumberId"];
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(phoneNumberId))
        {
            _logger.LogWarning("WhatsApp not configured; skipping message to {To}", toPhone);
            return;
        }

        var apiBase = section["ApiBase"] ?? "https://graph.facebook.com/v20.0";
        var payload = new
        {
            messaging_product = "whatsapp",
            to = toPhone,
            type = "template",
            template = new
            {
                name = templateName,
                language = new { code = "en" },
                components = new[]
                {
                    new { type = "body", parameters = parameters.Select(p => new { type = "text", text = p }).ToArray() },
                },
            },
        };

        using var client = _httpFactory.CreateClient("whatsapp");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{apiBase}/{phoneNumberId}/messages")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var response = await client.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }
}
