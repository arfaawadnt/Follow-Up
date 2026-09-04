using FollowUp.Domain.Common;

namespace FollowUp.Domain.Emailing;

/// <summary>
/// The SMTP mail-gateway configuration (single record). Unlike Oracle's connection string, the password IS
/// operator-editable through the app — but it is masked on read and only overwritten when a new value is
/// supplied (a blank/masked write keeps the stored one).
/// </summary>
public sealed class SmtpConfig : AggregateRoot<string>, IAuditable
{
    private SmtpConfig() { } // EF
    private SmtpConfig(string id) : base(id) { }

    public const string SingletonId = "smtp";

    public bool Enabled { get; private set; }
    public string Host { get; private set; } = string.Empty;
    public int Port { get; private set; } = 587;
    public bool UseSsl { get; private set; } = true;
    public string FromAddress { get; private set; } = string.Empty;
    public string? User { get; private set; }
    public string? Password { get; private set; }   // secret — masked by the API

    public DateTimeOffset CreatedAt { get; private set; }
    public string CreatedBy { get; private set; } = null!;
    public DateTimeOffset? UpdatedAt { get; private set; }
    public string? UpdatedBy { get; private set; }

    public bool HasPassword => !string.IsNullOrEmpty(Password);

    public static SmtpConfig Create() => new(SingletonId);

    /// <summary>Applies the non-secret settings (the password is set separately via <see cref="SetPassword"/>).</summary>
    public void Configure(bool enabled, string host, int port, bool useSsl, string fromAddress, string? user)
    {
        if (port is < 1 or > 65535) throw new DomainException("SMTP port must be between 1 and 65535.");
        Enabled = enabled;
        Host = host?.Trim() ?? string.Empty;
        Port = port;
        UseSsl = useSsl;
        FromAddress = fromAddress?.Trim() ?? string.Empty;
        User = string.IsNullOrWhiteSpace(user) ? null : user.Trim();
    }

    /// <summary>Overwrites the stored password. Call only when the operator actually supplied a new one.</summary>
    public void SetPassword(string? password) => Password = string.IsNullOrWhiteSpace(password) ? null : password;
}

public readonly record struct StatsEmailSubscriptionId(Guid Value)
{
    public static StatsEmailSubscriptionId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}

/// <summary>
/// A saved daily-email report: which statistics reports to include, the filters to apply, the recipients
/// (app users and/or free-form addresses), and the daily send time (Cairo). Sent by its own Hangfire
/// recurring schedule; reports are rendered org-wide and narrowed only by the saved filters.
/// </summary>
public sealed class StatsEmailSubscription : AggregateRoot<StatsEmailSubscriptionId>, IAuditable
{
    private readonly List<Guid> _userIds = new();
    private readonly List<string> _emails = new();

    private StatsEmailSubscription() { } // EF
    private StatsEmailSubscription(StatsEmailSubscriptionId id, string name) : base(id) { Name = name; }

    public string Name { get; private set; } = string.Empty;
    public bool IncludeLabStats { get; private set; }
    public bool IncludeTestStats { get; private set; }
    public bool IncludeAreaStats { get; private set; }
    /// <summary>Opaque saved-filter payload (governorates/cities/areas/categories/segments/groups), applied in memory.</summary>
    public string FiltersJson { get; private set; } = "{}";
    public IReadOnlyCollection<Guid> UserIds => _userIds.AsReadOnly();
    public IReadOnlyCollection<string> Emails => _emails.AsReadOnly();
    public int SendHour { get; private set; }        // 0-23, Cairo
    public int SendMinute { get; private set; }      // 0-59
    public int WindowDays { get; private set; } = 1; // reporting window = this many days ending yesterday
    public bool Enabled { get; private set; } = true;
    public DateTimeOffset? LastRunAt { get; private set; }
    public string? LastStatus { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public string CreatedBy { get; private set; } = null!;
    public DateTimeOffset? UpdatedAt { get; private set; }
    public string? UpdatedBy { get; private set; }

    public static StatsEmailSubscription Create(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("Report name is required.");
        return new StatsEmailSubscription(StatsEmailSubscriptionId.New(), name.Trim());
    }

    public void Rename(string name) =>
        Name = string.IsNullOrWhiteSpace(name) ? throw new DomainException("Report name is required.") : name.Trim();

    public void SetReports(bool lab, bool test, bool area)
    {
        if (!lab && !test && !area) throw new DomainException("Select at least one report.");
        IncludeLabStats = lab; IncludeTestStats = test; IncludeAreaStats = area;
    }

    public void SetFilters(string? json) => FiltersJson = string.IsNullOrWhiteSpace(json) ? "{}" : json.Trim();

    public void SetRecipients(IEnumerable<Guid> userIds, IEnumerable<string> emails)
    {
        _userIds.Clear();
        _userIds.AddRange(userIds.Distinct());
        _emails.Clear();
        _emails.AddRange(emails.Where(e => !string.IsNullOrWhiteSpace(e)).Select(e => e.Trim()).Distinct(StringComparer.OrdinalIgnoreCase));
        if (_userIds.Count == 0 && _emails.Count == 0)
            throw new DomainException("At least one recipient (user or email) is required.");
    }

    public void SetSchedule(int hour, int minute, int windowDays)
    {
        if (hour is < 0 or > 23) throw new DomainException("Send hour must be between 0 and 23.");
        if (minute is < 0 or > 59) throw new DomainException("Send minute must be between 0 and 59.");
        if (windowDays < 1) throw new DomainException("The reporting window must be at least 1 day.");
        SendHour = hour; SendMinute = minute; WindowDays = windowDays;
    }

    public void Enable(bool enabled) => Enabled = enabled;

    public void RecordRun(string status, DateTimeOffset when) { LastStatus = status?.Length > 500 ? status[..500] : status; LastRunAt = when; }
}
