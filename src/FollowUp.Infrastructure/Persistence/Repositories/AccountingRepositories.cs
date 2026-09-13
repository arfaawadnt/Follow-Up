using FollowUp.Application.Common.Abstractions.Persistence;
using FollowUp.Domain.Accounting;
using Microsoft.EntityFrameworkCore;

namespace FollowUp.Infrastructure.Persistence.Repositories;

internal sealed class TreasuryReasonRepository : ITreasuryReasonRepository
{
    private readonly FollowUpDbContext _db;
    public TreasuryReasonRepository(FollowUpDbContext db) => _db = db;
    public async Task<IReadOnlyList<TreasuryReason>> GetAllAsync(CancellationToken ct) => await _db.TreasuryReasons.OrderBy(x => x.Name).ToListAsync(ct);
    public Task<TreasuryReason?> GetByIdAsync(TreasuryReasonId id, CancellationToken ct) => _db.TreasuryReasons.FirstOrDefaultAsync(x => x.Id == id, ct);
    public void Add(TreasuryReason reason) => _db.TreasuryReasons.Add(reason);
}

internal sealed class TreasuryRepository : ITreasuryRepository
{
    private readonly FollowUpDbContext _db;
    public TreasuryRepository(FollowUpDbContext db) => _db = db;
    public async Task<IReadOnlyList<Treasury>> GetAllAsync(CancellationToken ct) => await _db.Treasuries.OrderBy(x => x.Name).ToListAsync(ct);
    public Task<Treasury?> GetByIdAsync(TreasuryId id, CancellationToken ct) => _db.Treasuries.FirstOrDefaultAsync(x => x.Id == id, ct);
    public void Add(Treasury treasury) => _db.Treasuries.Add(treasury);
}

internal sealed class TreasuryEntryRepository : ITreasuryEntryRepository
{
    private readonly FollowUpDbContext _db;
    public TreasuryEntryRepository(FollowUpDbContext db) => _db = db;
    public Task<TreasuryEntry?> GetByIdAsync(TreasuryEntryId id, CancellationToken ct) => _db.TreasuryEntries.FirstOrDefaultAsync(x => x.Id == id, ct);
    public void Add(TreasuryEntry entry) => _db.TreasuryEntries.Add(entry);
    public void Remove(TreasuryEntry entry) => _db.TreasuryEntries.Remove(entry);
}

internal sealed class PenaltyRecordRepository : IPenaltyRecordRepository
{
    private readonly FollowUpDbContext _db;
    public PenaltyRecordRepository(FollowUpDbContext db) => _db = db;
    public Task<PenaltyRecord?> GetByIdAsync(PenaltyRecordId id, CancellationToken ct) => _db.PenaltyRecords.FirstOrDefaultAsync(x => x.Id == id, ct);
    public void Add(PenaltyRecord record) => _db.PenaltyRecords.Add(record);
    public void Remove(PenaltyRecord record) => _db.PenaltyRecords.Remove(record);
}

internal sealed class DeductionRepository : IDeductionRepository
{
    private readonly FollowUpDbContext _db;
    public DeductionRepository(FollowUpDbContext db) => _db = db;
    public Task<Deduction?> GetByIdAsync(DeductionId id, CancellationToken ct) => _db.Deductions.FirstOrDefaultAsync(x => x.Id == id, ct);
    public void Add(Deduction deduction) => _db.Deductions.Add(deduction);
    public void Remove(Deduction deduction) => _db.Deductions.Remove(deduction);
}

internal sealed class CollectionRepository : ICollectionRepository
{
    private readonly FollowUpDbContext _db;
    public CollectionRepository(FollowUpDbContext db) => _db = db;
    public Task<Collection?> GetByIdAsync(CollectionId id, CancellationToken ct) => _db.Collections.FirstOrDefaultAsync(x => x.Id == id, ct);
    public void Add(Collection collection) => _db.Collections.Add(collection);
    public void Remove(Collection collection) => _db.Collections.Remove(collection);
}

internal sealed class RepIncomeEntryRepository : IRepIncomeEntryRepository
{
    private readonly FollowUpDbContext _db;
    public RepIncomeEntryRepository(FollowUpDbContext db) => _db = db;
    public Task<RepIncomeEntry?> GetByIdAsync(RepIncomeEntryId id, CancellationToken ct) => _db.RepIncomeEntries.FirstOrDefaultAsync(x => x.Id == id, ct);
    public void Add(RepIncomeEntry entry) => _db.RepIncomeEntries.Add(entry);
    public void Remove(RepIncomeEntry entry) => _db.RepIncomeEntries.Remove(entry);
}
