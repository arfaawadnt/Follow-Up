using FollowUp.Application.Features.DailyBoard.Contracts;
using FollowUp.Application.Features.LabCheckIn;
using FollowUp.Application.Features.Outsource;
using FollowUp.Application.Features.SampleTracking;
using FollowUp.Application.Features.Transfers;
using FollowUp.Domain.Identity;
using FollowUp.Domain.Operations;
using Microsoft.EntityFrameworkCore;

namespace FollowUp.Infrastructure.Persistence.Queries;

internal sealed class DailyBoardQueries : IDailyBoardQueries
{
    private readonly FollowUpDbContext _db;
    public DailyBoardQueries(FollowUpDbContext db) => _db = db;

    public async Task<IReadOnlyList<BoardItemDto>> GetBoardAsync(DateOnly date, OrgScope scope, bool canSeeEncrypted, CancellationToken ct)
    {
        var scopedLabs = _db.Laboratories.ApplyScope(scope).Select(l => l.Id);
        var rows = await (from v in _db.DailyVisits.AsNoTracking()
                          where v.VisitDate == date && scopedLabs.Contains(v.LaboratoryId)
                          join l in _db.Laboratories.AsNoTracking() on v.LaboratoryId equals l.Id
                          orderby v.ScheduledTime
                          select new { v.Id, v.LaboratoryId, l.Code, l.Name, v.CollectorRepId, v.VisitDate, v.ScheduledTime, v.Status, v.SampleCount, v.AdminChecked })
                         .ToListAsync(ct);

        return rows.Select(r => new BoardItemDto(
            r.Id.Value, r.LaboratoryId.Value, DisplayCode.For(r.Code.Value, canSeeEncrypted), r.Name,
            r.CollectorRepId != null ? r.CollectorRepId.Value.Value : (Guid?)null,
            r.VisitDate, r.ScheduledTime.ToString("HH:mm"), r.Status.Name, r.SampleCount, r.AdminChecked)).ToList();
    }

    public async Task<int?> GetSuggestedSampleCountAsync(Guid visitId, CancellationToken ct)
    {
        // Suggested value = the lab's most recent recorded sample count (SRS FR-5 helper).
        var visit = await _db.DailyVisits.AsNoTracking().FirstOrDefaultAsync(v => v.Id == new DailyVisitId(visitId), ct);
        if (visit is null) return null;
        return await _db.DailyVisits.AsNoTracking()
            .Where(v => v.LaboratoryId == visit.LaboratoryId && v.SampleCount != null)
            .OrderByDescending(v => v.VisitDate)
            .Select(v => v.SampleCount)
            .FirstOrDefaultAsync(ct);
    }
}

internal sealed class TransferQueries : ITransferQueries
{
    private readonly FollowUpDbContext _db;
    public TransferQueries(FollowUpDbContext db) => _db = db;

    public async Task<IReadOnlyList<TransferItemDto>> GetTransferableAsync(OrgScope scope, bool canSeeEncrypted, CancellationToken ct)
    {
        var scopedLabs = _db.Laboratories.ApplyScope(scope).Select(l => l.Id);
        var visited = VisitStatus.Visited;
        var rows = await (from v in _db.DailyVisits.AsNoTracking()
                          where v.Status == visited && v.TransferConfirmedAt == null && scopedLabs.Contains(v.LaboratoryId)
                          join l in _db.Laboratories.AsNoTracking() on v.LaboratoryId equals l.Id
                          select new { v.Id, v.LaboratoryId, l.Code, l.Name, v.VisitDate, v.SampleCount })
                         .ToListAsync(ct);
        return rows.Select(r => new TransferItemDto(
            r.Id.Value, r.LaboratoryId.Value, DisplayCode.For(r.Code.Value, canSeeEncrypted), r.Name, r.VisitDate, r.SampleCount)).ToList();
    }
}

internal sealed class LabCheckInQueries : ILabCheckInQueries
{
    private readonly FollowUpDbContext _db;
    public LabCheckInQueries(FollowUpDbContext db) => _db = db;

    public async Task<IReadOnlyList<ReceivingItemDto>> GetAwaitingReceiptAsync(OrgScope scope, bool canSeeEncrypted, CancellationToken ct)
    {
        var scopedLabs = _db.Laboratories.ApplyScope(scope).Select(l => l.Id);
        var visited = VisitStatus.Visited;
        var rows = await (from v in _db.DailyVisits.AsNoTracking()
                          where v.Status == visited && v.TransferConfirmedAt != null && scopedLabs.Contains(v.LaboratoryId)
                          join l in _db.Laboratories.AsNoTracking() on v.LaboratoryId equals l.Id
                          select new { v.Id, v.LaboratoryId, l.Code, l.Name, v.VisitDate, v.SampleCount })
                         .ToListAsync(ct);
        return rows.Select(r => new ReceivingItemDto(
            r.Id.Value, r.LaboratoryId.Value, DisplayCode.For(r.Code.Value, canSeeEncrypted), r.Name, r.VisitDate, r.SampleCount)).ToList();
    }
}

internal sealed class OutsourceQueries : IOutsourceQueries
{
    private readonly FollowUpDbContext _db;
    public OutsourceQueries(FollowUpDbContext db) => _db = db;

    public async Task<IReadOnlyList<OutsourceSampleDto>> ListAsync(OrgScope scope, bool canSeeEncrypted, DateOnly? date, CancellationToken ct)
    {
        var scopedLabs = _db.Laboratories.ApplyScope(scope).Select(l => l.Id);
        var q = from o in _db.OutsourceSamples.AsNoTracking()
                where scopedLabs.Contains(o.LaboratoryId) && (date == null || o.VisitDate == date)
                join l in _db.Laboratories.AsNoTracking() on o.LaboratoryId equals l.Id
                select new { o.Id, o.LaboratoryId, l.Code, o.VisitDate, o.DestinationLab, o.Quantity, o.Status };
        var rows = await q.ToListAsync(ct);
        return rows.Select(r => new OutsourceSampleDto(
            r.Id.Value, r.LaboratoryId.Value, DisplayCode.For(r.Code.Value, canSeeEncrypted), r.VisitDate,
            r.DestinationLab, r.Quantity, r.Status.Name)).ToList();
    }
}

internal sealed class SampleTrackingQueries : ISampleTrackingQueries
{
    private readonly FollowUpDbContext _db;
    public SampleTrackingQueries(FollowUpDbContext db) => _db = db;

    public async Task<IReadOnlyList<SampleTrackingDto>> ListAsync(DateOnly date, OrgScope scope, CancellationToken ct)
    {
        // Area-scoped (scope.Areas). Wildcard => all areas.
        var q = _db.SampleTracking.AsNoTracking().Where(s => s.Date == date);
        if (!scope.Areas.Contains(OrgScope.Wildcard))
        {
            var areas = scope.Areas.ToList();
            q = q.Where(s => areas.Contains(s.Area));
        }
        var rows = await q.OrderBy(s => s.Area).ToListAsync(ct);
        return rows.Select(s => new SampleTrackingDto(
            s.Id.Value, s.Area, s.Date, s.Count,
            s.DataEntry != null ? s.DataEntry.User : null, s.DataEntry != null ? s.DataEntry.At : null,
            s.Review != null ? s.Review.User : null, s.Review != null ? s.Review.At : null,
            s.Sort != null ? s.Sort.User : null, s.Sort != null ? s.Sort.At : null,
            s.IsComplete)).ToList();
    }

    public async Task<IReadOnlyList<SampleLifecycleReportRowDto>> ReportAsync(DateOnly from, DateOnly to, OrgScope scope, CancellationToken ct)
    {
        var q = _db.SampleTracking.AsNoTracking().Where(s => s.Date >= from && s.Date <= to);
        if (!scope.Areas.Contains(OrgScope.Wildcard))
        {
            var areas = scope.Areas.ToList();
            q = q.Where(s => areas.Contains(s.Area));
        }
        var rows = await q.OrderBy(s => s.Date).ThenBy(s => s.Area).ToListAsync(ct);
        return rows.Select(s => new SampleLifecycleReportRowDto(
            s.Area, s.Date, s.Count,
            s.Sort != null ? "Sorted" : s.Review != null ? "Reviewed" : s.DataEntry != null ? "Entered" : "Empty")).ToList();
    }
}
