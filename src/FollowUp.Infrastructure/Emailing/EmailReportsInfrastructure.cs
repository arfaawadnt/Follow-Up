using System.Net;
using System.Text;
using System.Text.Json;
using FollowUp.Application.Common.Abstractions;
using FollowUp.Application.Common.Abstractions.Persistence;
using FollowUp.Application.Features.AreaStats;
using FollowUp.Application.Features.EmailReports;
using FollowUp.Application.Features.LabStats;
using FollowUp.Application.Features.TestCatalogue;
using FollowUp.Domain.Emailing;
using FollowUp.Domain.Identity;
using FollowUp.Infrastructure.Persistence;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FollowUp.Infrastructure.Emailing;

// ---- Repositories ----

internal sealed class SmtpConfigRepository : ISmtpConfigRepository
{
    private readonly FollowUpDbContext _db;
    public SmtpConfigRepository(FollowUpDbContext db) => _db = db;
    public Task<SmtpConfig?> GetAsync(CancellationToken ct) =>
        _db.SmtpConfigs.FirstOrDefaultAsync(x => x.Id == SmtpConfig.SingletonId, ct);
    public void Add(SmtpConfig config) => _db.SmtpConfigs.Add(config);
}

internal sealed class StatsEmailSubscriptionRepository : IStatsEmailSubscriptionRepository
{
    private readonly FollowUpDbContext _db;
    public StatsEmailSubscriptionRepository(FollowUpDbContext db) => _db = db;
    public async Task<IReadOnlyList<StatsEmailSubscription>> GetAllAsync(CancellationToken ct) =>
        await _db.StatsEmailSubscriptions.OrderBy(x => x.Name).ToListAsync(ct);
    public Task<StatsEmailSubscription?> GetByIdAsync(StatsEmailSubscriptionId id, CancellationToken ct) =>
        _db.StatsEmailSubscriptions.FirstOrDefaultAsync(x => x.Id == id, ct);
    public void Add(StatsEmailSubscription s) => _db.StatsEmailSubscriptions.Add(s);
    public void Remove(StatsEmailSubscription s) => _db.StatsEmailSubscriptions.Remove(s);
}

// ---- Read-side queries ----

internal sealed class SmtpConfigQueries : ISmtpConfigQueries
{
    private readonly FollowUpDbContext _db;
    public SmtpConfigQueries(FollowUpDbContext db) => _db = db;
    public async Task<SmtpConfigDto> GetAsync(CancellationToken ct)
    {
        var c = await _db.SmtpConfigs.AsNoTracking().FirstOrDefaultAsync(x => x.Id == SmtpConfig.SingletonId, ct);
        return c is null
            ? new SmtpConfigDto(false, "", 587, true, "", null, false)
            : new SmtpConfigDto(c.Enabled, c.Host, c.Port, c.UseSsl, c.FromAddress, c.User, c.HasPassword); // password never returned
    }
}

internal sealed class StatsEmailSubscriptionQueries : IStatsEmailSubscriptionQueries
{
    private readonly FollowUpDbContext _db;
    public StatsEmailSubscriptionQueries(FollowUpDbContext db) => _db = db;
    public async Task<IReadOnlyList<StatsEmailSubscriptionDto>> ListAsync(CancellationToken ct)
    {
        var rows = await _db.StatsEmailSubscriptions.AsNoTracking().OrderBy(s => s.Name).ToListAsync(ct);
        return rows.Select(s => new StatsEmailSubscriptionDto(s.Id.Value, s.Name, s.IncludeLabStats, s.IncludeTestStats,
            s.IncludeAreaStats, s.FiltersJson, s.UserIds.ToList(), s.Emails.ToList(), s.SendHour, s.SendMinute,
            s.WindowDays, s.Enabled, s.LastStatus, s.LastRunAt)).ToList();
    }
}

// ---- Per-subscription Hangfire schedule ----

/// <summary>Thin Hangfire job: runs one subscription's daily email. The Guid arg is what Hangfire persists.</summary>
public sealed class StatsEmailJobRunner
{
    private readonly IStatsEmailRunner _runner;
    public StatsEmailJobRunner(IStatsEmailRunner runner) => _runner = runner;
    public Task RunAsync(Guid subscriptionId, CancellationToken ct) => _runner.RunAsync(new StatsEmailSubscriptionId(subscriptionId), ct);
}

internal sealed class StatsEmailScheduler : IStatsEmailScheduler
{
    private readonly IRecurringJobManager _jobs;
    private readonly IStatsEmailSubscriptionRepository _repo;
    private static readonly TimeZoneInfo Cairo = ResolveCairo();
    public StatsEmailScheduler(IRecurringJobManager jobs, IStatsEmailSubscriptionRepository repo) { _jobs = jobs; _repo = repo; }

    private static string JobId(Guid id) => $"stats-email-{id}";

    public void Schedule(StatsEmailSubscription s)
    {
        var id = s.Id.Value;
        if (!s.Enabled) { _jobs.RemoveIfExists(JobId(id)); return; }
        var cron = $"{s.SendMinute} {s.SendHour} * * *"; // daily at HH:mm Cairo
        _jobs.AddOrUpdate<StatsEmailJobRunner>(JobId(id), j => j.RunAsync(id, CancellationToken.None), cron,
            new RecurringJobOptions { TimeZone = Cairo });
    }

    public void Unschedule(StatsEmailSubscriptionId id) => _jobs.RemoveIfExists(JobId(id.Value));

    public async Task SyncAllAsync(CancellationToken ct)
    {
        foreach (var s in await _repo.GetAllAsync(ct)) Schedule(s);
    }

    private static TimeZoneInfo ResolveCairo()
    {
        foreach (var id in new[] { "Africa/Cairo", "Egypt Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { } catch (InvalidTimeZoneException) { }
        }
        return TimeZoneInfo.Utc;
    }
}

// ---- Render + send ----

internal sealed class StatsEmailRunner : IStatsEmailRunner
{
    private readonly FollowUpDbContext _db;
    private readonly IStatsEmailSubscriptionRepository _subs;
    private readonly ILabStatsQueries _labStats;
    private readonly ITestCatalogueQueries _testStats;
    private readonly IAreaStatsQueries _areaStats;
    private readonly IEmailSender _email;
    private readonly IClock _clock;
    private readonly ILogger<StatsEmailRunner> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public StatsEmailRunner(FollowUpDbContext db, IStatsEmailSubscriptionRepository subs, ILabStatsQueries labStats,
        ITestCatalogueQueries testStats, IAreaStatsQueries areaStats, IEmailSender email, IClock clock, ILogger<StatsEmailRunner> logger)
    {
        _db = db; _subs = subs; _labStats = labStats; _testStats = testStats; _areaStats = areaStats;
        _email = email; _clock = clock; _logger = logger;
    }

    private sealed record Filters(string[]? Governorates, string[]? Cities, string[]? Areas, string[]? Categories, string[]? Segments, string[]? Groups);
    private static bool Match(string[]? filter, string? value) =>
        filter is null || filter.Length == 0 || (value != null && filter.Contains(value));
    private static string Enc(string? v) => WebUtility.HtmlEncode(v ?? "");

    public async Task<StatsEmailRunResult> RunAsync(StatsEmailSubscriptionId id, CancellationToken ct)
    {
        var sub = await _subs.GetByIdAsync(id, ct);
        if (sub is null) return new StatsEmailRunResult(false, 0, 0, "not-found");

        var to = _clock.CairoToday.AddDays(-1);
        var from = to.AddDays(-(Math.Max(1, sub.WindowDays) - 1));
        Filters f;
        try { f = JsonSerializer.Deserialize<Filters>(sub.FiltersJson, JsonOpts) ?? new Filters(null, null, null, null, null, null); }
        catch { f = new Filters(null, null, null, null, null, null); }

        var html = await BuildHtmlAsync(sub, from, to, f, ct);
        var recipients = await ResolveRecipientsAsync(sub, ct);
        var subject = sub.WindowDays == 1 ? $"Daily statistics — {to:yyyy-MM-dd}" : $"Statistics {from:yyyy-MM-dd} → {to:yyyy-MM-dd}";

        int sent = 0, fail = 0;
        foreach (var email in recipients)
        {
            try { await _email.SendAsync(email, subject, html, ct); sent++; }
            catch (Exception ex) { fail++; _logger.LogWarning(ex, "Stats email to {Email} failed ({Sub})", email, sub.Name); }
        }

        var status = recipients.Count == 0 ? "no-recipients" : $"sent={sent} failed={fail}";
        sub.RecordRun(status, _clock.UtcNow);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Stats email '{Sub}' {From:yyyy-MM-dd}..{To:yyyy-MM-dd}: {Status}", sub.Name, from, to, status);
        return new StatsEmailRunResult(sent > 0, sent, fail, status);
    }

    private async Task<List<string>> ResolveRecipientsAsync(StatsEmailSubscription sub, CancellationToken ct)
    {
        var set = new HashSet<string>(sub.Emails, StringComparer.OrdinalIgnoreCase);
        if (sub.UserIds.Count > 0)
        {
            var ids = sub.UserIds.ToHashSet();
            var users = await _db.Users.AsNoTracking().Where(u => u.IsActive)
                .Select(u => new { u.Id, u.Email }).ToListAsync(ct);
            foreach (var u in users)
                if (ids.Contains(u.Id.Value) && !string.IsNullOrWhiteSpace(u.Email)) set.Add(u.Email!.Trim());
        }
        return set.ToList();
    }

    private async Task<string> BuildHtmlAsync(StatsEmailSubscription sub, DateOnly from, DateOnly to, Filters f, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.Append("<div style=\"font:14px system-ui,Arial,sans-serif;color:#1a1a1a\">");
        sb.Append($"<h2 style=\"color:#004578;margin:0 0 4px\">{Enc(sub.Name)}</h2>");
        sb.Append($"<p style=\"color:#555;margin:0 0 8px\">Reporting period: {from:yyyy-MM-dd} &rarr; {to:yyyy-MM-dd}</p>");
        if (sub.IncludeLabStats) sb.Append(await RenderLabAsync(from, to, f, ct));
        if (sub.IncludeTestStats) sb.Append(await RenderTestAsync(from, to, f, ct));
        if (sub.IncludeAreaStats) sb.Append(await RenderAreaAsync(from, to, f, ct));
        sb.Append("<p style=\"color:#999;font-size:12px;margin-top:22px\">Sent automatically by Follow-Up.</p></div>");
        return sb.ToString();
    }

    private static string Table(string title, string summary, string[] headers, List<string[]> rows)
    {
        var sb = new StringBuilder();
        sb.Append($"<h3 style=\"color:#004578;margin:22px 0 4px\">{Enc(title)}</h3>");
        if (summary.Length > 0) sb.Append($"<p style=\"margin:0 0 6px;color:#333\">{summary}</p>");
        if (rows.Count == 0) { sb.Append("<p style=\"color:#777\">No data for this period.</p>"); return sb.ToString(); }
        sb.Append("<table style=\"border-collapse:collapse;width:100%;font-size:13px\"><tr>");
        foreach (var h in headers) sb.Append($"<th style=\"background:#004578;color:#fff;padding:6px 8px;text-align:left;border:1px solid #ccc\">{Enc(h)}</th>");
        sb.Append("</tr>");
        var shown = rows.Take(100).ToList();
        foreach (var r in shown)
        {
            sb.Append("<tr>");
            foreach (var c in r) sb.Append($"<td style=\"padding:5px 8px;border:1px solid #ddd\">{Enc(c)}</td>");
            sb.Append("</tr>");
        }
        sb.Append("</table>");
        if (rows.Count > 100) sb.Append($"<p style=\"color:#777;font-size:12px\">&hellip;and {rows.Count - 100} more.</p>");
        return sb.ToString();
    }

    private async Task<string> RenderLabAsync(DateOnly from, DateOnly to, Filters f, CancellationToken ct)
    {
        var rows = (await _labStats.ListAsync(from, to, OrgScope.Global, ct))
            .Where(r => Match(f.Governorates, r.Governorate) && Match(f.Cities, r.City) && Match(f.Areas, r.Area)
                     && Match(f.Categories, r.Category) && Match(f.Segments, r.Segment));
        var agg = rows.GroupBy(r => r.LabCode).Select(g => new
        {
            Name = g.First().Name ?? g.Key, Gov = g.First().Governorate ?? "—", Area = g.First().Area ?? "—",
            Tests = g.Sum(x => x.TestCount), Income = g.Sum(x => x.Income),
        }).OrderByDescending(x => x.Tests).ToList();
        var table = agg.Select(a => new[] { a.Name, a.Gov, a.Area, a.Tests.ToString("N0"), a.Income.ToString("N1") }).ToList();
        var summary = $"<b>Total tests:</b> {agg.Sum(a => a.Tests):N0} &middot; <b>Labs:</b> {agg.Count:N0} &middot; <b>Income:</b> {agg.Sum(a => a.Income):N0} EGP";
        return Table("Lab Statistics", summary, new[] { "Lab", "Governorate", "Area", "Tests", "Income" }, table);
    }

    private async Task<string> RenderTestAsync(DateOnly from, DateOnly to, Filters f, CancellationToken ct)
    {
        var rows = (await _testStats.GetTestStatsAsync(from, to, ct))
            .Where(r => Match(f.Groups, r.GroupName));
        var agg = rows.GroupBy(r => (r.TestCode, r.TestType)).Select(g => new
        {
            Test = g.First().TestName ?? g.Key.TestCode, Group = g.First().GroupName ?? "—",
            Count = g.Sum(x => x.Count), Income = g.Sum(x => x.Income),
        }).OrderByDescending(x => x.Count).ToList();
        var table = agg.Select(a => new[] { a.Test, a.Group, a.Count.ToString("N0"), a.Income.ToString("N1") }).ToList();
        var summary = $"<b>Total tests:</b> {agg.Sum(a => a.Count):N0} &middot; <b>Distinct tests:</b> {agg.Count:N0}";
        return Table("Test Statistics", summary, new[] { "Test", "Group", "Count", "Income" }, table);
    }

    private async Task<string> RenderAreaAsync(DateOnly from, DateOnly to, Filters f, CancellationToken ct)
    {
        var rows = (await _areaStats.ListAsync(from, to, OrgScope.Global, ct))
            .Where(r => Match(f.Governorates, r.Governorate) && Match(f.Cities, r.City) && Match(f.Areas, r.Area));
        var agg = rows.GroupBy(r => (Gov: r.Governorate ?? "—", Area: r.Area ?? "—",
                GovReal: r.GovernorateRealName, AreaReal: r.AreaRealName))
            .Select(g => new
            {
                Gov = g.Key.GovReal is null ? g.Key.Gov : $"{g.Key.Gov} ({g.Key.GovReal})",
                Area = g.Key.AreaReal is null ? g.Key.Area : $"{g.Key.Area} ({g.Key.AreaReal})",
                Tests = g.Sum(x => x.TestCount), Income = g.Sum(x => x.Income),
            }).OrderByDescending(x => x.Tests).ToList();
        var table = agg.Select(a => new[] { a.Gov, a.Area, a.Tests.ToString("N0"), a.Income.ToString("N1") }).ToList();
        var summary = $"<b>Total tests:</b> {agg.Sum(a => a.Tests):N0} &middot; <b>Areas:</b> {agg.Count:N0}";
        return Table("Area Statistics", summary, new[] { "Governorate", "Area", "Tests", "Income" }, table);
    }
}
