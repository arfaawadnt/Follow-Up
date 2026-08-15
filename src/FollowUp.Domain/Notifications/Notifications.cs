using FollowUp.Domain.Common;
using FollowUp.Domain.Identity;

namespace FollowUp.Domain.Notifications;

/// <summary>Delivery channel for a notification (SRS FR-16).</summary>
public sealed class NotificationChannel : Enumeration
{
    public static readonly NotificationChannel System = new(1, nameof(System));
    public static readonly NotificationChannel Mail = new(2, nameof(Mail));
    public static readonly NotificationChannel WhatsApp = new(3, nameof(WhatsApp));

    private NotificationChannel(int id, string name) : base(id, name) { }
}

public readonly record struct NotificationTemplateId(Guid Value)
{
    public static NotificationTemplateId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}

/// <summary>
/// A bilingual notification template (SRS FR-16 — six seeded: complaint logged/resolved, visit missed,
/// marketing scheduled, pace alert, birthday). Rendering (variable substitution + HTML escaping for email)
/// happens in Infrastructure; the domain owns the template content and its event key.
/// </summary>
public sealed class NotificationTemplate : AggregateRoot<NotificationTemplateId>, IAuditable
{
    private NotificationTemplate() { } // EF

    private NotificationTemplate(NotificationTemplateId id, string eventKey, string subjectEn, string subjectAr,
        string bodyEn, string bodyAr) : base(id)
    {
        EventKey = eventKey;
        SubjectEn = subjectEn;
        SubjectAr = subjectAr;
        BodyEn = bodyEn;
        BodyAr = bodyAr;
    }

    /// <summary>Stable event key, e.g. <c>complaint.logged</c>, <c>visit.missed</c>, <c>contact.birthday</c>.</summary>
    public string EventKey { get; private set; } = null!;
    public string SubjectEn { get; private set; } = null!;
    public string SubjectAr { get; private set; } = null!;
    public string BodyEn { get; private set; } = null!;
    public string BodyAr { get; private set; } = null!;

    public DateTimeOffset CreatedAt { get; private set; }
    public string CreatedBy { get; private set; } = null!;
    public DateTimeOffset? UpdatedAt { get; private set; }
    public string? UpdatedBy { get; private set; }

    public static NotificationTemplate Create(string eventKey, string subjectEn, string subjectAr,
        string bodyEn, string bodyAr)
    {
        if (string.IsNullOrWhiteSpace(eventKey)) throw new DomainException("Template event key is required.");
        return new NotificationTemplate(NotificationTemplateId.New(), eventKey.Trim(),
            subjectEn, subjectAr, bodyEn, bodyAr);
    }

    public void Update(string subjectEn, string subjectAr, string bodyEn, string bodyAr)
    {
        SubjectEn = subjectEn; SubjectAr = subjectAr; BodyEn = bodyEn; BodyAr = bodyAr;
    }
}

public readonly record struct NotificationPreferenceId(Guid Value)
{
    public static NotificationPreferenceId New() => new(Guid.NewGuid());
}

/// <summary>
/// Per-user, per-template channel opt-ins (SRS FR-16). Default: System on, Mail/WhatsApp off. Each channel
/// gates whether the dispatcher may send on it for this user+event.
/// </summary>
public sealed class NotificationPreference : AggregateRoot<NotificationPreferenceId>
{
    private NotificationPreference() { } // EF

    private NotificationPreference(NotificationPreferenceId id, AppUserId userId, string eventKey,
        bool system, bool mail, bool whatsApp) : base(id)
    {
        UserId = userId;
        EventKey = eventKey;
        System = system;
        Mail = mail;
        WhatsApp = whatsApp;
    }

    public AppUserId UserId { get; private set; }
    public string EventKey { get; private set; } = null!;
    public bool System { get; private set; }
    public bool Mail { get; private set; }
    public bool WhatsApp { get; private set; }

    /// <summary>Creates a preference with the SRS default (System on; Mail/WhatsApp off).</summary>
    public static NotificationPreference Default(AppUserId userId, string eventKey) =>
        new(NotificationPreferenceId.New(), userId, eventKey, system: true, mail: false, whatsApp: false);

    public void Set(bool system, bool mail, bool whatsApp)
    {
        System = system; Mail = mail; WhatsApp = whatsApp;
    }

    public bool Allows(NotificationChannel channel) =>
        channel == NotificationChannel.System ? System
        : channel == NotificationChannel.Mail ? Mail
        : WhatsApp;
}

public readonly record struct SystemNotificationId(Guid Value)
{
    public static SystemNotificationId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}

/// <summary>An in-app feed notification (SRS FR-16) delivered in real time and markable as read.</summary>
public sealed class SystemNotification : AggregateRoot<SystemNotificationId>
{
    private SystemNotification() { } // EF

    private SystemNotification(SystemNotificationId id, AppUserId recipientUserId, string eventKey,
        string title, string body, DateTimeOffset createdAt) : base(id)
    {
        RecipientUserId = recipientUserId;
        EventKey = eventKey;
        Title = title;
        Body = body;
        CreatedAt = createdAt;
    }

    public AppUserId RecipientUserId { get; private set; }
    public string EventKey { get; private set; } = null!;
    public string Title { get; private set; } = null!;
    public string Body { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ReadAt { get; private set; }

    public bool IsRead => ReadAt is not null;

    public static SystemNotification Create(AppUserId recipient, string eventKey, string title, string body,
        DateTimeOffset createdAt) =>
        new(SystemNotificationId.New(), recipient, eventKey, title, body, createdAt);

    public void MarkRead(DateTimeOffset when) => ReadAt ??= when;
}

public readonly record struct NotificationDeliveryLogId(Guid Value)
{
    public static NotificationDeliveryLogId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}

/// <summary>
/// Delivery attempt record for an external channel (SRS FR-16). Failures are logged and retried by the
/// dispatcher with bounded backoff (closing JOBS-006). Tracks attempts and last error.
/// </summary>
public sealed class NotificationDeliveryLog : AggregateRoot<NotificationDeliveryLogId>
{
    private NotificationDeliveryLog() { } // EF

    private NotificationDeliveryLog(NotificationDeliveryLogId id, NotificationChannel channel, string recipient,
        string eventKey, DateTimeOffset queuedAt) : base(id)
    {
        Channel = channel;
        Recipient = recipient;
        EventKey = eventKey;
        QueuedAt = queuedAt;
        Status = "Pending";
    }

    public NotificationChannel Channel { get; private set; } = null!;
    public string Recipient { get; private set; } = null!;
    public string EventKey { get; private set; } = null!;
    public DateTimeOffset QueuedAt { get; private set; }
    public string Status { get; private set; } = null!;   // Pending | Sent | Failed
    public int Attempts { get; private set; }
    public string? LastError { get; private set; }
    public DateTimeOffset? LastAttemptAt { get; private set; }

    public static NotificationDeliveryLog Queue(NotificationChannel channel, string recipient, string eventKey,
        DateTimeOffset queuedAt) =>
        new(NotificationDeliveryLogId.New(), channel, recipient, eventKey, queuedAt);

    public void MarkSent(DateTimeOffset when)
    {
        Attempts++;
        Status = "Sent";
        LastAttemptAt = when;
        LastError = null;
    }

    public void MarkFailed(string error, DateTimeOffset when)
    {
        Attempts++;
        Status = "Failed";
        LastError = error;
        LastAttemptAt = when;
    }

    /// <summary>Resets a failed delivery to Pending so the dispatcher re-attempts it (manual retry, JOBS-006).</summary>
    public void RequeueForRetry()
    {
        if (Status == "Sent")
            throw new DomainException("A delivered notification cannot be retried.");
        Status = "Pending";
    }

    /// <summary>Whether the dispatcher should retry (failed and under the bounded attempt ceiling).</summary>
    public bool ShouldRetry(int maxAttempts) => Status == "Failed" && Attempts < maxAttempts;
}
