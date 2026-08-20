using FollowUp.Application.Common.Abstractions;
using FollowUp.Application.Features.Insights;
using FollowUp.Application.Features.Notifications;
using FollowUp.Domain.Common;
using FollowUp.Domain.Complaints;
using FollowUp.Domain.Identity;
using FollowUp.Domain.Laboratories;
using FollowUp.Domain.Operations;
using FollowUp.Domain.Representatives;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

namespace FollowUp.Infrastructure.Persistence.Queries;

internal sealed class NotificationQueries : INotificationQueries
{
    private readonly FollowUpDbContext _db;
    public NotificationQueries(FollowUpDbContext db) => _db = db;

    public async Task<IReadOnlyList<NotificationDto>> GetFeedAsync(AppUserId userId, bool unreadOnly, CancellationToken ct)
    {
        var q = _db.SystemNotifications.AsNoTracking().Where(n => n.RecipientUserId == userId);
        if (unreadOnly) q = q.Where(n => n.ReadAt == null);
        return await q.OrderByDescending(n => n.CreatedAt).Take(200)
            .Select(n => new NotificationDto(n.Id.Value, n.EventKey, n.Title, n.Body, n.CreatedAt, n.ReadAt != null))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<PreferenceDto>> GetPreferencesAsync(AppUserId userId, CancellationToken ct) =>
        await _db.NotificationPreferences.AsNoTracking().Where(p => p.UserId == userId)
            .Select(p => new PreferenceDto(p.EventKey, p.System, p.Mail, p.WhatsApp)).ToListAsync(ct);

    public async Task<IReadOnlyList<GatewayDto>> GetGatewaysAsync(CancellationToken ct)
    {
        // Secrets are never returned in clear (SRS NFR-SEC-7) — always masked here.
        var settings = await _db.Settings.AsNoTracking().Where(s => s.IsSecret).ToListAsync(ct);
        return new[] { "Mail", "WhatsApp" }
            .Select(name => new GatewayDto(name, settings.Any(s => s.Id.StartsWith(name, StringComparison.OrdinalIgnoreCase)), "********"))
            .ToList();
    }

    public async Task<IReadOnlyList<DeliveryLogDto>> GetLogsAsync(CancellationToken ct) =>
        await _db.DeliveryLogs.AsNoTracking().OrderByDescending(l => l.QueuedAt).Take(500)
            .Select(l => new DeliveryLogDto(l.Id.Value, l.Channel.Name, l.Recipient, l.EventKey, l.Status, l.Attempts, l.LastError))
            .ToListAsync(ct);
}

internal sealed class InsightsQueries : IInsightsQueries
{
    private readonly FollowUpDbContext _db;
    public InsightsQueries(FollowUpDbContext db) => _db = db;

    public async Task<DashboardDto> GetDashboardAsync(OrgScope scope, bool canSeeEncrypted, DateOnly today, CancellationToken ct)
    {
        var thisYm = new YearMonth(today.Year, today.Month).Code;
        // Value-object sub-properties (Segment.Name, Code.Value, Period.Code) don't translate to SQL — so we
        // materialize the scoped rows with converted-type equality/IN filters and then compute in memory.
        var labs = (await _db.Laboratories.ApplyScope(scope).AsNoTracking()
            .Select(l => new { l.Id, l.Code, l.Name, l.Segment, l.Governorate, l.Area, l.MonthlyTarget, l.Status, l.CollectorRepId })
            .ToListAsync(ct))
            .Select(l => new LabRow(l.Id, l.Name, l.Segment.Name, l.Governorate, l.Area, l.MonthlyTarget,
                l.Status == LaboratoryStatus.Active, l.CollectorRepId))
            .ToList();
        var labIds = labs.Select(l => l.Id).ToList();
        var labById = labs.ToDictionary(l => l.Id);

        // Monthly aggregates for the trailing 6 months (current first) — for MTD, trend, top labs, gov volume.
        var months = Enumerable.Range(0, 6).Select(i => { var m = today.AddMonths(-i); return new YearMonth(m.Year, m.Month); }).ToList();
        var monthCodes = months.Select(m => m.Code).ToList();
        var ms = await _db.MonthlySamples.AsNoTracking()
            .Where(m => labIds.Contains(m.LaboratoryId) && months.Contains(m.Period))
            .Select(m => new { m.LaboratoryId, Period = m.Period, m.SampleCount })
            .ToListAsync(ct);
        var mtdByLab = ms.Where(x => x.Period.Code == thisYm)
            .GroupBy(x => x.LaboratoryId).ToDictionary(g => g.Key, g => g.Sum(x => x.SampleCount));
        int MtdOf(LaboratoryId id) => mtdByLab.TryGetValue(id, out var v) ? v : 0;

        // Today's visits.
        var visits = await _db.DailyVisits.AsNoTracking()
            .Where(v => v.VisitDate == today && labIds.Contains(v.LaboratoryId))
            .Select(v => new { v.Id, v.LaboratoryId, v.Status, v.SampleCount, v.ScheduledTime, v.CollectorRepId, v.TransferConfirmedAt })
            .ToListAsync(ct);
        bool IsDone(VisitStatus s) => s == VisitStatus.Visited || s == VisitStatus.Received;

        // Rep names for schedule + collector progress.
        var repIds = labs.Where(l => l.CollectorRepId != null).Select(l => l.CollectorRepId!.Value)
            .Concat(visits.Where(v => v.CollectorRepId != null).Select(v => v.CollectorRepId!.Value)).Distinct().ToList();
        var repName = (await _db.Representatives.AsNoTracking().Where(r => repIds.Contains(r.Id))
            .Select(r => new { r.Id, r.FullName }).ToListAsync(ct)).ToDictionary(r => r.Id, r => r.FullName);
        string RepOf(RepresentativeId? id) => id != null && repName.TryGetValue(id.Value, out var n) ? n : "—";

        // Unresolved complaints (top 8, newest first).
        var cRaw = await _db.Complaints.AsNoTracking()
            .Where(c => labIds.Contains(c.LaboratoryId) && c.Status != ComplaintStatus.Resolved)
            .OrderByDescending(c => c.Number)
            .Select(c => new { c.Number, c.LaboratoryId, c.Category, c.Details, c.CreatedAt })
            .Take(8).ToListAsync(ct);

        var openCount = await _db.Complaints.CountAsync(c => labIds.Contains(c.LaboratoryId) && (c.Status == ComplaintStatus.Open || c.Status == ComplaintStatus.InProgress), ct);
        var inProgCount = await _db.Complaints.CountAsync(c => labIds.Contains(c.LaboratoryId) && c.Status == ComplaintStatus.InProgress, ct);
        var resolvedCount = await _db.Complaints.CountAsync(c => labIds.Contains(c.LaboratoryId) && c.Status == ComplaintStatus.Resolved, ct);

        // Birthdays today.
        var contacts = await _db.Laboratories.ApplyScope(scope)
            .SelectMany(l => l.Contacts.Select(c => new { c.Name, c.Birthday, LabName = l.Name }))
            .Where(x => x.Birthday != null).ToListAsync(ct);
        var bdayContact = contacts.FirstOrDefault(x => x.Birthday!.Value.Month == today.Month && x.Birthday!.Value.Day == today.Day);

        // ---- assemble ----
        var kpis = new DashboardKpisDto(
            ActiveLabs: labs.Count(l => l.Active),
            TotalLabs: labs.Count,
            Done: visits.Count(v => IsDone(v.Status)),
            TotalVisits: visits.Count,
            Pending: visits.Count(v => v.Status == VisitStatus.Pending),
            Missed: visits.Count(v => v.Status == VisitStatus.Missed),
            SamplesToday: visits.Where(v => IsDone(v.Status) && v.SampleCount != null).Sum(v => v.SampleCount!.Value),
            OpenComplaints: openCount, InProgress: inProgCount, Resolved: resolvedCount,
            Mtd: mtdByLab.Values.Sum(), Target: labs.Sum(l => (long)l.MonthlyTarget),
            MonthName: today.ToString("MMMM", CultureInfo.InvariantCulture));

        var schedule = visits.OrderBy(v => v.ScheduledTime).Take(9)
            .Select(v => new DashScheduleDto(v.Id.Value, v.ScheduledTime.ToString("HH:mm"),
                labById.TryGetValue(v.LaboratoryId, out var l) ? l.Name : "—", l?.Area, RepOf(v.CollectorRepId),
                v.Status.Name, v.SampleCount, v.TransferConfirmedAt != null)).ToList();

        var complaints = cRaw.Select(c => new DashComplaintDto($"CMP-{c.Number}",
            labById.TryGetValue(c.LaboratoryId, out var l) ? l.Name : "—", c.Details, c.Category,
            Math.Max(0, today.DayNumber - DateOnly.FromDateTime(c.CreatedAt.UtcDateTime).DayNumber))).ToList();

        var repProg = labs.Where(l => l.CollectorRepId != null).GroupBy(l => l.CollectorRepId!.Value)
            .Select(g => { var tgt = g.Sum(l => l.MonthlyTarget); var mtd = g.Sum(l => MtdOf(l.Id));
                return new DashRepProgDto(RepOf(g.Key), $"{mtd:n0} / {tgt:n0}", tgt > 0 ? (int)Math.Round(100.0 * mtd / tgt) : 0); })
            .Where(r => r.Detail != "0 / 0").OrderByDescending(r => r.Pct).Take(8).ToList();

        var topLabs = labs.Select(l => new DashTopLabDto(l.Name, l.Area, MtdOf(l.Id)))
            .Where(x => x.V > 0).OrderByDescending(x => x.V).Take(5).ToList();

        var trend = months.AsEnumerable().Reverse()
            .Select(m => ms.Where(x => x.Period.Code == m.Code).Sum(x => x.SampleCount)).ToList();

        var segMix = labs.GroupBy(l => l.Seg).OrderBy(g => g.Key)
            .Select(g => new DashSegMixDto(g.Key, g.Count())).ToList();

        var govRows = labs.Where(l => !string.IsNullOrWhiteSpace(l.Gov)).GroupBy(l => l.Gov!)
            .Select(g => new DashGovRowDto(g.Key, g.Sum(l => MtdOf(l.Id))))
            .OrderByDescending(x => x.V).Take(8).ToList();

        return new DashboardDto(kpis,
            bdayContact is null ? null : new DashboardBirthdayDto($"{bdayContact.Name} at {bdayContact.LabName} has a birthday today"),
            schedule, complaints, repProg, topLabs, trend, segMix, govRows);
    }

    private sealed record LabRow(LaboratoryId Id, string Name, string Seg, string? Gov, string? Area,
        int MonthlyTarget, bool Active, RepresentativeId? CollectorRepId);

    public async Task<NetworkOverviewDto> GetOverviewAsync(OrgScope scope, CancellationToken ct)
    {
        var active = LaboratoryStatus.Active;
        var total = await _db.Laboratories.ApplyScope(scope).CountAsync(ct);
        var activeLabs = await _db.Laboratories.ApplyScope(scope).CountAsync(l => l.Status == active, ct);
        var scopedLabIds = await _db.Laboratories.ApplyScope(scope).Select(l => l.Id).ToListAsync(ct);
        var monthStart = new DateOnly(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
        var samples = await _db.MonthlySamples.Where(m => scopedLabIds.Contains(m.LaboratoryId)).SumAsync(m => (int?)m.SampleCount, ct) ?? 0;
        // Income is a Money value object (converted) — its .Amount can't translate to SQL; sum the
        // materialized month's incomes in memory (bounded by one month of per-lab rows).
        var incomes = await _db.DailyLabStatistics.Where(s => s.Date >= monthStart).Select(s => s.Income).ToListAsync(ct);
        var income = incomes.Sum(m => m.Amount);
        return new NetworkOverviewDto(total, activeLabs, samples, income);
    }

    public async Task<IReadOnlyList<RepPerformanceRowDto>> GetPerformanceAsync(OrgScope scope, CancellationToken ct)
    {
        // Basic attainment = achieved/target from the latest commission rows; pace/on-track use the >=85% rule (BR-8).
        var reps = await _db.Representatives.AsNoTracking().Where(r => r.IsActive).ToListAsync(ct);
        var result = new List<RepPerformanceRowDto>();
        foreach (var r in reps)
        {
            var latest = await _db.Commissions.AsNoTracking().Where(c => c.RepresentativeId == r.Id)
                .OrderByDescending(c => c.Period).FirstOrDefaultAsync(ct);
            var achievementPct = latest is null || latest.Target == 0 ? 0 : Math.Round(latest.Achieved / latest.Target * 100m, 2);
            result.Add(new RepPerformanceRowDto(r.Id.Value, r.FullName, achievementPct, achievementPct, achievementPct >= 85m, r.Salary.Amount));
        }
        return result;
    }

    public async Task<LabHistoryDto?> GetLabHistoryAsync(Guid labId, bool canSeeEncrypted, CancellationToken ct)
    {
        var lab = await _db.Laboratories.AsNoTracking().FirstOrDefaultAsync(l => l.Id == new LaboratoryId(labId), ct);
        if (lab is null) return null;
        var points = await _db.VisitHistory.AsNoTracking().Where(h => h.LaboratoryId == lab.Id)
            .OrderByDescending(h => h.VisitDate).Take(90)
            .Select(h => new LabHistoryPointDto(h.VisitDate, h.SampleCount ?? 0, h.Status)).ToListAsync(ct);
        return new LabHistoryDto(DisplayCode.For(lab.Code.Value, canSeeEncrypted), lab.Name, points);
    }

    public async Task<IReadOnlyList<RepIntervalDto>> GetRepIntervalsAsync(OrgScope scope, CancellationToken ct)
    {
        // Cycle-time interval = avg hours between check-in and receipt per collector rep.
        var rows = await _db.DailyVisits.AsNoTracking()
            .Where(v => v.CheckedInAt != null && v.ReceivedAt != null && v.CollectorRepId != null)
            .Select(v => new { v.CollectorRepId, v.CheckedInAt, v.ReceivedAt }).ToListAsync(ct);
        var byRep = rows.GroupBy(x => x.CollectorRepId!.Value)
            .Select(g => new { RepId = g.Key, Avg = g.Average(x => (x.ReceivedAt!.Value - x.CheckedInAt!.Value).TotalHours) })
            .ToList();
        var reps = await _db.Representatives.AsNoTracking().ToListAsync(ct);
        return byRep.Select(x => new RepIntervalDto(x.RepId.Value,
            reps.FirstOrDefault(r => r.Id == x.RepId)?.FullName ?? "", Math.Round(x.Avg, 2))).ToList();
    }
}
