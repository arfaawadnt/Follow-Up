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
    public void Add(DailyVisit visit) => _db.DailyVisits.Add(visit);
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
    public Task<SampleTracking?> GetByAreaDateAsync(string area, DateOnly date, CancellationToken ct) =>
        _db.SampleTracking.FirstOrDefaultAsync(x => x.Area == area && x.Date == date, ct);
    public void Add(SampleTracking tracking) => _db.SampleTracking.Add(tracking);
}

internal sealed class MarketingVisitRepository : IMarketingVisitRepository
{
    private readonly FollowUpDbContext _db;
    public MarketingVisitRepository(FollowUpDbContext db) => _db = db;
    public Task<MarketingVisit?> GetByIdAsync(MarketingVisitId id, CancellationToken ct) =>
        _db.MarketingVisits.FirstOrDefaultAsync(x => x.Id == id, ct);
    public async Task<int> NextNumberAsync(CancellationToken ct) =>
        (await _db.MarketingVisits.MaxAsync(x => (int?)x.Number, ct) ?? 0) + 1;
    public void Add(MarketingVisit visit) => _db.MarketingVisits.Add(visit);
}

internal sealed class ComplaintRepository : IComplaintRepository
{
    private readonly FollowUpDbContext _db;
    public ComplaintRepository(FollowUpDbContext db) => _db = db;
    public Task<Complaint?> GetByIdAsync(ComplaintId id, CancellationToken ct) =>
        _db.Complaints.FirstOrDefaultAsync(x => x.Id == id, ct);
    public async Task<int> NextNumberAsync(CancellationToken ct) =>
        (await _db.Complaints.MaxAsync(x => (int?)x.Number, ct) ?? 0) + 1;
    public void Add(Complaint complaint) => _db.Complaints.Add(complaint);
}
