using FollowUp.Domain.Emailing;

namespace FollowUp.Application.Common.Abstractions.Persistence;

public interface ISmtpConfigRepository
{
    Task<SmtpConfig?> GetAsync(CancellationToken ct);
    void Add(SmtpConfig config);
}

public interface IStatsEmailSubscriptionRepository
{
    Task<IReadOnlyList<StatsEmailSubscription>> GetAllAsync(CancellationToken ct);
    Task<StatsEmailSubscription?> GetByIdAsync(StatsEmailSubscriptionId id, CancellationToken ct);
    void Add(StatsEmailSubscription subscription);
    void Remove(StatsEmailSubscription subscription);
}
