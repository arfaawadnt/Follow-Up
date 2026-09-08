using FollowUp.Application.Common.Abstractions.Persistence;
using FollowUp.Domain.Complaints;
using FollowUp.Domain.Laboratories;
using FollowUp.Domain.Marketing;
using FollowUp.Domain.Operations;
using Microsoft.EntityFrameworkCore;

namespace FollowUp.Infrastructure.Persistence.Repositories;

internal sealed class DailyVisitRepository : IDailyVisitRepository
{
    private readonly FollowUpDbContext _db;
    public DailyVisitRepository(FollowUpDbContext db) => _db = db;
    public Task<DailyVisit?> GetByIdAsync(DailyVisitId id, CancellationToken ct) =>
        _db.DailyVisits.FirstOrDefaultAsync(x => x.Id == id, ct);
    public async Task<IReadOnlyList<TimeOnly>> TakenSlotsAsync(LaboratoryId labId, DateOnly date, CancellationToken ct) =>
        await _db.DailyVisits.AsNoTracking()
            .Where(v => v.LaboratoryId == labId && v.VisitDate == date)
            .Select(v => v.ScheduledTime)
            .ToListAsync(ct);
    public void Add(DailyVisit visit) => _db.DailyVisits.Add(visit);
}

internal sealed class VisitAttachmentRepository : IVisitAttachmentRepository
{
    private readonly FollowUpDbContext _db;
    public VisitAttachmentRepository(FollowUpDbContext db) => _db = db;
    public void Add(VisitAttachment attachment) => _db.VisitAttachments.Add(attachment);
    public Task<VisitAttachment?> GetByIdAsync(VisitAttachmentId id, CancellationToken ct) =>
        _db.VisitAttachments.FirstOrDefaultAsync(x => x.Id == id, ct);
    public async Task<IReadOnlyList<VisitAttachment>> GetByIdsAsync(IReadOnlyCollection<VisitAttachmentId> ids, CancellationToken ct) =>
        ids.Count == 0 ? Array.Empty<VisitAttachment>()
        : await _db.VisitAttachments.Where(x => ids.Contains(x.Id)).ToListAsync(ct);
}

internal sealed class OutsourceSampleRepository : IOutsourceSampleRepository
{
    private readonly FollowUpDbContext _db;
    public OutsourceSampleRepository(FollowUpDbContext db) => _db = db;
    public Task<OutsourceSample?> GetByIdAsync(OutsourceSampleId id, CancellationToken ct) =>
        _db.OutsourceSamples.FirstOrDefaultAsync(x => x.Id == id, ct);
    public Task<bool> ExistsForAsync(LaboratoryId labId, DateOnly visitDate, CancellationToken ct) =>
        _db.OutsourceSamples.AnyAsync(x => x.LaboratoryId == labId && x.VisitDate == visitDate, ct);
    public void Add(OutsourceSample sample) => _db.OutsourceSamples.Add(sample);
    public void Remove(OutsourceSample sample) => _db.OutsourceSamples.Remove(sample);
}

internal sealed class SampleTrackingRepository : ISampleTrackingRepository
{
    private readonly FollowUpDbContext _db;
    public SampleTrackingRepository(FollowUpDbContext db) => _db = db;
    public Task<SampleTracking?> GetByIdAsync(SampleTrackingId id, CancellationToken ct) =>
        _db.SampleTracking.FirstOrDefaultAsync(x => x.Id == id, ct);
    public Task<SampleTracking?> GetByAreaDateAsync(string area, DateOnly date, CancellationToken ct)
    {
        // Outbox handlers in one dispatch batch share this scoped context and only save at the end, so a
        // row added by an earlier message is invisible to a SQL query — returning it from the change
        // tracker prevents a duplicate Add that would break the unique (Area, Date) index and wedge the
        // batch. A row deleted earlier in the batch counts as absent; EF orders the delete before any
        // re-insert of the same key (unique-index dependency ordering).
        var local = _db.SampleTracking.Local.FirstOrDefault(x => x.Area == area && x.Date == date);
        if (local is not null)
            return Task.FromResult<SampleTracking?>(_db.Entry(local).State == EntityState.Deleted ? null : local);
        return _db.SampleTracking.FirstOrDefaultAsync(x => x.Area == area && x.Date == date, ct);
    }
    public void Add(SampleTracking tracking) => _db.SampleTracking.Add(tracking);
    public void Remove(SampleTracking tracking) => _db.SampleTracking.Remove(tracking);
}

internal sealed class MarketingVisitRepository : IMarketingVisitRepository
{
    private readonly FollowUpDbContext _db;
    public MarketingVisitRepository(FollowUpDbContext db) => _db = db;
    public Task<MarketingVisit?> GetByIdAsync(MarketingVisitId id, CancellationToken ct) =>
        _db.MarketingVisits.FirstOrDefaultAsync(x => x.Id == id, ct);
    // Transaction-scoped advisory lock key for gap-free marketing-visit numbering (same race as M-12).
    private const long NumberLockKey = 811002;
    public async Task<int> NextNumberAsync(CancellationToken ct)
    {
        await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({NumberLockKey})", ct);
        return (await _db.MarketingVisits.MaxAsync(x => (int?)x.Number, ct) ?? 0) + 1;
    }
    public void Add(MarketingVisit visit) => _db.MarketingVisits.Add(visit);
}

internal sealed class ComplaintRepository : IComplaintRepository
{
    private readonly FollowUpDbContext _db;
    public ComplaintRepository(FollowUpDbContext db) => _db = db;
    public Task<Complaint?> GetByIdAsync(ComplaintId id, CancellationToken ct) =>
        _db.Complaints.FirstOrDefaultAsync(x => x.Id == id, ct);
    // Transaction-scoped advisory lock key for gap-free complaint numbering (finding M-12 / CMP-7).
    private const long NumberLockKey = 811001;
    public async Task<int> NextNumberAsync(CancellationToken ct)
    {
        // Serialize concurrent creation so read-max-plus-one can't hand two inserts the same Number (which would
        // violate ix_complaint_number and surface as a raw 500). The lock is held until the command's transaction
        // commits, so the reserved number is safely inserted before any other creator proceeds.
        await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({NumberLockKey})", ct);
        return (await _db.Complaints.MaxAsync(x => (int?)x.Number, ct) ?? 0) + 1;
    }
    public void Add(Complaint complaint) => _db.Complaints.Add(complaint);
}
