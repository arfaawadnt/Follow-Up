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

    private sealed record Filters(string[]? Governorates, string[]? Cities, string[]? Areas, string[]? Categories,
        string[]? Segments, string[]? Groups, string? RefMonth, string? CompareBy);
    private static bool Match(string[]? filter, string? value) =>
        filter is null || filter.Length == 0 || (value != null && filter.Contains(value));
    private static bool IsIncome(Filters f) => string.Equals(f.CompareBy, "income", StringComparison.OrdinalIgnoreCase);
    private static string Enc(string? v) => WebUtility.HtmlEncode(v ?? "");

    public async Task<StatsEmailRunResult> RunAsync(StatsEmailSubscriptionId id, CancellationToken ct)
    {
        var sub = await _subs.GetByIdAsync(id, ct);
        if (sub is null) return new StatsEmailRunResult(false, 0, 0, "not-found");

        var to = _clock.CairoToday.AddDays(-1);
        var from = to.AddDays(-(Math.Max(1, sub.WindowDays) - 1));
        Filters f;
        try { f = JsonSerializer.Deserialize<Filters>(sub.FiltersJson, JsonOpts) ?? new Filters(null, null, null, null, null, null, null, null); }
        catch { f = new Filters(null, null, null, null, null, null, null, null); }

        var (html, attachments) = await BuildAsync(sub, from, to, f, ct);
        var recipients = await ResolveRecipientsAsync(sub, ct);
        var subject = sub.WindowDays == 1 ? $"Daily statistics — {to:yyyy-MM-dd}" : $"Statistics {from:yyyy-MM-dd} → {to:yyyy-MM-dd}";

        int sent = 0, fail = 0;
        string? lastError = null;
        foreach (var email in recipients)
        {
            try { await _email.SendAsync(email, subject, html, attachments, ct); sent++; }
            catch (Exception ex) { fail++; lastError = ex.Message; _logger.LogWarning(ex, "Stats email to {Email} failed ({Sub})", email, sub.Name); }
        }

        var status = recipients.Count == 0 ? "no-recipients"
            : fail > 0 ? $"sent={sent} failed={fail} · {lastError}"
            : $"sent={sent} failed={fail}";
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

    /// <summary>One report: the HTML summary (rendered inline, capped) plus the full data exported as an .xlsx
    /// attachment. HTML and xlsx carry their own headers/rows so the Area report can attach the grouped, colour-coded
    /// grid (matching the on-screen page) while the email body keeps a compact preview.</summary>
    private sealed record ReportSection(string Title, string FileName, string SummaryHtml,
        string[] HtmlHeaders, List<string[]> HtmlRows, string[] XlsxHeaders, List<XlsxCell[]> XlsxRows);

    private async Task<(string Html, IReadOnlyList<EmailAttachment> Attachments)> BuildAsync(
        StatsEmailSubscription sub, DateOnly from, DateOnly to, Filters f, CancellationToken ct)
    {
        var dateTag = to.ToString("yyyy-MM-dd");
        var sections = new List<ReportSection>();
        if (sub.IncludeLabStats) sections.Add(await RenderLabAsync(dateTag, from, to, f, ct));
        if (sub.IncludeTestStats) sections.Add(await RenderTestAsync(dateTag, from, to, f, ct));
        if (sub.IncludeAreaStats) sections.Add(await RenderAreaAsync(dateTag, from, to, f, ct));

        var sb = new StringBuilder();
        sb.Append("<div style=\"font:14px system-ui,Arial,sans-serif;color:#1a1a1a\">");
        sb.Append($"<h2 style=\"color:#004578;margin:0 0 4px\">{Enc(sub.Name)}</h2>");
        sb.Append($"<p style=\"color:#555;margin:0 0 8px\">Reporting period: {from:yyyy-MM-dd} &rarr; {to:yyyy-MM-dd}</p>");

        var attachments = new List<EmailAttachment>();
        foreach (var s in sections)
        {
            sb.Append(Table(s.Title, s.SummaryHtml, s.HtmlHeaders, s.HtmlRows));
            if (s.XlsxRows.Count > 0)
                attachments.Add(new EmailAttachment(s.FileName, XlsxWriter.Build(s.Title, s.XlsxHeaders, s.XlsxRows), XlsxWriter.ContentType));
        }

        if (attachments.Count > 0)
            sb.Append("<p style=\"color:#333;font-size:13px;margin-top:16px\">The full data for each report is attached as an Excel file. The Area Statistics sheet is grouped by governorate &amp; area and colour-coded against the reference month, matching the on-screen page.</p>");
        sb.Append("<p style=\"color:#999;font-size:12px;margin-top:22px\">Sent automatically by Follow-Up.</p></div>");
        return (sb.ToString(), attachments);
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
        if (rows.Count > 100) sb.Append($"<p style=\"color:#777;font-size:12px\">&hellip;and {rows.Count - 100} more (see the attached Excel file for the full data).</p>");
        return sb.ToString();
    }

    private static string MetricLabel(bool income) => income ? "Test income" : "Test count";

    /// <summary>Lab Statistics as a daily pivot (one column per day in the window, like the on-screen page): per-lab
    /// rows with the selected metric (count or income) in each day column, plus Total tests + Total income.</summary>
    private async Task<ReportSection> RenderLabAsync(string dateTag, DateOnly from, DateOnly to, Filters f, CancellationToken ct)
    {
        var income = IsIncome(f);
        var rows = (await _labStats.ListAsync(from, to, OrgScope.Global, ct))
            .Where(r => Match(f.Governorates, r.Governorate) && Match(f.Cities, r.City) && Match(f.Areas, r.Area)
                     && Match(f.Categories, r.Category) && Match(f.Segments, r.Segment)).ToList();
        var periods = rows.Select(r => r.Date).Distinct().OrderBy(d => d).ToList();

        var byLab = new Dictionary<string, LabPivot>();
        foreach (var r in rows)
        {
            if (!byLab.TryGetValue(r.LabCode, out var p))
            { p = new LabPivot { Name = r.Name ?? r.LabCode, Gov = r.Governorate ?? Dash, Area = r.Area ?? Dash }; byLab[r.LabCode] = p; }
            var c = p.Cells.GetValueOrDefault(r.Date);
            p.Cells[r.Date] = new DayCell(c.Count + r.TestCount, c.Income + r.Income);
            p.TotalTests += r.TestCount; p.TotalIncome += r.Income;
        }
        var list = byLab.Values.OrderByDescending(p => income ? (double)p.TotalIncome : p.TotalTests).ThenBy(p => p.Name).ToList();

        var headers = new List<string> { "Lab", "Governorate", "Area" };
        headers.AddRange(periods.Select(p => p.ToString("yyyy-MM-dd")));
        headers.Add("Total tests"); headers.Add("Total income");

        var xlsxRows = new List<XlsxCell[]>();
        var htmlRows = new List<string[]>();
        foreach (var p in list)
        {
            var xr = new List<XlsxCell> { p.Name, p.Gov, p.Area };
            var hr = new List<string> { p.Name, p.Gov, p.Area };
            foreach (var per in periods)
            {
                var c = p.Cells.GetValueOrDefault(per);
                if (income) { xr.Add(Dec(c.Income)); hr.Add(c.Income.ToString("N1")); }
                else { xr.Add(c.Count); hr.Add(c.Count.ToString("N0")); }
            }
            xr.Add(p.TotalTests); xr.Add(Dec(p.TotalIncome));
            hr.Add(p.TotalTests.ToString("N0")); hr.Add(p.TotalIncome.ToString("N1"));
            xlsxRows.Add(xr.ToArray());
            htmlRows.Add(hr.ToArray());
        }
        var summary = $"<b>Total tests:</b> {list.Sum(p => p.TotalTests):N0} &middot; <b>Labs:</b> {list.Count:N0} &middot; <b>Income:</b> {list.Sum(p => p.TotalIncome):N0} EGP &middot; <b>Compare by:</b> {MetricLabel(income)}";
        return new ReportSection("Lab Statistics", $"Lab-Statistics-{dateTag}.xlsx", summary,
            headers.ToArray(), htmlRows, headers.ToArray(), xlsxRows);
    }

    /// <summary>Test Statistics as a daily pivot (one column per day), per-test rows with the selected metric per day,
    /// plus Total count + Total income.</summary>
    private async Task<ReportSection> RenderTestAsync(string dateTag, DateOnly from, DateOnly to, Filters f, CancellationToken ct)
    {
        var income = IsIncome(f);
        var rows = (await _testStats.GetTestStatsAsync(from, to, ct))
            .Where(r => Match(f.Groups, r.GroupName)).ToList();
        var periods = rows.Select(r => r.Date).Distinct().OrderBy(d => d).ToList();

        var byTest = new Dictionary<(string, int), TestPivot>();
        foreach (var r in rows)
        {
            var key = (r.TestCode, r.TestType);
            if (!byTest.TryGetValue(key, out var p))
            { p = new TestPivot { Test = r.TestName ?? r.TestCode, Group = r.GroupName ?? Dash }; byTest[key] = p; }
            var c = p.Cells.GetValueOrDefault(r.Date);
            p.Cells[r.Date] = new DayCell(c.Count + r.Count, c.Income + r.Income);
            p.TotalCount += r.Count; p.TotalIncome += r.Income;
        }
        var list = byTest.Values.OrderByDescending(p => income ? (double)p.TotalIncome : p.TotalCount).ThenBy(p => p.Test).ToList();

        var headers = new List<string> { "Test", "Group" };
        headers.AddRange(periods.Select(p => p.ToString("yyyy-MM-dd")));
        headers.Add("Total count"); headers.Add("Total income");

        var xlsxRows = new List<XlsxCell[]>();
        var htmlRows = new List<string[]>();
        foreach (var p in list)
        {
            var xr = new List<XlsxCell> { p.Test, p.Group };
            var hr = new List<string> { p.Test, p.Group };
            foreach (var per in periods)
            {
                var c = p.Cells.GetValueOrDefault(per);
                if (income) { xr.Add(Dec(c.Income)); hr.Add(c.Income.ToString("N1")); }
                else { xr.Add(c.Count); hr.Add(c.Count.ToString("N0")); }
            }
            xr.Add(p.TotalCount); xr.Add(Dec(p.TotalIncome));
            hr.Add(p.TotalCount.ToString("N0")); hr.Add(p.TotalIncome.ToString("N1"));
            xlsxRows.Add(xr.ToArray());
            htmlRows.Add(hr.ToArray());
        }
        var summary = $"<b>Total tests:</b> {list.Sum(p => p.TotalCount):N0} &middot; <b>Distinct tests:</b> {list.Count:N0} &middot; <b>Compare by:</b> {MetricLabel(income)}";
        return new ReportSection("Test Statistics", $"Test-Statistics-{dateTag}.xlsx", summary,
            headers.ToArray(), htmlRows, headers.ToArray(), xlsxRows);
    }

    private const string Dash = "—";
    private static decimal Dec(decimal v) => Math.Round(v, 1);
    private readonly record struct DayCell(int Count, decimal Income);
    private sealed class LabPivot { public string Name = ""; public string Gov = ""; public string Area = ""; public readonly Dictionary<DateOnly, DayCell> Cells = new(); public int TotalTests; public decimal TotalIncome; }
    private sealed class TestPivot { public string Test = ""; public string Group = ""; public readonly Dictionary<DateOnly, DayCell> Cells = new(); public int TotalCount; public decimal TotalIncome; }

    // Reference-month window from the "YYYY-MM" RefMonth filter; falls back to the calendar month before `to`.
    private static (DateOnly From, DateOnly To, int Days) RefWindow(DateOnly to, string? refMonth)
    {
        int y, m;
        if (!string.IsNullOrWhiteSpace(refMonth) && refMonth.Length >= 7
            && int.TryParse(refMonth.AsSpan(0, 4), out var py) && int.TryParse(refMonth.AsSpan(5, 2), out var pm)
            && pm is >= 1 and <= 12)
        { y = py; m = pm; }
        else { var prev = new DateOnly(to.Year, to.Month, 1).AddMonths(-1); y = prev.Year; m = prev.Month; }
        var days = DateTime.DaysInMonth(y, m);
        return (new DateOnly(y, m, 1), new DateOnly(y, m, days), days);
    }

    private class AreaAcc
    {
        public string? RealName;
        public readonly Dictionary<DateOnly, DayCell> Cells = new();
        public int Total;
        public decimal Income;
        public int RefCount;
        public decimal RefIncome;
    }
    private sealed class GovAcc : AreaAcc { public readonly Dictionary<string, AreaAcc> Areas = new(); }

    /// <summary>
    /// Area Statistics as a grouped, colour-coded sheet matching the on-screen page and its Excel export: rows are
    /// governorate bands (light fill, bold) each followed by its areas, with per-day columns flagged green when the
    /// day beats the reference month's daily average and red when it falls short (daily view over the window).
    /// </summary>
    private async Task<ReportSection> RenderAreaAsync(string dateTag, DateOnly from, DateOnly to, Filters f, CancellationToken ct)
    {
        var income = IsIncome(f);
        bool Geo(AreaStatDto r) => Match(f.Governorates, r.Governorate) && Match(f.Cities, r.City) && Match(f.Areas, r.Area);
        var rows = (await _areaStats.ListAsync(from, to, OrgScope.Global, ct)).Where(Geo).ToList();

        var (refFrom, refTo, refDays) = RefWindow(to, f.RefMonth);
        var refRows = (await _areaStats.ListAsync(refFrom, refTo, OrgScope.Global, ct)).Where(Geo).ToList();

        // Reference-month totals (count + income) per governorate and per (governorate|area).
        var refByGov = new Dictionary<string, DayCell>();
        var refByArea = new Dictionary<string, DayCell>();
        foreach (var r in refRows)
        {
            var g = r.Governorate ?? Dash; var a = r.Area ?? Dash;
            var cg = refByGov.GetValueOrDefault(g); refByGov[g] = new DayCell(cg.Count + r.TestCount, cg.Income + r.Income);
            var key = g + "|" + a;
            var ca = refByArea.GetValueOrDefault(key); refByArea[key] = new DayCell(ca.Count + r.TestCount, ca.Income + r.Income);
        }

        var periods = rows.Select(r => r.Date).Distinct().OrderBy(d => d).ToList();

        var govs = new Dictionary<string, GovAcc>();
        foreach (var r in rows)
        {
            var govName = r.Governorate ?? Dash; var areaName = r.Area ?? Dash;
            if (!govs.TryGetValue(govName, out var g))
            {
                var rg = refByGov.GetValueOrDefault(govName);
                g = new GovAcc { RealName = r.GovernorateRealName, RefCount = rg.Count, RefIncome = rg.Income };
                govs[govName] = g;
            }
            var gc = g.Cells.GetValueOrDefault(r.Date); g.Cells[r.Date] = new DayCell(gc.Count + r.TestCount, gc.Income + r.Income); g.Total += r.TestCount; g.Income += r.Income;
            if (!g.Areas.TryGetValue(areaName, out var a))
            {
                var ra = refByArea.GetValueOrDefault(govName + "|" + areaName);
                a = new AreaAcc { RealName = r.AreaRealName, RefCount = ra.Count, RefIncome = ra.Income };
                g.Areas[areaName] = a;
            }
            var ac = a.Cells.GetValueOrDefault(r.Date); a.Cells[r.Date] = new DayCell(ac.Count + r.TestCount, ac.Income + r.Income); a.Total += r.TestCount; a.Income += r.Income;
        }
        double MetricTotal(AreaAcc x) => income ? (double)x.Income : x.Total;
        var govList = govs.OrderByDescending(x => MetricTotal(x.Value)).ThenBy(x => x.Key).ToList();

        // Metric-aware values: a period cell / reference in the selected metric (count or income).
        double CellVal(DayCell c) => income ? (double)c.Income : c.Count;
        double RefVal(AreaAcc x) => income ? (double)x.RefIncome : x.RefCount;
        decimal RefMonthDisp(AreaAcc x) => income ? Dec(x.RefIncome) : x.RefCount;
        decimal RefDayDisp(AreaAcc x) => Dec((decimal)(refDays > 0 ? RefVal(x) / refDays : 0));
        object PeriodVal(DayCell c) => income ? Dec(c.Income) : c.Count; // xlsx numeric cell value
        // Daily-view baseline: a period cell is compared against the reference month's average day, in the metric.
        XlsxFill Flag(DayCell c, double refVal)
        {
            var baseline = refDays > 0 ? refVal / refDays : 0;
            if (baseline <= 0) return XlsxFill.None;
            var v = CellVal(c);
            return v > baseline ? XlsxFill.Pos : v < baseline ? XlsxFill.Neg : XlsxFill.None;
        }
        static string Named(string name, string? real) => string.IsNullOrWhiteSpace(real) ? name : $"{name} ({real})";

        var headers = new List<string> { "Governorate", "Area", "Real Name", "Ref by Month", "Ref by Day", "Total Test Count", "Total Income" };
        headers.AddRange(periods.Select(p => p.ToString("yyyy-MM-dd")));

        var xlsxRows = new List<XlsxCell[]>();
        foreach (var (govName, g) in govList)
        {
            XlsxCell Gov(object? v) => new(v, XlsxFill.Gov, Bold: true);
            var govCells = new List<XlsxCell> { Gov(govName), Gov(""), Gov(g.RealName ?? ""), Gov(RefMonthDisp(g)), Gov(RefDayDisp(g)), Gov(g.Total), Gov(Dec(g.Income)) };
            foreach (var p in periods)
            {
                var c = g.Cells.GetValueOrDefault(p);
                var flag = Flag(c, RefVal(g));
                govCells.Add(flag != XlsxFill.None ? new XlsxCell(PeriodVal(c), flag) : Gov(PeriodVal(c)));
            }
            xlsxRows.Add(govCells.ToArray());

            foreach (var (areaName, a) in g.Areas.OrderByDescending(x => MetricTotal(x.Value)).ThenBy(x => x.Key))
            {
                var areaCells = new List<XlsxCell> { "", areaName, a.RealName ?? "", RefMonthDisp(a), RefDayDisp(a), a.Total, Dec(a.Income) };
                foreach (var p in periods)
                {
                    var c = a.Cells.GetValueOrDefault(p);
                    var flag = Flag(c, RefVal(a));
                    areaCells.Add(flag != XlsxFill.None ? new XlsxCell(PeriodVal(c), flag) : new XlsxCell(PeriodVal(c)));
                }
                xlsxRows.Add(areaCells.ToArray());
            }
        }

        // Compact HTML preview in the email body (governorate + area totals); the full grouped/colour-coded grid is the attachment.
        string RefDisp(AreaAcc x) => income ? RefMonthDisp(x).ToString("N1") : ((int)x.RefCount).ToString("N0");
        var htmlHeaders = new[] { "Governorate", "Area", "Ref (month)", "Tests", "Income" };
        var htmlRows = new List<string[]>();
        foreach (var (govName, g) in govList)
        {
            htmlRows.Add(new[] { Named(govName, g.RealName), "", RefDisp(g), g.Total.ToString("N0"), g.Income.ToString("N1") });
            foreach (var (areaName, a) in g.Areas.OrderByDescending(x => MetricTotal(x.Value)).ThenBy(x => x.Key))
                htmlRows.Add(new[] { "", Named(areaName, a.RealName), RefDisp(a), a.Total.ToString("N0"), a.Income.ToString("N1") });
        }

        var totalTests = rows.Sum(r => r.TestCount);
        var summary = $"<b>Total tests:</b> {totalTests:N0} &middot; <b>Areas:</b> {govList.Sum(x => x.Value.Areas.Count):N0} &middot; <b>Reference month:</b> {refFrom:yyyy-MM} &middot; <b>Compare by:</b> {MetricLabel(income)} (green beats the daily average, red falls short)";
        return new ReportSection("Area Statistics", $"Area-Statistics-{dateTag}.xlsx", summary,
            htmlHeaders, htmlRows, headers.ToArray(), xlsxRows);
    }
}
