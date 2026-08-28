using FollowUp.Application.Common.Models;
using FollowUp.Application.Features.Complaints.Contracts;
using FollowUp.Application.Features.Marketing;
using FollowUp.Domain.Identity;
using FollowUp.Domain.Marketing;
using Microsoft.EntityFrameworkCore;

namespace FollowUp.Infrastructure.Persistence.Queries;

internal sealed class ComplaintQueries : IComplaintQueries
{
    private readonly FollowUpDbContext _db;
    public ComplaintQueries(FollowUpDbContext db) => _db = db;

    public async Task<PagedResult<ComplaintListItemDto>> SearchAsync(
        ComplaintSearchCriteria criteria, OrgScope scope, bool canSeeEncrypted, CancellationToken ct)
    {
        var scopedLabs = _db.Laboratories.ApplyScope(scope).Select(l => l.Id);
        var q = from c in _db.Complaints.AsNoTracking()
                where scopedLabs.Contains(c.LaboratoryId)
                select c;

        if (!string.IsNullOrWhiteSpace(criteria.Status))
        {
            var status = Domain.Common.Enumeration.FromName<Domain.Complaints.ComplaintStatus>(criteria.Status);
            q = q.Where(c => c.Status == status);
        }
        if (!string.IsNullOrWhiteSpace(criteria.Category))
            q = q.Where(c => c.Category == criteria.Category);
        if (criteria.LaboratoryId is { } labId)
            q = q.Where(c => c.LaboratoryId == new Domain.Laboratories.LaboratoryId(labId));

        var total = await q.CountAsync(ct);
        var rows = await (from cp in q
                          join l in _db.Laboratories.AsNoTracking() on cp.LaboratoryId equals l.Id
                          orderby cp.Number descending
                          select new { cp.Id, cp.Number, cp.LaboratoryId, l.Code, l.Name, LabCategory = l.Category, cp.Category, cp.ViaChannel,
                              cp.AssignedTeam, cp.Details, cp.Status, cp.Stage, cp.ResolvedBy, cp.ResolvedAt, cp.ResolutionSummary, cp.CreatedAt })
                         .Skip(criteria.Skip).Take(criteria.PageSize).ToListAsync(ct);

        var todayNum = DateOnly.FromDateTime(DateTime.UtcNow).DayNumber;
        var items = rows.Select(r => new ComplaintListItemDto(
            r.Id.Value, $"CMP-{r.Number}", r.LaboratoryId.Value, DisplayCode.For(r.Code.Value, canSeeEncrypted), r.Name,
            r.LabCategory, r.Category, r.ViaChannel, r.AssignedTeam, r.Details, r.Status.Name, r.Stage.Name,
            Math.Max(0, todayNum - DateOnly.FromDateTime(r.CreatedAt.UtcDateTime).DayNumber),
            r.ResolvedBy, r.ResolvedAt, r.ResolutionSummary, r.CreatedAt)).ToList();

        return PagedResult<ComplaintListItemDto>.Create(items, total, criteria.Page, criteria.PageSize);
    }

    public async Task<ComplaintDetailDto?> GetByIdAsync(Guid id, OrgScope scope, bool canSeeEncrypted, CancellationToken ct)
    {
        // Scope the join to the caller's org scope: an out-of-scope complaint yields no row → null → 404,
        // matching the list read (SearchAsync) and the SRS SCOPE-READ requirement.
        var row = await (from c in _db.Complaints.AsNoTracking()
                         where c.Id == new Domain.Complaints.ComplaintId(id)
                         join l in _db.Laboratories.ApplyScope(scope).AsNoTracking() on c.LaboratoryId equals l.Id
                         select new { c, l.Code, l.Name }).FirstOrDefaultAsync(ct);
        if (row is null) return null;
        var complaint = row.c;

        string? repName = null;
        if (complaint.RepresentativeId is { } repId)
            repName = await _db.Representatives.AsNoTracking()
                .Where(r => r.Id == new Domain.Representatives.RepresentativeId(repId))
                .Select(r => r.FullName).FirstOrDefaultAsync(ct);

        return new ComplaintDetailDto(
            complaint.Id.Value, complaint.Reference, complaint.LaboratoryId.Value, DisplayCode.For(row.Code.Value, canSeeEncrypted),
            row.Name, complaint.Category, complaint.ViaChannel, complaint.AssignedTeam, complaint.Details,
            complaint.Status.Name, complaint.Stage.Name, complaint.ResolvedAt, complaint.ResolvedBy,
            complaint.RepresentativeId, repName, complaint.ReceivedAt,
            complaint.IsValid, complaint.ValidityNotes, complaint.InvestigationNotes,
            complaint.OutcomeType, complaint.OutcomeSummary, complaint.ResolutionSummary, complaint.CreatedAt);
    }

    public async Task<IReadOnlyList<ComplaintAuditRowDto>> GetAuditAsync(Guid id, OrgScope scope, CancellationToken ct)
    {
        // Confirm the complaint's lab is within the caller's org scope before exposing its audit trail
        // (the trail includes before/after snapshots). Out of scope → empty, never a cross-scope disclosure.
        var inScope = await (from c in _db.Complaints.AsNoTracking()
                             join l in _db.Laboratories.ApplyScope(scope) on c.LaboratoryId equals l.Id
                             where c.Id == new Domain.Complaints.ComplaintId(id)
                             select c.Id).AnyAsync(ct);
        if (!inScope) return Array.Empty<ComplaintAuditRowDto>();

        var idStr = id.ToString();
        return await _db.AuditEntries.AsNoTracking()
            .Where(a => a.Entity == "Complaint" && a.EntityId == idStr)
            .OrderBy(a => a.OccurredAt)
            .Select(a => new ComplaintAuditRowDto(a.OccurredAt, a.Actor, a.Action, a.BeforeJson, a.AfterJson))
            .ToListAsync(ct);
    }
}

internal sealed class MarketingQueries : IMarketingQueries
{
    private readonly FollowUpDbContext _db;
    public MarketingQueries(FollowUpDbContext db) => _db = db;

    public async Task<PagedResult<MarketingVisitDto>> SearchAsync(
        MarketingSearchCriteria criteria, OrgScope scope, bool canSeeEncrypted, CancellationToken ct)
    {
        var scopedLabs = _db.Laboratories.ApplyScope(scope).Select(l => l.Id);
        var q = from m in _db.MarketingVisits.AsNoTracking()
                where scopedLabs.Contains(m.LaboratoryId)
                select m;

        if (!string.IsNullOrWhiteSpace(criteria.Status))
        {
            var status = Domain.Common.Enumeration.FromName<MarketingVisitStatus>(criteria.Status);
            q = q.Where(m => m.Status == status);
        }
        if (criteria.LaboratoryId is { } labId)
            q = q.Where(m => m.LaboratoryId == new Domain.Laboratories.LaboratoryId(labId));

        var total = await q.CountAsync(ct);
        var scheduled = MarketingVisitStatus.Scheduled;
        var rows = await (from mv in q
                          join l in _db.Laboratories.AsNoTracking() on mv.LaboratoryId equals l.Id
                          orderby (mv.Status == scheduled ? 0 : 1), mv.ScheduledDate descending, mv.Number descending // BR-10: scheduled first
                          select new { mv.Id, mv.Number, mv.LaboratoryId, l.Code, l.Name, l.Area, l.Governorate, mv.RepresentativeId, mv.Purpose, mv.ScheduledDate, mv.ScheduledTime, mv.Plan, mv.Status, mv.Outcome })
                         .Skip(criteria.Skip).Take(criteria.PageSize).ToListAsync(ct);

        var repIds = rows.Select(r => r.RepresentativeId).Distinct().ToList();
        var repName = (await _db.Representatives.AsNoTracking().Where(r => repIds.Contains(r.Id))
            .Select(r => new { r.Id, r.FullName }).ToListAsync(ct)).ToDictionary(r => r.Id, r => r.FullName);

        var items = rows.Select(r => new MarketingVisitDto(
            r.Id.Value, $"MV{r.Number}", r.LaboratoryId.Value, DisplayCode.For(r.Code.Value, canSeeEncrypted), r.Name, r.Area, r.Governorate,
            r.RepresentativeId.Value, repName.TryGetValue(r.RepresentativeId, out var n) ? n : null,
            r.Purpose.Name, r.ScheduledDate, r.ScheduledTime.HasValue ? r.ScheduledTime.Value.ToString("HH:mm") : null, r.Plan,
            r.Status.Name, r.Outcome)).ToList();

        return PagedResult<MarketingVisitDto>.Create(items, total, criteria.Page, criteria.PageSize);
    }
}
