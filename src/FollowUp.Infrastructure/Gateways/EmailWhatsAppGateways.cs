using System.Net;
using System.Net.Mail;
using System.Text;
using System.Text.Json;
using FollowUp.Application.Common.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FollowUp.Infrastructure.Gateways;

/// <summary>
/// SMTP email sender (SRS External Interfaces). Reads the <c>Smtp</c> config section. HTML bodies must be
/// escaped by the caller (JOBS-003); this sender does not build markup. When SMTP is unconfigured it logs
/// and no-ops so a dev environment without a mail server still runs.
/// </summary>
public sealed class SmtpEmailSender : IEmailSender
{
    private readonly IConfiguration _config;
    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(IConfiguration config, ILogger<SmtpEmailSender> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken ct)
    {
        var section = _config.GetSection("Smtp");
        var host = section["Host"];
        if (string.IsNullOrWhiteSpace(host))
        {
            _logger.LogWarning("SMTP not configured; skipping email to {To}", toEmail);
            return;
        }

        using var message = new MailMessage(section["From"] ?? "noreply@megalab.local", toEmail)
        {
            Subject = subject,
            Body = htmlBody,
            IsBodyHtml = true,
            BodyEncoding = Encoding.UTF8,
        };
        using var client = new SmtpClient(host, int.TryParse(section["Port"], out var p) ? p : 587)
        {
            EnableSsl = !bool.TryParse(section["UseSsl"], out var ssl) || ssl,
        };
        if (!string.IsNullOrWhiteSpace(section["User"]))
            client.Credentials = new NetworkCredential(section["User"], section["Password"]);

        await client.SendMailAsync(message, ct);
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
