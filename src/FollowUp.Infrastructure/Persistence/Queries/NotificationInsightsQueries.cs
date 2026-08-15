using FollowUp.Application.Common.Abstractions;
using FollowUp.Application.Features.Insights;
using FollowUp.Application.Features.Notifications;
using FollowUp.Domain.Complaints;
using FollowUp.Domain.Identity;
using FollowUp.Domain.Laboratories;
using FollowUp.Domain.Operations;
using Microsoft.EntityFrameworkCore;

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
        var scopedLabs = _db.Laboratories.ApplyScope(scope).Select(l => l.Id);
        var active = LaboratoryStatus.Active;
        var open = ComplaintStatus.Open;
        var inProgress = ComplaintStatus.InProgress;
        var missed = VisitStatus.Missed;

        var activeLabs = await _db.Laboratories.ApplyScope(scope).CountAsync(l => l.Status == active, ct);
        var openComplaints = await _db.Complaints.Where(c => scopedLabs.Contains(c.LaboratoryId) && (c.Status == open || c.Status == inProgress)).CountAsync(ct);
        var samplesToday = await _db.DailyVisits.Where(v => v.VisitDate == today && scopedLabs.Contains(v.LaboratoryId) && v.SampleCount != null).SumAsync(v => (int?)v.SampleCount, ct) ?? 0;
        var missedToday = await _db.DailyVisits.Where(v => v.VisitDate == today && v.Status == missed && scopedLabs.Contains(v.LaboratoryId)).CountAsync(ct);

        var schedule = await (from v in _db.DailyVisits.AsNoTracking()
                              where v.VisitDate == today && scopedLabs.Contains(v.LaboratoryId)
                              join l in _db.Laboratories.AsNoTracking() on v.LaboratoryId equals l.Id
                              orderby v.ScheduledTime
                              select new { v.Id, l.Code, l.Name, v.Status, v.ScheduledTime }).Take(50).ToListAsync(ct);

        var unresolved = await (from c in _db.Complaints.AsNoTracking()
                                where scopedLabs.Contains(c.LaboratoryId) && c.Status != ComplaintStatus.Resolved
                                join l in _db.Laboratories.AsNoTracking() on c.LaboratoryId equals l.Id
                                orderby c.Number descending
                                select new { c.Id, c.Number, l.Code, c.Status }).Take(20).ToListAsync(ct);

        // Birthdays today among scoped labs' contacts (bounded post-filter on month/day).
        var contacts = await _db.Laboratories.ApplyScope(scope)
            .SelectMany(l => l.Contacts.Select(c => new { c.Name, c.Phone, c.Birthday, LabCode = l.Code }))
            .Where(x => x.Birthday != null).ToListAsync(ct);
        var birthdays = contacts
            .Where(x => x.Birthday!.Value.Month == today.Month && x.Birthday!.Value.Day == today.Day)
            .Select(x => new BirthdayDto(x.Name, DisplayCode.For(x.LabCode.Value, canSeeEncrypted), x.Phone))
            .ToList();

        return new DashboardDto(
            activeLabs, openComplaints, samplesToday, missedToday,
            schedule.Select(s => new ScheduleItemDto(s.Id.Value, DisplayCode.For(s.Code.Value, canSeeEncrypted), s.Name, s.Status.Name, s.ScheduledTime.ToString("HH:mm"))).ToList(),
            unresolved.Select(u => new UnresolvedComplaintDto(u.Id.Value, $"CMP-{u.Number}", DisplayCode.For(u.Code.Value, canSeeEncrypted), u.Status.Name)).ToList(),
            Array.Empty<RepProgressDto>(), // attainment engine (BR-8 rolling 90d) — refined in a later pass
            birthdays);
    }

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
