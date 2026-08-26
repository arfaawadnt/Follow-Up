using FollowUp.Domain.Common;

namespace FollowUp.Domain.Statistics;

public readonly record struct DailyLabStatisticId(Guid Value)
{
    public static DailyLabStatisticId New() => new(Guid.NewGuid());
}

/// <summary>
/// Daily per-lab volumes keyed by (date, lab code) (SRS FR-13). Populated by xlsx import and by Oracle sync;
/// upserted by key. Income is money at fixed precision.
/// </summary>
public sealed class DailyLabStatistic : AggregateRoot<DailyLabStatisticId>
{
    private DailyLabStatistic() { } // EF

    private DailyLabStatistic(DailyLabStatisticId id, DateOnly date, string labCode)
        : base(id)
    {
        Date = date;
        LabCode = labCode;
    }

    public DateOnly Date { get; private set; }
    public string LabCode { get; private set; } = null!;
    public int Registrations { get; private set; }
    public int TestCount { get; private set; }
    public Money Income { get; private set; }

    public static DailyLabStatistic For(DateOnly date, string labCode)
    {
        if (string.IsNullOrWhiteSpace(labCode)) throw new DomainException("Lab code is required.");
        return new DailyLabStatistic(DailyLabStatisticId.New(), date, labCode.Trim().ToUpperInvariant());
    }

    public void Set(int registrations, int testCount, Money income)
    {
        if (registrations < 0 || testCount < 0) throw new DomainException("Counts cannot be negative.");
        if (income < Money.Zero) throw new DomainException("Income cannot be negative.");
        Registrations = registrations;
        TestCount = testCount;
        Income = income;
    }
}

public readonly record struct TestStatisticId(Guid Value)
{
    public static TestStatisticId New() => new(Guid.NewGuid());
}

/// <summary>Per-test daily statistics keyed by (date, test code) (SRS FR-14). Upserted by key.</summary>
public sealed class TestStatistic : AggregateRoot<TestStatisticId>
{
    private TestStatistic() { } // EF

    private TestStatistic(TestStatisticId id, DateOnly date, string testCode)
        : base(id)
    {
        Date = date;
        TestCode = testCode;
    }

    public DateOnly Date { get; private set; }
    public string TestCode { get; private set; } = null!;
    public int Count { get; private set; }
    public Money Income { get; private set; }

    public static TestStatistic For(DateOnly date, string testCode)
    {
        if (string.IsNullOrWhiteSpace(testCode)) throw new DomainException("Test code is required.");
        return new TestStatistic(TestStatisticId.New(), date, testCode.Trim().ToUpperInvariant());
    }

    public void SetCount(int count)
    {
        if (count < 0) throw new DomainException("Count cannot be negative.");
        Count = count;
    }

    public void SetIncome(Money income)
    {
        if (income < Money.Zero) throw new DomainException("Income cannot be negative.");
        Income = income;
    }
}
