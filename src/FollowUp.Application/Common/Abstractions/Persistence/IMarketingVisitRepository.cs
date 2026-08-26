using FollowUp.Domain.Marketing;

namespace FollowUp.Application.Common.Abstractions.Persistence;

/// <summary>Aggregate repository for <see cref="MarketingVisit"/> (write side; ADR-0005).</summary>
public interface IMarketingVisitRepository
{
    Task<MarketingVisit?> GetByIdAsync(MarketingVisitId id, CancellationToken ct);
    Task<int> NextNumberAsync(CancellationToken ct);
    void Add(MarketingVisit visit);
}
