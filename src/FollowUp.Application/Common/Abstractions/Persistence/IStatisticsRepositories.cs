using FollowUp.Domain.Statistics;

namespace FollowUp.Application.Common.Abstractions.Persistence;

/// <summary>Aggregate repository for <see cref="DailyLabStatistic"/> (upsert by date + lab code).</summary>
public interface IDailyLabStatisticRepository
{
    Task<DailyLabStatistic?> GetAsync(DateOnly date, string labCode, CancellationToken ct);
    void Add(DailyLabStatistic stat);
}

/// <summary>Aggregate repository for <see cref="TestStatistic"/> (upsert by date + test code).</summary>
public interface ITestStatisticRepository
{
    Task<TestStatistic?> GetAsync(DateOnly date, string testCode, CancellationToken ct);
    void Add(TestStatistic stat);
}

/// <summary>Aggregate repository for <see cref="TestGroup"/>.</summary>
public interface ITestGroupRepository
{
    Task<TestGroup?> GetByIdAsync(TestGroupId id, CancellationToken ct);
    void Add(TestGroup group);
    void Remove(TestGroup group);
}

/// <summary>Aggregate repository for <see cref="TestSetup"/>.</summary>
public interface ITestSetupRepository
{
    Task<TestSetup?> GetByIdAsync(TestSetupId id, CancellationToken ct);
    Task<IReadOnlyList<TestSetup>> GetByGroupAsync(TestGroupId groupId, CancellationToken ct);
    void Add(TestSetup setup);
    void Remove(TestSetup setup);
}
