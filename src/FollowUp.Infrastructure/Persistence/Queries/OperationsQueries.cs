using FollowUp.Application.Features.DailyBoard.Contracts;
using FollowUp.Application.Features.LabCheckIn;
using FollowUp.Application.Features.Outsource;
using FollowUp.Application.Features.SampleTracking;
using FollowUp.Application.Features.Transfers;
using FollowUp.Domain.Common;
using FollowUp.Domain.Identity;
using FollowUp.Domain.Operations;
using FollowUp.Domain.Representatives;
using Microsoft.EntityFrameworkCore;

namespace FollowUp.Infrastructure.Persistence.Queries;

internal sealed class DailyBoardQueries : IDailyBoardQueries
{
    private readonly FollowUpDbContext _db;
    public DailyBoardQueries(FollowUpDbContext db) => _db = db;

    public async Task<IReadOnlyList<BoardItemDto>> GetBoardAsync(
        DateOnly start, DateOnly end, Guid? repId, string? status, OrgScope scope, bool canSeeEncrypted, CancellationToken ct)
    {
        var scopedLabs = _db.Laboratories.ApplyScope(scope).Select(l => l.Id);
        var q = _db.DailyVisits.AsNoTracking()
            .Where(v => v.VisitDate >= start && v.VisitDate <= end && scopedLabs.Contains(v.LaboratoryId));

        if (repId is { } rid)
            q = q.Where(v => v.CollectorRepId == new RepresentativeId(rid));
        if (status is { Length: > 0 })
        {
            // "Visited" spans the collected + received states; other pills match exactly (converted equality translates).
            if (status == "Visited")
                q = q.Where(v => v.Status == VisitStatus.Visited || v.Status == VisitStatus.Received);
            else
                q = q.Where(v => v.Status == Enumeration.FromName<VisitStatus>(status));
        }

        var rows = await (from v in q
                          join l in _db.Laboratories.AsNoTracking() on v.LaboratoryId equals l.Id
                          orderby v.VisitDate, v.ScheduledTime
                          select new { v.Id, v.LaboratoryId, l.Code, l.Name, l.Branch, l.Governorate, l.City, l.Area,
                              v.CollectorRepId, v.VisitDate, v.ScheduledTime, v.Status, v.SampleCount, v.AdminChecked, v.TransferConfirmedAt })
                         .ToListAsync(ct);

        var repIds = rows.Where(r => r.CollectorRepId != null).Select(r => r.CollectorRepId!.Value).Distinct().ToList();
        var repName = (await _db.Representatives.AsNoTracking().Where(r => repIds.Contains(r.Id))
            .Select(r => new { r.Id, r.FullName }).ToListAsync(ct)).ToDictionary(r => r.Id, r => r.FullName);

        return rows.Select(r => new BoardItemDto(
            r.Id.Value, r.LaboratoryId.Value, DisplayCode.For(r.Code.Value, canSeeEncrypted), r.Code.Value, r.Name,
            r.CollectorRepId != null ? r.CollectorRepId.Value.Value : (Guid?)null,
            r.CollectorRepId != null && repName.TryGetValue(r.CollectorRepId.Value, out var n) ? n : null,
            r.Branch, r.Governorate, r.City, r.Area,
            r.VisitDate, r.ScheduledTime.ToString("HH:mm"), r.Status.Name, r.SampleCount, r.AdminChecked,
            r.TransferConfirmedAt != null)).ToList();
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

    public async Task<IReadOnlyList<TransferItemDto>> GetTransferableAsync(DateOnly start, DateOnly end, OrgScope scope, bool canSeeEncrypted, CancellationToken ct)
    {
        var scopedLabs = _db.Laboratories.ApplyScope(scope).Select(l => l.Id);
        var visited = VisitStatus.Visited;
        var rows = await (from v in _db.DailyVisits.AsNoTracking()
                          where v.Status == visited && v.VisitDate >= start && v.VisitDate <= end && scopedLabs.Contains(v.LaboratoryId)
                          join l in _db.Laboratories.AsNoTracking() on v.LaboratoryId equals l.Id
                          orderby v.VisitDate, v.ScheduledTime
                          select new { v.Id, v.LaboratoryId, l.Code, l.Name, l.Branch, l.Governorate, l.City, l.Area,
                              v.VisitDate, v.ScheduledTime, v.CollectorRepId, v.SampleCount, v.TransferConfirmedAt,
                              v.TransferRepId, v.Transfer })
                         .ToListAsync(ct);

        var repIds = rows.SelectMany(r => new[] { r.CollectorRepId, r.TransferRepId }).Where(x => x != null).Select(x => x!.Value).Distinct().ToList();
        var repName = (await _db.Representatives.AsNoTracking().Where(r => repIds.Contains(r.Id))
            .Select(r => new { r.Id, r.FullName }).ToListAsync(ct)).ToDictionary(r => r.Id, r => r.FullName);
        string? Name(RepresentativeId? id) => id != null && repName.TryGetValue(id.Value, out var n) ? n : null;

        return rows.Select(r => new TransferItemDto(
            r.Id.Value, r.LaboratoryId.Value, DisplayCode.For(r.Code.Value, canSeeEncrypted), r.Code.Value, r.Name,
            r.Branch, r.Governorate, r.City, r.Area,
            r.VisitDate, r.ScheduledTime.ToString("HH:mm"), Name(r.CollectorRepId), r.SampleCount,
            r.TransferConfirmedAt != null, r.Transfer?.DriverName, r.Transfer?.DriverMobile, r.Transfer?.CarPlate,
            r.TransferRepId != null ? r.TransferRepId.Value.Value : (Guid?)null, Name(r.TransferRepId),
            r.TransferConfirmedAt?.ToString("yyyy-MM-dd HH:mm"))).ToList();
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
