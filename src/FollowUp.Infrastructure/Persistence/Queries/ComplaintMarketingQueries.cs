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
                          select new { cp.Id, cp.Number, cp.LaboratoryId, l.Code, cp.Category, cp.Status, cp.Stage, cp.CreatedAt })
                         .Skip(criteria.Skip).Take(criteria.PageSize).ToListAsync(ct);

        var items = rows.Select(r => new ComplaintListItemDto(
            r.Id.Value, $"CMP-{r.Number}", r.LaboratoryId.Value, DisplayCode.For(r.Code.Value, canSeeEncrypted),
            r.Category, r.Status.Name, r.Stage.Name, r.CreatedAt)).ToList();

        return PagedResult<ComplaintListItemDto>.Create(items, total, criteria.Page, criteria.PageSize);
    }

    public async Task<ComplaintDetailDto?> GetByIdAsync(Guid id, bool canSeeEncrypted, CancellationToken ct)
    {
        var row = await (from c in _db.Complaints.AsNoTracking()
                         where c.Id == new Domain.Complaints.ComplaintId(id)
                         join l in _db.Laboratories.AsNoTracking() on c.LaboratoryId equals l.Id
                         select new { c, l.Code }).FirstOrDefaultAsync(ct);
        if (row is null) return null;
        var complaint = row.c;
        return new ComplaintDetailDto(
            complaint.Id.Value, complaint.Reference, complaint.LaboratoryId.Value, DisplayCode.For(row.Code.Value, canSeeEncrypted),
            complaint.Category, complaint.ViaChannel, complaint.AssignedTeam, complaint.Details,
            complaint.Status.Name, complaint.Stage.Name, complaint.ResolvedAt, complaint.ResolvedBy);
    }

    public async Task<IReadOnlyList<ComplaintAuditRowDto>> GetAuditAsync(Guid id, CancellationToken ct)
    {
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
                          orderby (mv.Status == scheduled ? 0 : 1), mv.ScheduledDate descending // BR-10: scheduled first
                          select new { mv.Id, mv.LaboratoryId, l.Code, l.Name, mv.RepresentativeId, mv.Purpose, mv.ScheduledDate, mv.Status, mv.Outcome })
                         .Skip(criteria.Skip).Take(criteria.PageSize).ToListAsync(ct);

        var items = rows.Select(r => new MarketingVisitDto(
            r.Id.Value, r.LaboratoryId.Value, DisplayCode.For(r.Code.Value, canSeeEncrypted), r.Name,
            r.RepresentativeId.Value, r.Purpose.Name, r.ScheduledDate, r.Status.Name, r.Outcome)).ToList();

        return PagedResult<MarketingVisitDto>.Create(items, total, criteria.Page, criteria.PageSize);
    }
}
