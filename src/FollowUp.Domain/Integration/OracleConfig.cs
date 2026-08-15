using System.Security.Cryptography;
using System.Text;
using FollowUp.Domain.Common;

namespace FollowUp.Domain.Integration;

/// <summary>
/// One allow-listed, read-only Oracle SELECT (SRS FR-17). The SQL text is config-managed (never editable via
/// the API); its fingerprint is re-validated at execution so a tampered query is refused.
/// </summary>
public sealed class AllowListedQuery : ValueObject
{
    public string Name { get; }   // Labs | LabStats | TestStats
    public string Sql { get; }
    public string Fingerprint { get; }

    private AllowListedQuery(string name, string sql, string fingerprint)
    {
        Name = name;
        Sql = sql;
        Fingerprint = fingerprint;
    }

    public static AllowListedQuery Create(string name, string sql)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("Query name is required.");
        if (string.IsNullOrWhiteSpace(sql)) throw new DomainException("Query SQL is required.");
        var trimmed = sql.Trim();
        if (!trimmed.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
            throw new DomainException("Only read-only SELECT queries are allowed.");
        return new AllowListedQuery(name.Trim(), trimmed, Fingerprint_(trimmed));
    }

    private static string Fingerprint_(string sql) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sql)));

    /// <summary>Re-validates that the (possibly reloaded) SQL still matches the recorded fingerprint.</summary>
    public bool Matches(string sql) => Fingerprint_(sql.Trim()) == Fingerprint;

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Name;
        yield return Fingerprint;
    }
}

/// <summary>
/// Oracle synchronization configuration (SRS FR-17). Only <see cref="Enabled"/> and <see cref="IntervalHours"/>
/// are writable via the API; the connection string and the allow-listed queries are config-managed and the
/// connection string is never returned by the API. There is a single configuration record.
/// </summary>
public sealed class OracleConfig : AggregateRoot<string>
{
    private readonly List<AllowListedQuery> _queries = new();

    private OracleConfig() { } // EF

    private OracleConfig(string id, bool enabled, int intervalHours) : base(id)
    {
        Enabled = enabled;
        IntervalHours = intervalHours;
    }

    public const string SingletonId = "oracle";

    public bool Enabled { get; private set; }
    public int IntervalHours { get; private set; }
    public string? ConnectionString { get; private set; }   // never exposed by the API
    public IReadOnlyCollection<AllowListedQuery> Queries => _queries.AsReadOnly();
    public DateTimeOffset? LastSyncAt { get; private set; }
    public string? LastStatus { get; private set; }

    public static OracleConfig Create(bool enabled, int intervalHours) =>
        new(SingletonId, enabled, ValidInterval(intervalHours));

    /// <summary>API-writable: enable/disable and interval only (SRS FR-17 acceptance a).</summary>
    public void Configure(bool enabled, int intervalHours)
    {
        Enabled = enabled;
        IntervalHours = ValidInterval(intervalHours);
    }

    /// <summary>Config-managed only (not reachable from the API): connection + allow-listed queries.</summary>
    public void ApplyManagedConfig(string? connectionString, IEnumerable<AllowListedQuery> queries)
    {
        ConnectionString = connectionString;
        _queries.Clear();
        _queries.AddRange(queries);
    }

    public bool IsDue(DateTimeOffset now) =>
        Enabled && (LastSyncAt is null || now - LastSyncAt >= TimeSpan.FromHours(IntervalHours));

    public void RecordSyncResult(string status, DateTimeOffset when)
    {
        LastStatus = status;
        LastSyncAt = when;
    }

    private static int ValidInterval(int hours) =>
        hours < 1 ? throw new DomainException("Oracle sync interval must be at least 1 hour.") : hours;
}
