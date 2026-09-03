using FollowUp.Domain.Statistics;

namespace FollowUp.Application.Common.Abstractions.Persistence;

/// <summary>Aggregate repository for <see cref="DailyLabStatistic"/> (upsert by date + lab code).</summary>
public interface IDailyLabStatisticRepository
{
    Task<DailyLabStatistic?> GetAsync(DateOnly date, string labCode, CancellationToken ct);
    /// <summary>Loads every statistic whose date falls in the inclusive range — used to bulk-upsert an Oracle sync
    /// without a per-row lookup.</summary>
    Task<IReadOnlyList<DailyLabStatistic>> GetRangeAsync(DateOnly from, DateOnly to, CancellationToken ct);
    void Add(DailyLabStatistic stat);
    void Remove(DailyLabStatistic stat);
}

/// <summary>Aggregate repository for <see cref="TestStatistic"/> (upsert by date + test code + test type).</summary>
public interface ITestStatisticRepository
{
    Task<TestStatistic?> GetAsync(DateOnly date, string testCode, int testType, CancellationToken ct);
    /// <summary>Loads every statistic whose date falls in the inclusive range — used to bulk-upsert an Oracle sync
    /// without a per-row lookup.</summary>
    Task<IReadOnlyList<TestStatistic>> GetRangeAsync(DateOnly from, DateOnly to, CancellationToken ct);
    void Add(TestStatistic stat);
    void Remove(TestStatistic stat);
}

/// <summary>Aggregate repository for <see cref="TestGroup"/>.</summary>
public interface ITestGroupRepository
{
    Task<TestGroup?> GetByIdAsync(TestGroupId id, CancellationToken ct);
    Task<TestGroup?> GetByCodeAsync(string code, CancellationToken ct);
    Task<IReadOnlyList<TestGroup>> GetAllAsync(CancellationToken ct);
    void Add(TestGroup group);
    void Remove(TestGroup group);
}

/// <summary>Aggregate repository for <see cref="TestSetup"/>.</summary>
public interface ITestSetupRepository
{
    Task<TestSetup?> GetByIdAsync(TestSetupId id, CancellationToken ct);
    Task<TestSetup?> GetByCodeAsync(string code, int testType, CancellationToken ct);
    Task<IReadOnlyList<TestSetup>> GetByGroupAsync(TestGroupId groupId, CancellationToken ct);
    Task<IReadOnlyList<TestSetup>> GetAllAsync(CancellationToken ct);
    void Add(TestSetup setup);
    void Remove(TestSetup setup);
}
