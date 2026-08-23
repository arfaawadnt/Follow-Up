using FollowUp.Application.Common.Abstractions;
using FollowUp.Application.Features.DailyBoard.Contracts;
using FollowUp.Domain.Common;
using FollowUp.Domain.Laboratories;
using FollowUp.Domain.Operations;
using FollowUp.Domain.Statistics;
using FollowUp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FollowUp.Infrastructure.Jobs;

/// <summary>
/// The daily-board background operations (SRS FR-5, Workflows §2/§4). Orchestration only — the invariants
/// live in the aggregates (<see cref="DailyVisit"/>, <see cref="VisitHistory"/>, <see cref="MonthlySample"/>).
/// The evening missed-sweep runs on its own trigger and MUST complete before the midnight archive so a
/// still-Pending visit is never archived verbatim as Pending (closes JOBS-001).
/// </summary>
public sealed class BoardService : IBoardScheduler
{
    private readonly FollowUpDbContext _db;
    private readonly IClock _clock;
    private readonly ILogger<BoardService> _logger;

    public BoardService(FollowUpDbContext db, IClock clock, ILogger<BoardService> logger)
    {
        _db = db;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>Marks every still-Pending visit for <paramref name="date"/> (default Cairo today) as Missed.</summary>
    public async Task<int> RunMissedSweepAsync(DateOnly? date = null, CancellationToken ct = default)
    {
        var day = date ?? _clock.CairoToday;
        var pending = VisitStatus.Pending;
        var visits = await _db.DailyVisits.Where(v => v.VisitDate == day && v.Status == pending).ToListAsync(ct);
        foreach (var visit in visits) visit.Miss();
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Missed-sweep marked {Count} visits missed for {Day}", visits.Count, day);
        return visits.Count;
    }

    /// <summary>Midnight roll-over: sweep, archive yesterday, roll verified samples, generate today's board.</summary>
    public async Task RunRolloverAsync(CancellationToken ct = default)
    {
        var today = _clock.CairoToday;
        var yesterday = today.AddDays(-1);
        var now = _clock.UtcNow;

        // Safety: ensure yesterday's still-Pending visits are Missed before archiving verbatim (JOBS-001).
        await RunMissedSweepAsync(yesterday, ct);

        var toArchive = await _db.DailyVisits.Where(v => v.VisitDate == yesterday).ToListAsync(ct);
        var period = YearMonth.From(yesterday);
        foreach (var visit in toArchive)
        {
            _db.VisitHistory.Add(VisitHistory.ArchiveFrom(visit, now));
            if (visit.RollsToMonthly && visit.SampleCount is > 0)
                await RollToMonthlyAsync(visit, period, ct);
        }
        _db.DailyVisits.RemoveRange(toArchive);

        await GenerateBoardAsync(today, ct);

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Board roll-over archived {Archived} visits and generated today's board", toArchive.Count);
    }

    /// <summary>BR-3 intra-day reconcile — generates any missing visits for today's board (<see cref="IBoardScheduler"/>).</summary>
    public Task<int> ReconcileTodayAsync(CancellationToken ct = default) => GenerateBoardAsync(_clock.CairoToday, ct);

    /// <summary>
    /// BR-3 lab-scoped reconcile after a schedule change (<see cref="IBoardScheduler"/>). Aligns the lab's
    /// visits for today with its current schedule: adds newly scheduled times and prunes still-Pending visits
    /// whose time is no longer scheduled. Visited/Missed/Received visits are preserved (they carry real activity).
    /// </summary>
    public async Task<BoardReconciliation> ReconcileLabTodayAsync(LaboratoryId laboratoryId, CancellationToken ct = default)
    {
        var today = _clock.CairoToday;

        var lab = await _db.Laboratories.FirstOrDefaultAsync(l => l.Id == laboratoryId, ct);
        if (lab is null) return new BoardReconciliation(0, 0);

        // The times the lab should be visited today (empty when not schedulable or today is not a work day).
        var desiredTimes = lab.Status.IsSchedulable && lab.Schedule.WorkDays.Contains(today.DayOfWeek)
            ? lab.Schedule.VisitTimes.ToHashSet()
            : new HashSet<TimeOnly>();

        var existing = await _db.DailyVisits
            .Where(v => v.LaboratoryId == laboratoryId && v.VisitDate == today)
            .ToListAsync(ct);

        // Prune stale Pending visits (a removed time, or a day/lab that is no longer schedulable). Never
        // touch a visit with real activity — undo already refuses that, and history/monthly totals depend on it.
        var pending = VisitStatus.Pending;
        var stale = existing.Where(v => v.Status == pending && !desiredTimes.Contains(v.ScheduledTime)).ToList();
        _db.DailyVisits.RemoveRange(stale);

        // Add newly scheduled times that have no visit at all yet (in any status).
        var present = existing.Select(v => v.ScheduledTime).ToHashSet();
        var added = 0;
        foreach (var time in desiredTimes)
        {
            if (present.Contains(time)) continue;
            _db.DailyVisits.Add(DailyVisit.Schedule(laboratoryId, lab.CollectorRepId, today, time));
            added++;
        }

        await _db.SaveChangesAsync(ct);
        return new BoardReconciliation(added, stale.Count);
    }

    /// <summary>Generates the board for <paramref name="date"/> from each schedulable lab's schedule (BR-3 intra-day too).</summary>
    public async Task<int> GenerateBoardAsync(DateOnly date, CancellationToken ct = default)
    {
        var active = LaboratoryStatus.Active;
        var pending = LaboratoryStatus.Pending;
        var isNew = LaboratoryStatus.New;
        var labs = await _db.Laboratories
            .Where(l => l.Status == active || l.Status == pending || l.Status == isNew)
            .ToListAsync(ct);

        var existing = (await _db.DailyVisits.Where(v => v.VisitDate == date).Select(v => v.LaboratoryId).ToListAsync(ct)).ToHashSet();

        var created = 0;
        foreach (var lab in labs)
        {
            if (existing.Contains(lab.Id)) continue; // don't duplicate an already-scheduled lab
            if (!lab.Schedule.WorkDays.Contains(date.DayOfWeek)) continue;
            foreach (var time in lab.Schedule.VisitTimes)
            {
                _db.DailyVisits.Add(DailyVisit.Schedule(lab.Id, lab.CollectorRepId, date, time));
                created++;
            }
        }
        await _db.SaveChangesAsync(ct); // persist so callers (roll-over, BR-3 intra-day) don't need to
        return created;
    }

    private async Task RollToMonthlyAsync(DailyVisit visit, YearMonth period, CancellationToken ct)
    {
        var monthly = await _db.MonthlySamples
            .FirstOrDefaultAsync(m => m.LaboratoryId == visit.LaboratoryId && m.Period == period, ct);
        if (monthly is null)
        {
            monthly = MonthlySample.Start(visit.LaboratoryId, visit.CollectorRepId, period);
            _db.MonthlySamples.Add(monthly);
        }
        monthly.Add(visit.SampleCount!.Value);
    }
}
