using FollowUp.Domain.Common;

namespace FollowUp.Domain.Statistics;

/// <summary>Origin of a catalogue record. Oracle-sourced rows are mirrored (add/edit/delete) by the sync;
/// Manual rows are entered on the page and are never removed by the sync (SRS FR-17 catalogue mirroring).</summary>
public enum CatalogueSource
{
    Manual = 0,
    Oracle = 1,
}

public readonly record struct TestGroupId(Guid Value)
{
    public static TestGroupId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}

/// <summary>A grouping in the test catalogue (SRS FR-14). Deleting a group leaves its tests ungrouped (FK SET NULL).</summary>
public sealed class TestGroup : AggregateRoot<TestGroupId>, IAuditable
{
    private TestGroup() { } // EF

    private TestGroup(TestGroupId id, string code, string nameEn, string? nameAr) : base(id)
    {
        Code = code;
        NameEn = nameEn;
        NameAr = nameAr;
    }

    public string Code { get; private set; } = null!;
    public string NameEn { get; private set; } = null!;
    public string? NameAr { get; private set; }
    public CatalogueSource Source { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public string CreatedBy { get; private set; } = null!;
    public DateTimeOffset? UpdatedAt { get; private set; }
    public string? UpdatedBy { get; private set; }

    public static TestGroup Create(string code, string nameEn, string? nameAr)
    {
        if (string.IsNullOrWhiteSpace(code)) throw new DomainException("Test group code is required.");
        if (string.IsNullOrWhiteSpace(nameEn)) throw new DomainException("Test group name is required.");
        return new TestGroup(TestGroupId.New(), code.Trim(), nameEn.Trim(), nameAr?.Trim()) { Source = CatalogueSource.Manual };
    }

    /// <summary>Creates an Oracle-sourced group (mirrored by the sync). Falls back to the code when Oracle has no name.</summary>
    public static TestGroup FromOracle(string code, string? nameEn)
    {
        if (string.IsNullOrWhiteSpace(code)) throw new DomainException("Test group code is required.");
        var name = string.IsNullOrWhiteSpace(nameEn) ? code.Trim() : nameEn.Trim();
        return new TestGroup(TestGroupId.New(), code.Trim(), name, null) { Source = CatalogueSource.Oracle };
    }

    public void Rename(string nameEn, string? nameAr)
    {
        if (string.IsNullOrWhiteSpace(nameEn)) throw new DomainException("Test group name is required.");
        NameEn = nameEn.Trim();
        NameAr = nameAr?.Trim();
    }

    /// <summary>Applies the latest Oracle values and marks the record Oracle-owned (mirror update).</summary>
    public void ApplyOracle(string? nameEn)
    {
        if (!string.IsNullOrWhiteSpace(nameEn)) NameEn = nameEn.Trim();
        Source = CatalogueSource.Oracle;
    }
}

public readonly record struct TestSetupId(Guid Value)
{
    public static TestSetupId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}

/// <summary>A test definition (SRS FR-14). Optionally belongs to a <see cref="TestGroup"/> (SET NULL on group delete).</summary>
public sealed class TestSetup : AggregateRoot<TestSetupId>, IAuditable
{
    private TestSetup() { } // EF

    private TestSetup(TestSetupId id, string code, string nameEn, string? nameAr, TestGroupId? groupId,
        int testType, Money cost) : base(id)
    {
        Code = code;
        NameEn = nameEn;
        NameAr = nameAr;
        GroupId = groupId;
        TestType = testType;
        Cost = cost;
    }

    public string Code { get; private set; } = null!;
    public string NameEn { get; private set; } = null!;
    public string? NameAr { get; private set; }
    public TestGroupId? GroupId { get; private set; }

    /// <summary>Oracle test_type; part of the natural key (code + type). Defaults to 0 for manual entries.</summary>
    public int TestType { get; private set; }
    public Money Cost { get; private set; }
    public CatalogueSource Source { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public string CreatedBy { get; private set; } = null!;
    public DateTimeOffset? UpdatedAt { get; private set; }
    public string? UpdatedBy { get; private set; }

    public static TestSetup Create(string code, string nameEn, string? nameAr, TestGroupId? groupId,
        int testType = 0, Money? cost = null)
    {
        if (string.IsNullOrWhiteSpace(code)) throw new DomainException("Test code is required.");
        if (string.IsNullOrWhiteSpace(nameEn)) throw new DomainException("Test name is required.");
        return new TestSetup(TestSetupId.New(), code.Trim().ToUpperInvariant(), nameEn.Trim(), nameAr?.Trim(),
            groupId, testType, cost ?? Money.Zero) { Source = CatalogueSource.Manual };
    }

    /// <summary>Creates an Oracle-sourced test (mirrored by the sync).</summary>
    public static TestSetup FromOracle(string code, string? nameEn, TestGroupId? groupId, int testType, Money cost)
    {
        if (string.IsNullOrWhiteSpace(code)) throw new DomainException("Test code is required.");
        var name = string.IsNullOrWhiteSpace(nameEn) ? code.Trim() : nameEn.Trim();
        return new TestSetup(TestSetupId.New(), code.Trim().ToUpperInvariant(), name, null, groupId, testType, cost)
            { Source = CatalogueSource.Oracle };
    }

    public void Update(string nameEn, string? nameAr, TestGroupId? groupId, int testType = 0, Money? cost = null)
    {
        if (string.IsNullOrWhiteSpace(nameEn)) throw new DomainException("Test name is required.");
        NameEn = nameEn.Trim();
        NameAr = nameAr?.Trim();
        GroupId = groupId;
        TestType = testType;
        Cost = cost ?? Money.Zero;
    }

    /// <summary>Applies the latest Oracle values and marks the record Oracle-owned (mirror update).</summary>
    public void ApplyOracle(string? nameEn, TestGroupId? groupId, int testType, Money cost)
    {
        if (!string.IsNullOrWhiteSpace(nameEn)) NameEn = nameEn.Trim();
        GroupId = groupId;
        TestType = testType;
        Cost = cost;
        Source = CatalogueSource.Oracle;
    }

    /// <summary>Detaches from its group when the group is deleted (mirrors the SET NULL FK).</summary>
    public void Ungroup() => GroupId = null;
}
