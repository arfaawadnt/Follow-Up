using FollowUp.Domain.Common;
using FollowUp.Domain.Compensation;
using FollowUp.Domain.Laboratories;
using FollowUp.Domain.Representatives;

namespace FollowUp.Application.Common.Abstractions.Persistence;

/// <summary>Aggregate repository for <see cref="LabLoyaltyLedger"/> (per lab + year-month).</summary>
public interface ILabLoyaltyLedgerRepository
{
    Task<LabLoyaltyLedger?> GetAsync(LaboratoryId labId, YearMonth period, CancellationToken ct);
    /// <summary>All ledgers for a period, tracked — for the batch recalc (CPN-18) instead of one GetAsync per lab.</summary>
    Task<IReadOnlyList<LabLoyaltyLedger>> GetForPeriodAsync(YearMonth period, CancellationToken ct);
    void Add(LabLoyaltyLedger ledger);
}

/// <summary>Aggregate repository for <see cref="RepCommission"/> (per rep + year-month).</summary>
public interface IRepCommissionRepository
{
    Task<RepCommission?> GetAsync(RepresentativeId repId, YearMonth period, CancellationToken ct);
    void Add(RepCommission commission);
}

/// <summary>Aggregate repository for the singleton <see cref="CompensationConfig"/>.</summary>
public interface ICompensationConfigRepository
{
    Task<CompensationConfig?> GetAsync(CancellationToken ct);
    void Add(CompensationConfig config);
}

/// <summary>
/// Read access to achieved monthly volumes used by the compensation engine (from monthly_sample). Kept
/// separate from projections so the engine never over-fetches aggregates.
/// </summary>
public interface ICompensationData
{
    Task<int> GetLabAchievedSamplesAsync(LaboratoryId labId, YearMonth period, CancellationToken ct);
    /// <summary>Achieved samples for every lab in a period, keyed by lab — for the batch recalc (CPN-18).</summary>
    Task<IReadOnlyDictionary<LaboratoryId, int>> GetLabAchievedSamplesForPeriodAsync(YearMonth period, CancellationToken ct);
    Task<int> GetRepAchievedSamplesAsync(RepresentativeId repId, YearMonth period, CancellationToken ct);
}
