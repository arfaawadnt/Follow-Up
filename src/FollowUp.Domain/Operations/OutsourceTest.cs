using FollowUp.Domain.Common;

namespace FollowUp.Domain.Operations;

public readonly record struct OutsourceTestId(Guid Value)
{
    public static OutsourceTestId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}

/// <summary>
/// One outsourced test line under an <see cref="OutsourceSample"/> (owned child): the picked test, its
/// sample-volume category (Small/Medium/Large) and the fees. Net revenue is derived (test − outsource) and
/// recomputed server-side, never trusted from the client (BR-9).
/// </summary>
public sealed class OutsourceTest
{
    /// <summary>The allowed sample-volume categories.</summary>
    public static readonly IReadOnlyList<string> Volumes = new[] { "Small", "Medium", "Large" };

    private OutsourceTest() { } // EF

    private OutsourceTest(OutsourceTestId id, string testCode, string testName, string sampleVolume,
        Money testFees, Money outsourceFees)
    {
        Id = id;
        TestCode = testCode;
        TestName = testName;
        SampleVolume = sampleVolume;
        TestFees = testFees;
        OutsourceFees = outsourceFees;
    }

    public OutsourceTestId Id { get; private set; }
    public string TestCode { get; private set; } = null!;
    public string TestName { get; private set; } = null!;
    /// <summary>Sample-volume category: Small / Medium / Large.</summary>
    public string SampleVolume { get; private set; } = null!;
    public Money TestFees { get; private set; }
    public Money OutsourceFees { get; private set; }
    /// <summary>Derived: test fees − outsource fees (never persisted; recomputed here).</summary>
    public Money NetRevenue => TestFees - OutsourceFees;

    public static OutsourceTest Create(string testCode, string testName, string sampleVolume, decimal testFees, decimal outsourceFees)
    {
        if (string.IsNullOrWhiteSpace(testCode)) throw new DomainException("Test is required.");
        if (string.IsNullOrWhiteSpace(testName)) throw new DomainException("Test name is required.");
        var volume = (sampleVolume ?? string.Empty).Trim();
        if (!Volumes.Contains(volume)) throw new DomainException("Sample volume must be Small, Medium or Large.");
        if (testFees < 0 || outsourceFees < 0) throw new DomainException("Fees cannot be negative.");
        return new OutsourceTest(OutsourceTestId.New(), testCode.Trim().ToUpperInvariant(), testName.Trim(), volume,
            new Money(testFees), new Money(outsourceFees));
    }
}
