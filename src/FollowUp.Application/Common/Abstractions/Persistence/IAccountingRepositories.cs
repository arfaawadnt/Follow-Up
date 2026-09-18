using FollowUp.Domain.Identity;
using FollowUp.Domain.Accounting;

namespace FollowUp.Application.Common.Abstractions.Persistence;

// Accounting module repositories. Reads are async; Add/Remove are void — the DbContext is the unit of work and the
// TransactionBehavior commits (handlers never call SaveChanges).

public interface ITreasuryReasonRepository
{
    Task<IReadOnlyList<TreasuryReason>> GetAllAsync(CancellationToken ct);
    Task<TreasuryReason?> GetByIdAsync(TreasuryReasonId id, CancellationToken ct);
    void Add(TreasuryReason reason);
}

public interface ITreasuryRepository
{
    Task<IReadOnlyList<Treasury>> GetAllAsync(CancellationToken ct);
    Task<Treasury?> GetByIdAsync(TreasuryId id, CancellationToken ct);
    /// <summary>Active treasuries assigned to a branch (the lab's serving branch), ordered by name.</summary>
    Task<IReadOnlyList<Treasury>> GetActiveByBranchAsync(string branch, CancellationToken ct);
    void Add(Treasury treasury);
}

public interface ITreasuryEntryRepository
{
    Task<TreasuryEntry?> GetByIdAsync(TreasuryEntryId id, CancellationToken ct);
    /// <summary>The AutoCollection row mirroring a collection, if one exists.</summary>
    Task<TreasuryEntry?> GetByCollectionAsync(CollectionId collectionId, CancellationToken ct);
    void Add(TreasuryEntry entry);
    void Remove(TreasuryEntry entry);
}

/// <summary>Per-role, per-treasury rights (View / Validate / Update). Unique per (role, treasury).</summary>
public interface ITreasuryGrantRepository
{
    Task<IReadOnlyList<TreasuryGrant>> GetForRoleAsync(RoleId roleId, CancellationToken ct);
    void Add(TreasuryGrant grant);
    void Remove(TreasuryGrant grant);
}

public interface IPenaltyRecordRepository
{
    Task<PenaltyRecord?> GetByIdAsync(PenaltyRecordId id, CancellationToken ct);
    void Add(PenaltyRecord record);
    void Remove(PenaltyRecord record);
}

public interface IDeductionRepository
{
    Task<Deduction?> GetByIdAsync(DeductionId id, CancellationToken ct);
    void Add(Deduction deduction);
    void Remove(Deduction deduction);
}

public interface ICollectionRepository
{
    Task<Collection?> GetByIdAsync(CollectionId id, CancellationToken ct);
    void Add(Collection collection);
    void Remove(Collection collection);
}

public interface IRepIncomeEntryRepository
{
    Task<RepIncomeEntry?> GetByIdAsync(RepIncomeEntryId id, CancellationToken ct);
    void Add(RepIncomeEntry entry);
    void Remove(RepIncomeEntry entry);
}

public interface IRepLabIncomeRepository
{
    /// <summary>The rep's sheet lines of one date (tracked) — the save command upserts against them.</summary>
    Task<IReadOnlyList<RepLabIncome>> GetForRepDateAsync(FollowUp.Domain.Representatives.RepresentativeId repId, DateOnly date, CancellationToken ct);
    void Add(RepLabIncome entry);
    void Remove(RepLabIncome entry);
}
