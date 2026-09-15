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
    void Add(Treasury treasury);
}

public interface ITreasuryEntryRepository
{
    Task<TreasuryEntry?> GetByIdAsync(TreasuryEntryId id, CancellationToken ct);
    void Add(TreasuryEntry entry);
    void Remove(TreasuryEntry entry);
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
    /// <summary>The AutoPenalty row mirroring a penalty, if one exists.</summary>
    Task<Deduction?> GetByPenaltyAsync(PenaltyRecordId penaltyId, CancellationToken ct);
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
