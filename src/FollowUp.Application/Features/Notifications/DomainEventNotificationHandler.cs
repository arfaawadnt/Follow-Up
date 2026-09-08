using System.Net;
using FollowUp.Application.Common.Abstractions;
using FollowUp.Application.Common.Abstractions.Persistence;
using FollowUp.Application.Common.Messaging;
using FollowUp.Domain.Complaints;
using FollowUp.Domain.Identity;
using FollowUp.Domain.Laboratories;
using FollowUp.Domain.Marketing;
using FollowUp.Domain.Notifications;
using FollowUp.Domain.Operations;
using MediatR;
using Microsoft.Extensions.Logging;

namespace FollowUp.Application.Features.Notifications;

/// <summary>
/// The notification fan-out (SRS FR-16, Workflows §11). Maps a domain event to a template + recipients (users
/// with the relevant privilege), then per recipient honours channel preferences: an in-app feed row + SignalR
/// push (System, default on), and Mail/WhatsApp deliveries. Lab codes are masked before external egress; email
/// variables are HTML-escaped (JOBS-003). Runs from the Outbox dispatcher and never throws — a notification
/// failure must not derail outbox processing (failed deliveries are logged for retry, JOBS-006).
/// </summary>
public sealed class DomainEventNotificationHandler : INotificationHandler<DomainEventNotification>
{
    private readonly INotificationRecipients _recipients;
    private readonly INotificationTemplateRepository _templates;
    private readonly INotificationPreferenceRepository _preferences;
    private readonly ISystemNotificationRepository _feed;
    private readonly INotificationDeliveryLogRepository _deliveries;
    private readonly ILaboratoryRepository _labs;
    private readonly INotificationDispatcher _dispatcher;
    private readonly IRealtimeNotifier _realtime;
    private readonly IClock _clock;
    private readonly ILogger<DomainEventNotificationHandler> _logger;

    public DomainEventNotificationHandler(
        INotificationRecipients recipients, INotificationTemplateRepository templates,
        INotificationPreferenceRepository preferences, ISystemNotificationRepository feed,
        INotificationDeliveryLogRepository deliveries, ILaboratoryRepository labs,
        INotificationDispatcher dispatcher, IRealtimeNotifier realtime, IClock clock,
        ILogger<DomainEventNotificationHandler> logger)
    {
        _recipients = recipients; _templates = templates; _preferences = preferences; _feed = feed;
        _deliveries = deliveries; _labs = labs; _dispatcher = dispatcher;
        _realtime = realtime; _clock = clock; _logger = logger;
    }

    private sealed record Plan(string EventKey, string Privilege, LaboratoryId? LabId, IDictionary<string, string> Vars);

    public async Task Handle(DomainEventNotification notification, CancellationToken ct)
    {
        try
        {
            var plan = Map(notification.DomainEvent);
            if (plan is null) return; // not a user-facing event (dataChange broadcast handled elsewhere)

            var template = await _templates.GetByEventKeyAsync(plan.EventKey, ct);
            if (template is null) return;

            // Resolve lab code (real for the in-app feed; masked ENC alias before external egress).
            string labReal = plan.Vars.TryGetValue("lab", out var l) ? l : string.Empty;
            string labMasked = labReal;
            Domain.Laboratories.Laboratory? eventLab = null;
            if (plan.LabId is { } labId)
            {
                eventLab = await _labs.GetByIdAsync(labId, ct);
                if (eventLab is not null) { labReal = eventLab.Code.Value; labMasked = eventLab.Code.ToEncryptedAlias(); }
            }

            var recipients = await _recipients.ForPrivilegeAsync(plan.Privilege, ct);
            // Withhold a lab-scoped notification — and the real lab code it carries in the feed — from users
            // whose org scope does not include the event's lab (finding M-3 / MSG-002). Lab-less events
            // (eventLab is null) are not scope-filtered.
            if (eventLab is not null)
                recipients = recipients.Where(r => r.Scope.Allows(
                    eventLab.Branch, eventLab.Governorate, eventLab.City, eventLab.Area, eventLab.Category, eventLab.Segment)).ToList();
            foreach (var r in recipients)
            {
                var pref = await _preferences.GetAsync(new AppUserId(r.UserId), plan.EventKey, ct);
                var vars = new Dictionary<string, string>(plan.Vars);

                // In-app feed (System channel — default on).
                var systemOn = pref?.Allows(NotificationChannel.System) ?? true;
                if (systemOn)
                {
                    vars["lab"] = labReal;
                    var (title, body) = Render(template, r.Language, vars, htmlEscape: false);
                    _feed.Add(SystemNotification.Create(new AppUserId(r.UserId), plan.EventKey, title, body, _clock.UtcNow));
                    await _realtime.NotifyUserAsync(r.UserId, title, ct);
                }

                // External channels — mask the lab code before egress.
                vars["lab"] = labMasked;
                if ((pref?.Mail ?? false) && !string.IsNullOrWhiteSpace(r.Email))
                {
                    var (subject, body) = Render(template, r.Language, vars, htmlEscape: true);
                    await DeliverAsync(NotificationChannel.Mail, r.Email!, plan.EventKey, subject, body, Array.Empty<string>(), ct);
                }

                if ((pref?.WhatsApp ?? false) && !string.IsNullOrWhiteSpace(r.Phone))
                    await DeliverAsync(NotificationChannel.WhatsApp, r.Phone!, plan.EventKey, null, null, vars.Values.ToList(), ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Notification fan-out failed for {Event}", notification.DomainEvent.GetType().Name);
        }
    }

    // Sends over one external channel and records the rendered content on the delivery log, so a failed
    // delivery can be re-sent by the retry job without re-rendering (finding M-13).
    private async Task DeliverAsync(NotificationChannel channel, string recipient, string eventKey,
        string? subject, string? body, IReadOnlyList<string> parameters, CancellationToken ct)
    {
        var log = NotificationDeliveryLog.Queue(channel, recipient, eventKey, subject, body,
            parameters.Count == 0 ? null : System.Text.Json.JsonSerializer.Serialize(parameters), _clock.UtcNow);
        _deliveries.Add(log);
        try
        {
            await _dispatcher.SendAsync(channel, recipient, eventKey, subject, body, parameters, ct);
            log.MarkSent(_clock.UtcNow);
        }
        catch (Exception ex)
        {
            log.MarkFailed(ex.Message, _clock.UtcNow); // the retry job re-attempts from the stored content (finding M-13)
        }
    }

    private static (string Title, string Body) Render(NotificationTemplate t, string lang, IDictionary<string, string> vars, bool htmlEscape)
    {
        var ar = string.Equals(lang, "ar", StringComparison.OrdinalIgnoreCase);
        var subject = Substitute(ar ? t.SubjectAr : t.SubjectEn, vars, htmlEscape);
        var body = Substitute(ar ? t.BodyAr : t.BodyEn, vars, htmlEscape);
        return (subject, body);
    }

    private static string Substitute(string template, IDictionary<string, string> vars, bool htmlEscape)
    {
        var result = template;
        foreach (var (key, value) in vars)
            result = result.Replace("{" + key + "}", htmlEscape ? WebUtility.HtmlEncode(value) : value);
        return result;
    }

    private Plan? Map(Domain.Common.IDomainEvent e) => e switch
    {
        ComplaintLogged c => new Plan("complaint.logged", Privileges.ManageComplaints, c.LaboratoryId,
            new Dictionary<string, string> { ["reference"] = c.Number }),
        ComplaintResolved c => new Plan("complaint.resolved", Privileges.ManageComplaints, c.LaboratoryId,
            new Dictionary<string, string> { ["reference"] = c.Number }),
        VisitMissed v => new Plan("visit.missed", Privileges.VerifyDailyFollowup, v.LaboratoryId,
            new Dictionary<string, string> { ["date"] = _clock.CairoToday.ToString("yyyy-MM-dd") }),
        MarketingVisitScheduled m => new Plan("marketing.scheduled", Privileges.ViewMarketing, m.LaboratoryId,
            new Dictionary<string, string> { ["date"] = _clock.CairoToday.ToString("yyyy-MM-dd") }),
        _ => null,
    };
}
