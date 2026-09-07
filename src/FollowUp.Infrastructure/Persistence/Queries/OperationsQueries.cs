using FollowUp.Application.Common.Abstractions;
using FollowUp.Application.Common.Models;
using FollowUp.Application.Features.DailyBoard.Attachments;
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

/// <summary>Loads visit attachments (keyed by the stable visit id) for a set of visits — shared by the board,
/// transfer, check-in and sample-lifecycle read queries so each row can surface its documents.</summary>
internal static class AttachmentEnricher
{
    public static async Task<Dictionary<Guid, IReadOnlyList<AttachmentRefDto>>> LoadAsync(
        FollowUpDbContext db, IEnumerable<Guid> visitIds, CancellationToken ct)
    {
        var ids = visitIds.Distinct().ToList();
        if (ids.Count == 0) return new();
        var rows = await db.VisitAttachments.AsNoTracking()
            .Where(a => a.VisitId != null && ids.Contains(a.VisitId!.Value))
            .Select(a => new { VisitId = a.VisitId!.Value, a.Id, a.FileName, a.ContentType, a.SizeBytes })
            .ToListAsync(ct);
        return rows.GroupBy(a => a.VisitId).ToDictionary(
            g => g.Key,
            g => (IReadOnlyList<AttachmentRefDto>)g
                .Select(a => new AttachmentRefDto(a.Id.Value, a.FileName, a.ContentType, a.SizeBytes)).ToList());
    }
}

internal sealed class DailyBoardQueries : IDailyBoardQueries
{
    private readonly FollowUpDbContext _db;
    public DailyBoardQueries(FollowUpDbContext db) => _db = db;

    public async Task<IReadOnlyList<BoardItemDto>> GetBoardAsync(
        DateOnly start, DateOnly end, Guid? repId, string? status, OrgScope scope, bool canSeeEncrypted, CancellationToken ct)
    {
        var scopedLabs = _db.Laboratories.ApplyScope(scope).Select(l => l.Id);

        // Live board — the actionable rows (only today survives the midnight roll-over).
        var q = _db.DailyVisits.AsNoTracking()
            .Where(v => v.VisitDate >= start && v.VisitDate <= end && scopedLabs.Contains(v.LaboratoryId));
        if (repId is { } rid)
            q = q.Where(v => v.CollectorRepId == new RepresentativeId(rid));
        if (status is { Length: > 0 })
        {
            // "Visited" spans the collected + received states; other pills match exactly (converted equality translates).
            if (status == VisitStatus.Visited.Name) // BRD-11: bound to the enumeration, not a raw literal
                q = q.Where(v => v.Status == VisitStatus.Visited || v.Status == VisitStatus.Received);
            else
                q = q.Where(v => v.Status == Enumeration.FromName<VisitStatus>(status));
        }
        var live = await (from v in q
                          join l in _db.Laboratories.AsNoTracking() on v.LaboratoryId equals l.Id
                          select new { v.Id, v.LaboratoryId, l.Code, l.IsEncrypted, l.Name, l.Branch, l.Governorate, l.City, l.Area,
                              v.CollectorRepId, v.VisitDate, v.ScheduledTime, v.Status, v.SampleCount, v.CheckedInAt, v.AdminChecked, v.TransferConfirmedAt })
                         .ToListAsync(ct);

        // Archived days (rolled off the live board into visit_history) — read-only history.
        var hq = _db.VisitHistory.AsNoTracking()
            .Where(h => h.VisitDate >= start && h.VisitDate <= end && scopedLabs.Contains(h.LaboratoryId));
        if (repId is { } ridH)
            hq = hq.Where(h => h.CollectorRepId == new RepresentativeId(ridH));
        if (status is { Length: > 0 })
        {
            if (status == VisitStatus.Visited.Name)
                hq = hq.Where(h => h.Status == VisitStatus.Visited.Name || h.Status == VisitStatus.Received.Name);
            else
                hq = hq.Where(h => h.Status == status);
        }
        var archived = await (from h in hq
                              join l in _db.Laboratories.AsNoTracking() on h.LaboratoryId equals l.Id
                              select new { h.OriginalVisitId, h.LaboratoryId, l.Code, l.IsEncrypted, l.Name, l.Branch, l.Governorate, l.City, l.Area,
                                  h.CollectorRepId, h.VisitDate, h.ScheduledTime, h.Status, h.SampleCount, h.CheckedInAt, h.AdminChecked, h.TransferConfirmedAt })
                             .ToListAsync(ct);

        var repIds = live.Where(r => r.CollectorRepId != null).Select(r => r.CollectorRepId!.Value)
            .Concat(archived.Where(r => r.CollectorRepId != null).Select(r => r.CollectorRepId!.Value)).Distinct().ToList();
        var repName = (await _db.Representatives.AsNoTracking().Where(r => repIds.Contains(r.Id))
            .Select(r => new { r.Id, r.FullName }).ToListAsync(ct)).ToDictionary(r => r.Id, r => r.FullName);
        string? RepOf(RepresentativeId? id) => id != null && repName.TryGetValue(id.Value, out var n) ? n : null;

        var liveDtos = live.Select(r => new BoardItemDto(
            r.Id.Value, r.LaboratoryId.Value, DisplayCode.For(r.Code.Value, r.IsEncrypted, canSeeEncrypted), r.Name,
            r.CollectorRepId != null ? r.CollectorRepId.Value.Value : (Guid?)null, RepOf(r.CollectorRepId),
            r.Branch, r.Governorate, r.City, r.Area,
            r.VisitDate, r.ScheduledTime.ToString("HH:mm"), r.Status.Name, r.SampleCount,
            r.CheckedInAt?.ToString("o"), r.AdminChecked, r.TransferConfirmedAt != null, Archived: false));
        var archDtos = archived.Select(r => new BoardItemDto(
            r.OriginalVisitId.Value, r.LaboratoryId.Value, DisplayCode.For(r.Code.Value, r.IsEncrypted, canSeeEncrypted), r.Name,
            r.CollectorRepId != null ? r.CollectorRepId.Value.Value : (Guid?)null, RepOf(r.CollectorRepId),
            r.Branch, r.Governorate, r.City, r.Area,
            r.VisitDate, r.ScheduledTime != null ? r.ScheduledTime.Value.ToString("HH:mm") : "—", r.Status, r.SampleCount,
            r.CheckedInAt?.ToString("o"), r.AdminChecked, r.TransferConfirmedAt != null, Archived: true));

        var result = liveDtos.Concat(archDtos).OrderBy(d => d.VisitDate).ThenBy(d => d.ScheduledTime).ToList();
        var atts = await AttachmentEnricher.LoadAsync(_db, result.Select(d => d.VisitId), ct);
        return atts.Count == 0 ? result
            : result.Select(d => atts.TryGetValue(d.VisitId, out var a) ? d with { Attachments = a } : d).ToList();
    }

    public async Task<int?> GetSuggestedSampleCountAsync(Guid visitId, OrgScope scope, CancellationToken ct)
    {
        // Suggested value = the lab's most recent recorded sample count (SRS FR-5 helper). Scope the lookup:
        // a visit whose lab is outside the caller's org scope is not resolvable here (SRS SCOPE-READ).
        var visit = await (from v in _db.DailyVisits.AsNoTracking()
                           join l in _db.Laboratories.ApplyScope(scope) on v.LaboratoryId equals l.Id
                           where v.Id == new DailyVisitId(visitId)
                           select v).FirstOrDefaultAsync(ct);
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

        // Live (today) — actionable.
        var live = await (from v in _db.DailyVisits.AsNoTracking()
                          where v.Status == visited && v.VisitDate >= start && v.VisitDate <= end && scopedLabs.Contains(v.LaboratoryId)
                          join l in _db.Laboratories.AsNoTracking() on v.LaboratoryId equals l.Id
                          select new { v.Id, v.LaboratoryId, l.Code, l.IsEncrypted, l.Name, l.Branch, l.Governorate, l.City, l.Area,
                              v.VisitDate, Time = (TimeOnly?)v.ScheduledTime, v.CollectorRepId, v.SampleCount, v.TransferConfirmedAt,
                              v.TransferRepId, v.Transfer })
                         .ToListAsync(ct);

        // Archived (rolled-off days) — read-only history.
        var archived = await (from h in _db.VisitHistory.AsNoTracking()
                              where h.Status == VisitStatus.Visited.Name && h.VisitDate >= start && h.VisitDate <= end && scopedLabs.Contains(h.LaboratoryId)
                              join l in _db.Laboratories.AsNoTracking() on h.LaboratoryId equals l.Id
                              select new { Id = h.OriginalVisitId, h.LaboratoryId, l.Code, l.IsEncrypted, l.Name, l.Branch, l.Governorate, l.City, l.Area,
                                  h.VisitDate, Time = h.ScheduledTime, h.CollectorRepId, h.SampleCount, h.TransferConfirmedAt,
                                  h.TransferRepId, h.DriverName, h.DriverMobile, h.CarPlate })
                             .ToListAsync(ct);

        var repIds = live.SelectMany(r => new[] { r.CollectorRepId, r.TransferRepId })
            .Concat(archived.SelectMany(r => new[] { r.CollectorRepId, r.TransferRepId }))
            .Where(x => x != null).Select(x => x!.Value).Distinct().ToList();
        var repName = (await _db.Representatives.AsNoTracking().Where(r => repIds.Contains(r.Id))
            .Select(r => new { r.Id, r.FullName }).ToListAsync(ct)).ToDictionary(r => r.Id, r => r.FullName);
        string? Name(RepresentativeId? id) => id != null && repName.TryGetValue(id.Value, out var n) ? n : null;
        TransferItemDto Map(Guid vid, Guid labId, FollowUp.Domain.Laboratories.LabCode code, bool enc, string name,
            string? br, string? gov, string? city, string? area, DateOnly date, TimeOnly? time, RepresentativeId? collector,
            int? samples, DateTimeOffset? tConf, RepresentativeId? tRep, string? dName, string? dMob, string? plate, bool archivedRow) =>
            new(vid, labId, DisplayCode.For(code.Value, enc, canSeeEncrypted), name, br, gov, city, area,
                date, time != null ? time.Value.ToString("HH:mm") : "—", Name(collector), samples,
                tConf != null, dName, dMob, plate, tRep != null ? tRep.Value.Value : (Guid?)null, Name(tRep),
                tConf?.ToString("o"), Archived: archivedRow);

        var liveDtos = live.Select(r => Map(r.Id.Value, r.LaboratoryId.Value, r.Code, r.IsEncrypted, r.Name, r.Branch, r.Governorate, r.City, r.Area,
            r.VisitDate, r.Time, r.CollectorRepId, r.SampleCount, r.TransferConfirmedAt, r.TransferRepId, r.Transfer?.DriverName, r.Transfer?.DriverMobile, r.Transfer?.CarPlate, false));
        var archDtos = archived.Select(r => Map(r.Id.Value, r.LaboratoryId.Value, r.Code, r.IsEncrypted, r.Name, r.Branch, r.Governorate, r.City, r.Area,
            r.VisitDate, r.Time, r.CollectorRepId, r.SampleCount, r.TransferConfirmedAt, r.TransferRepId, r.DriverName, r.DriverMobile, r.CarPlate, true));

        var result = liveDtos.Concat(archDtos).OrderBy(d => d.VisitDate).ThenBy(d => d.VisitTime).ToList();
        var atts = await AttachmentEnricher.LoadAsync(_db, result.Select(d => d.VisitId), ct);
        return atts.Count == 0 ? result
            : result.Select(d => atts.TryGetValue(d.VisitId, out var a) ? d with { Attachments = a } : d).ToList();
    }
}

internal sealed class LabCheckInQueries : ILabCheckInQueries
{
    private readonly FollowUpDbContext _db;
    public LabCheckInQueries(FollowUpDbContext db) => _db = db;

    public async Task<IReadOnlyList<ReceivingItemDto>> GetAwaitingReceiptAsync(DateOnly start, DateOnly end, OrgScope scope, bool canSeeEncrypted, CancellationToken ct)
    {
        var scopedLabs = _db.Laboratories.ApplyScope(scope).Select(l => l.Id);
        var visited = VisitStatus.Visited;
        var received = VisitStatus.Received;

        // Live (today) — actionable.
        var live = await (from v in _db.DailyVisits.AsNoTracking()
                          where v.TransferConfirmedAt != null && (v.Status == visited || v.Status == received)
                                && v.VisitDate >= start && v.VisitDate <= end && scopedLabs.Contains(v.LaboratoryId)
                          join l in _db.Laboratories.AsNoTracking() on v.LaboratoryId equals l.Id
                          select new { v.Id, v.LaboratoryId, l.Code, l.IsEncrypted, l.Name, l.Branch, l.Governorate, l.City, l.Area,
                              v.VisitDate, Time = (TimeOnly?)v.ScheduledTime, v.CollectorRepId, v.SampleCount, IsReceived = v.Status == received, v.TransferRepId,
                              v.TransferConfirmedAt, v.ReceivedAt })
                         .ToListAsync(ct);

        // Archived (rolled-off days) — read-only history.
        var archived = await (from h in _db.VisitHistory.AsNoTracking()
                              where h.TransferConfirmedAt != null && (h.Status == VisitStatus.Visited.Name || h.Status == VisitStatus.Received.Name)
                                    && h.VisitDate >= start && h.VisitDate <= end && scopedLabs.Contains(h.LaboratoryId)
                              join l in _db.Laboratories.AsNoTracking() on h.LaboratoryId equals l.Id
                              select new { Id = h.OriginalVisitId, h.LaboratoryId, l.Code, l.IsEncrypted, l.Name, l.Branch, l.Governorate, l.City, l.Area,
                                  h.VisitDate, Time = h.ScheduledTime, h.CollectorRepId, h.SampleCount, IsReceived = h.Status == VisitStatus.Received.Name, h.TransferRepId,
                                  h.TransferConfirmedAt, h.ReceivedAt })
                             .ToListAsync(ct);

        var repIds = live.SelectMany(r => new[] { r.TransferRepId, r.CollectorRepId })
            .Concat(archived.SelectMany(r => new[] { r.TransferRepId, r.CollectorRepId }))
            .Where(x => x != null).Select(x => x!.Value).Distinct().ToList();
        var repName = (await _db.Representatives.AsNoTracking().Where(r => repIds.Contains(r.Id))
            .Select(r => new { r.Id, r.FullName }).ToListAsync(ct)).ToDictionary(r => r.Id, r => r.FullName);
        string? Name(RepresentativeId? id) => id != null && repName.TryGetValue(id.Value, out var n) ? n : null;
        // "Transferred" is a display-only status (no VisitStatus member); "Received" is bound to the enum (BRD-11).
        ReceivingItemDto Map(Guid vid, Guid labId, FollowUp.Domain.Laboratories.LabCode code, bool enc, string name,
            string? br, string? gov, string? city, string? area, DateOnly date, TimeOnly? time, RepresentativeId? collector,
            int? samples, bool isReceived, RepresentativeId? tRep, DateTimeOffset? tConf, DateTimeOffset? recv, bool archivedRow) =>
            new(vid, labId, DisplayCode.For(code.Value, enc, canSeeEncrypted), name, br, gov, city, area,
                date, time != null ? time.Value.ToString("HH:mm") : "—", Name(collector), samples,
                isReceived ? VisitStatus.Received.Name : "Transferred", Name(tRep),
                tConf?.ToString("o"), recv?.ToString("o"), Archived: archivedRow);

        var liveDtos = live.Select(r => Map(r.Id.Value, r.LaboratoryId.Value, r.Code, r.IsEncrypted, r.Name, r.Branch, r.Governorate, r.City, r.Area,
            r.VisitDate, r.Time, r.CollectorRepId, r.SampleCount, r.IsReceived, r.TransferRepId, r.TransferConfirmedAt, r.ReceivedAt, false));
        var archDtos = archived.Select(r => Map(r.Id.Value, r.LaboratoryId.Value, r.Code, r.IsEncrypted, r.Name, r.Branch, r.Governorate, r.City, r.Area,
            r.VisitDate, r.Time, r.CollectorRepId, r.SampleCount, r.IsReceived, r.TransferRepId, r.TransferConfirmedAt, r.ReceivedAt, true));

        var result = liveDtos.Concat(archDtos).OrderBy(d => d.VisitDate).ThenBy(d => d.VisitTime).ToList();
        var atts = await AttachmentEnricher.LoadAsync(_db, result.Select(d => d.VisitId), ct);
        return atts.Count == 0 ? result
            : result.Select(d => atts.TryGetValue(d.VisitId, out var a) ? d with { Attachments = a } : d).ToList();
    }
}

internal sealed class VisitAttachmentQueries : IVisitAttachmentQueries
{
    private readonly FollowUpDbContext _db;
    private readonly IAttachmentStorage _storage;
    public VisitAttachmentQueries(FollowUpDbContext db, IAttachmentStorage storage) { _db = db; _storage = storage; }

    public async Task<VisitAttachmentContent?> GetForViewingAsync(Guid id, OrgScope scope, CancellationToken ct)
    {
        var attId = new VisitAttachmentId(id);
        var row = await _db.VisitAttachments.AsNoTracking()
            .Where(a => a.Id == attId && a.LaboratoryId != null)
            .Select(a => new { a.StoredName, a.FileName, a.ContentType, a.LaboratoryId })
            .FirstOrDefaultAsync(ct);
        if (row is null) return null;
        // Org-scope: the caller may fetch the document only if the owning lab is within their scope.
        var inScope = await _db.Laboratories.ApplyScope(scope).AnyAsync(l => l.Id == row.LaboratoryId, ct);
        if (!inScope) return null;
        var bytes = await _storage.ReadAsync(row.StoredName, ct);
        return bytes is null ? null : new VisitAttachmentContent(row.FileName, row.ContentType, bytes);
    }
}

internal sealed class OutsourceQueries : IOutsourceQueries
{
    private readonly FollowUpDbContext _db;
    public OutsourceQueries(FollowUpDbContext db) => _db = db;

    public async Task<IReadOnlyList<OutsourceSampleDto>> ListAsync(DateOnly start, DateOnly end, OrgScope scope, bool canSeeEncrypted, CancellationToken ct)
    {
        var (samples, labs) = await LoadScopedAsync(start, end, scope, ct);
        return samples.OrderByDescending(s => s.VisitDate).Select(s =>
        {
            var lab = labs[s.LaboratoryId];
            return new OutsourceSampleDto(
                s.Id.Value, s.LaboratoryId.Value, DisplayCode.For(lab.Code.Value, lab.IsEncrypted, canSeeEncrypted), lab.Name,
                s.VisitDate, s.DestinationLab, s.Quantity, s.Status.Name, s.Notes,
                s.Tests.Select(t => new OutsourceTestDto(t.Id.Value, t.TestCode, t.TestName, t.SampleVolume,
                    t.TestFees.Amount, t.OutsourceFees.Amount, t.NetRevenue.Amount)).ToList());
        }).ToList();
    }

    public async Task<IReadOnlyList<OutsourceTrackingRowDto>> TrackingAsync(DateOnly start, DateOnly end, OrgScope scope, bool canSeeEncrypted, CancellationToken ct)
    {
        var (samples, labs) = await LoadScopedAsync(start, end, scope, ct);
        var rows = new List<OutsourceTrackingRowDto>();
        foreach (var s in samples)
        {
            if (!labs.TryGetValue(s.LaboratoryId, out var lab)) continue;
            foreach (var t in s.Tests)
                rows.Add(new OutsourceTrackingRowDto(
                    s.VisitDate, s.LaboratoryId.Value, DisplayCode.For(lab.Code.Value, lab.IsEncrypted, canSeeEncrypted), lab.Name,
                    lab.Branch, lab.Governorate, lab.City, lab.Area,
                    t.TestCode, t.TestName, t.SampleVolume, t.TestFees.Amount, t.OutsourceFees.Amount, t.NetRevenue.Amount));
        }
        return rows.OrderByDescending(r => r.VisitDate).ThenBy(r => r.LabName).ThenBy(r => r.TestName).ToList();
    }

    // Loads scoped outsource samples (owned Tests auto-included) for a range plus the lab dimensions dictionary.
    private async Task<(List<Domain.Operations.OutsourceSample> Samples,
        Dictionary<Domain.Laboratories.LaboratoryId, LabRow> Labs)> LoadScopedAsync(
        DateOnly start, DateOnly end, OrgScope scope, CancellationToken ct)
    {
        var scopedLabs = _db.Laboratories.ApplyScope(scope).Select(l => l.Id);
        var samples = await _db.OutsourceSamples.AsNoTracking()
            .Where(o => scopedLabs.Contains(o.LaboratoryId) && o.VisitDate >= start && o.VisitDate <= end)
            .ToListAsync(ct);
        var labIds = samples.Select(s => s.LaboratoryId).Distinct().ToList();
        var labs = (await _db.Laboratories.AsNoTracking().Where(l => labIds.Contains(l.Id))
            .Select(l => new LabRow(l.Id, l.Code, l.IsEncrypted, l.Name, l.Branch, l.Governorate, l.City, l.Area))
            .ToListAsync(ct)).ToDictionary(l => l.Id);
        return (samples, labs);
    }

    private sealed record LabRow(Domain.Laboratories.LaboratoryId Id, Domain.Laboratories.LabCode Code, bool IsEncrypted,
        string Name, string? Branch, string? Governorate, string? City, string? Area);
}

internal sealed class SampleTrackingQueries : ISampleTrackingQueries
{
    private readonly FollowUpDbContext _db;
    public SampleTrackingQueries(FollowUpDbContext db) => _db = db;

    public async Task<IReadOnlyList<SampleTrackingDto>> ListAsync(DateOnly start, DateOnly end, OrgScope scope, CancellationToken ct)
    {
        // Area-scoped (scope.Areas). Wildcard => all areas.
        var q = _db.SampleTracking.AsNoTracking().Where(s => s.Date >= start && s.Date <= end);
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
            s.Notes, s.IsComplete)).ToList();
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

    public async Task<IReadOnlyList<SampleLifecycleRowDto>> LifecycleAsync(
        DateOnly from, DateOnly to, OrgScope scope, bool canSeeEncrypted, CancellationToken ct)
    {
        var start = from;
        var end = to;

        // Live visits (today's board) + archived history, both scoped via the lab dimensions.
        var live = (await (from v in _db.DailyVisits.AsNoTracking()
                           where v.VisitDate >= start && v.VisitDate <= end && v.SampleCount != null
                           join l in _db.Laboratories.ApplyScope(scope).AsNoTracking() on v.LaboratoryId equals l.Id
                           select new { VisitId = v.Id.Value, l.Code, l.IsEncrypted, l.Name, l.Area, v.VisitDate, Time = (TimeOnly?)v.ScheduledTime,
                               v.SampleCount, v.CheckedInAt, v.TransferConfirmedAt, v.ReceivedAt,
                               v.CollectorRepId, v.TransferRepId, v.Transfer })
                          .ToListAsync(ct))
            .Select(v => new { v.VisitId, v.Code, v.IsEncrypted, v.Name, v.Area, v.VisitDate, v.Time, v.SampleCount, v.CheckedInAt,
                v.TransferConfirmedAt, v.ReceivedAt, v.CollectorRepId, v.TransferRepId,
                DriverName = v.Transfer?.DriverName, DriverMobile = v.Transfer?.DriverMobile, CarPlate = v.Transfer?.CarPlate });

        var archived = await (from h in _db.VisitHistory.AsNoTracking()
                              where h.VisitDate >= start && h.VisitDate <= end && h.SampleCount != null
                              join l in _db.Laboratories.ApplyScope(scope).AsNoTracking() on h.LaboratoryId equals l.Id
                              select new { VisitId = h.OriginalVisitId.Value, l.Code, l.IsEncrypted, l.Name, l.Area, h.VisitDate, Time = h.ScheduledTime,
                                  h.SampleCount, h.CheckedInAt, h.TransferConfirmedAt, h.ReceivedAt,
                                  h.CollectorRepId, h.TransferRepId, h.DriverName, h.DriverMobile, h.CarPlate })
                             .ToListAsync(ct);

        var rows = live.Concat(archived).ToList();

        // Rep names for the collected/transferred legs — single lookup.
        var repIds = rows.SelectMany(r => new[] { r.CollectorRepId, r.TransferRepId })
            .Where(id => id != null).Select(id => id!.Value).Distinct().ToList();
        var repNames = (await _db.Representatives.AsNoTracking().Where(x => repIds.Contains(x.Id))
            .Select(x => new { x.Id, x.FullName }).ToListAsync(ct)).ToDictionary(x => x.Id, x => x.FullName);
        string? NameOf(Domain.Representatives.RepresentativeId? id) =>
            id != null && repNames.TryGetValue(id.Value, out var n) ? n : null;

        // Area/day tracking rows (data entry / review / sort + notes), keyed by (area, date).
        var trackingRows = await _db.SampleTracking.AsNoTracking()
            .Where(s => s.Date >= start && s.Date <= end).ToListAsync(ct);
        var tracking = trackingRows.ToDictionary(s => (s.Area, s.Date));

        var result = rows
            .OrderByDescending(r => r.VisitDate).ThenBy(r => r.Time)
            .Select(r =>
            {
                var t = r.Area != null && tracking.TryGetValue((r.Area, r.VisitDate), out var found) ? found : null;
                return new SampleLifecycleRowDto(
                    r.Name, DisplayCode.For(r.Code.Value, r.IsEncrypted, canSeeEncrypted), r.Area,
                    r.VisitDate, r.Time?.ToString("HH:mm") ?? "—", r.SampleCount,
                    NameOf(r.CollectorRepId), r.CheckedInAt,
                    NameOf(r.TransferRepId), r.DriverName, r.DriverMobile, r.CarPlate, r.TransferConfirmedAt,
                    r.ReceivedAt,
                    t?.DataEntry?.User, t?.DataEntry?.At,
                    t?.Review?.User, t?.Review?.At,
                    t?.Sort?.User, t?.Sort?.At,
                    t?.Notes, VisitId: r.VisitId);
            }).ToList();

        var atts = await AttachmentEnricher.LoadAsync(_db, result.Where(d => d.VisitId.HasValue).Select(d => d.VisitId!.Value), ct);
        return atts.Count == 0 ? result
            : result.Select(d => d.VisitId.HasValue && atts.TryGetValue(d.VisitId.Value, out var a) ? d with { Attachments = a } : d).ToList();
    }

    public async Task<int> SumReceivedSamplesAsync(string area, DateOnly date, CancellationToken ct)
    {
        var received = VisitStatus.Received;
        var live = await (from v in _db.DailyVisits.AsNoTracking()
                          where v.VisitDate == date && v.Status == received && v.SampleCount != null
                          join l in _db.Laboratories.AsNoTracking() on v.LaboratoryId equals l.Id
                          where l.Area == area
                          select v.SampleCount!.Value).SumAsync(ct);
        var archived = await (from h in _db.VisitHistory.AsNoTracking()
                              where h.VisitDate == date && h.Status == received.Name && h.SampleCount != null
                              join l in _db.Laboratories.AsNoTracking() on h.LaboratoryId equals l.Id
                              where l.Area == area
                              select h.SampleCount!.Value).SumAsync(ct);
        return live + archived;
    }

    public async Task<IReadOnlyList<DateOnly>> GetReceivedVisitDatesAsync(
        FollowUp.Domain.Laboratories.LaboratoryId laboratoryId, CancellationToken ct)
    {
        var received = VisitStatus.Received;
        var live = await _db.DailyVisits.AsNoTracking()
            .Where(v => v.LaboratoryId == laboratoryId && v.Status == received && v.SampleCount != null)
            .Select(v => v.VisitDate).Distinct().ToListAsync(ct);
        var archived = await _db.VisitHistory.AsNoTracking()
            .Where(h => h.LaboratoryId == laboratoryId && h.Status == received.Name && h.SampleCount != null)
            .Select(h => h.VisitDate).Distinct().ToListAsync(ct);
        return live.Union(archived).OrderBy(d => d).ToList();
    }
}
