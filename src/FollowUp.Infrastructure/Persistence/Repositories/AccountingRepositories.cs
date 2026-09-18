using FollowUp.Domain.Identity;
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
    public async Task<IReadOnlyList<Treasury>> GetActiveByBranchAsync(string branch, CancellationToken ct)
    {
        // Branches is a jsonb string list — filter in memory over the (small) treasury set, case-insensitively like SetBranches.
        var active = await _db.Treasuries.Where(t => t.IsActive).OrderBy(t => t.Name).ToListAsync(ct);
        return active.Where(t => t.Branches.Contains(branch, StringComparer.OrdinalIgnoreCase)).ToList();
    }
    public void Add(Treasury treasury) => _db.Treasuries.Add(treasury);
}

internal sealed class TreasuryEntryRepository : ITreasuryEntryRepository
{
    private readonly FollowUpDbContext _db;
    public TreasuryEntryRepository(FollowUpDbContext db) => _db = db;
    public Task<TreasuryEntry?> GetByIdAsync(TreasuryEntryId id, CancellationToken ct) => _db.TreasuryEntries.FirstOrDefaultAsync(x => x.Id == id, ct);
    public Task<TreasuryEntry?> GetByCollectionAsync(CollectionId collectionId, CancellationToken ct) =>
        _db.TreasuryEntries.FirstOrDefaultAsync(x => x.CollectionId == collectionId, ct);
    public void Add(TreasuryEntry entry) => _db.TreasuryEntries.Add(entry);
    public void Remove(TreasuryEntry entry) => _db.TreasuryEntries.Remove(entry);
}

internal sealed class TreasuryGrantRepository : ITreasuryGrantRepository
{
    private readonly FollowUpDbContext _db;
    public TreasuryGrantRepository(FollowUpDbContext db) => _db = db;
    public async Task<IReadOnlyList<TreasuryGrant>> GetForRoleAsync(RoleId roleId, CancellationToken ct) =>
        await _db.TreasuryGrants.Where(g => g.RoleId == roleId).ToListAsync(ct);
    public void Add(TreasuryGrant grant) => _db.TreasuryGrants.Add(grant);
    public void Remove(TreasuryGrant grant) => _db.TreasuryGrants.Remove(grant);
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

internal sealed class RepLabIncomeRepository : IRepLabIncomeRepository
{
    private readonly FollowUpDbContext _db;
    public RepLabIncomeRepository(FollowUpDbContext db) => _db = db;
    public async Task<IReadOnlyList<RepLabIncome>> GetForRepDateAsync(FollowUp.Domain.Representatives.RepresentativeId repId, DateOnly date, CancellationToken ct) =>
        await _db.RepLabIncomes.Where(x => x.RepresentativeId == repId && x.Date == date).ToListAsync(ct);
    public void Add(RepLabIncome entry) => _db.RepLabIncomes.Add(entry);
    public void Remove(RepLabIncome entry) => _db.RepLabIncomes.Remove(entry);
}
