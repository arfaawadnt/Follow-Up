using FollowUp.Domain.Common;

namespace FollowUp.Domain.Statistics;

public readonly record struct DailyLabStatisticId(Guid Value)
{
    public static DailyLabStatisticId New() => new(Guid.NewGuid());
}

/// <summary>
/// Daily per-lab volumes keyed by (date, lab code, registration branch) (SRS FR-13). Populated by xlsx import (branch
/// unknown → "") and by the Oracle sync, which since 2026-09-16 splits a lab's day by the branch the registrations were
/// made at (reg.branch_code) so the Lab Statistics page can filter by "Reg Branch". Every consumer that wants a lab's
/// day total sums the rows of that (date, lab code). Income is money at fixed precision.
/// </summary>
public sealed class DailyLabStatistic : AggregateRoot<DailyLabStatisticId>
{
    private DailyLabStatistic() { } // EF

    private DailyLabStatistic(DailyLabStatisticId id, DateOnly date, string labCode, string branch)
        : base(id)
    {
        Date = date;
        LabCode = labCode;
        Branch = branch;
    }

    public DateOnly Date { get; private set; }
    public string LabCode { get; private set; } = null!;
    /// <summary>Registration branch CODE (Oracle reg.branch_code, uppercased); "" when unknown (xlsx import, rows synced before 2026-09-16).</summary>
    public string Branch { get; private set; } = "";
    public int Registrations { get; private set; }
    public int TestCount { get; private set; }
    public Money Income { get; private set; }

    public static DailyLabStatistic For(DateOnly date, string labCode, string? branch = null)
    {
        if (string.IsNullOrWhiteSpace(labCode)) throw new DomainException("Lab code is required.");
        return new DailyLabStatistic(DailyLabStatisticId.New(), date, labCode.Trim().ToUpperInvariant(), NormalizeBranch(branch));
    }

    /// <summary>The key form of a registration branch code: trimmed, uppercased, "" for none.</summary>
    public static string NormalizeBranch(string? branch) => string.IsNullOrWhiteSpace(branch) ? "" : branch.Trim().ToUpperInvariant();

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

/// <summary>
/// Per-test daily statistics keyed by (date, test code, test type) (SRS FR-14). Upserted by key.
/// The type is part of the natural key because Oracle's GLOBAL_TESTS2 reuses the same test_code across
/// test_types for different tests; keying by code alone merges those into one mislabeled row. Manual xlsx
/// imports (which have no Oracle type) use type 0.
/// </summary>
public sealed class TestStatistic : AggregateRoot<TestStatisticId>
{
    private TestStatistic() { } // EF

    private TestStatistic(TestStatisticId id, DateOnly date, string testCode, int testType, string branch)
        : base(id)
    {
        Date = date;
        TestCode = testCode;
        TestType = testType;
        Branch = branch;
    }

    public DateOnly Date { get; private set; }
    public string TestCode { get; private set; } = null!;
    public int TestType { get; private set; }
    /// <summary>The registration's branch code (from <c>reg.branch_code</c>), part of the natural key. Empty for
    /// manual xlsx imports and pre-branch legacy rows; resolved to a branch name at read time.</summary>
    public string Branch { get; private set; } = "";
    public int Count { get; private set; }
    public Money Income { get; private set; }

    public static TestStatistic For(DateOnly date, string testCode, int testType = 0, string? branch = null)
    {
        if (string.IsNullOrWhiteSpace(testCode)) throw new DomainException("Test code is required.");
        return new TestStatistic(TestStatisticId.New(), date, testCode.Trim().ToUpperInvariant(), testType, (branch ?? "").Trim());
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
