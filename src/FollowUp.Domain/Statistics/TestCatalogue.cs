using FollowUp.Domain.Common;

namespace FollowUp.Domain.Statistics;

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

    public DateTimeOffset CreatedAt { get; private set; }
    public string CreatedBy { get; private set; } = null!;
    public DateTimeOffset? UpdatedAt { get; private set; }
    public string? UpdatedBy { get; private set; }

    public static TestGroup Create(string code, string nameEn, string? nameAr)
    {
        if (string.IsNullOrWhiteSpace(code)) throw new DomainException("Test group code is required.");
        if (string.IsNullOrWhiteSpace(nameEn)) throw new DomainException("Test group name is required.");
        return new TestGroup(TestGroupId.New(), code.Trim(), nameEn.Trim(), nameAr?.Trim());
    }

    public void Rename(string nameEn, string? nameAr)
    {
        if (string.IsNullOrWhiteSpace(nameEn)) throw new DomainException("Test group name is required.");
        NameEn = nameEn.Trim();
        NameAr = nameAr?.Trim();
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

    private TestSetup(TestSetupId id, string code, string nameEn, string? nameAr, TestGroupId? groupId) : base(id)
    {
        Code = code;
        NameEn = nameEn;
        NameAr = nameAr;
        GroupId = groupId;
    }

    public string Code { get; private set; } = null!;
    public string NameEn { get; private set; } = null!;
    public string? NameAr { get; private set; }
    public TestGroupId? GroupId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public string CreatedBy { get; private set; } = null!;
    public DateTimeOffset? UpdatedAt { get; private set; }
    public string? UpdatedBy { get; private set; }

    public static TestSetup Create(string code, string nameEn, string? nameAr, TestGroupId? groupId)
    {
        if (string.IsNullOrWhiteSpace(code)) throw new DomainException("Test code is required.");
        if (string.IsNullOrWhiteSpace(nameEn)) throw new DomainException("Test name is required.");
        return new TestSetup(TestSetupId.New(), code.Trim().ToUpperInvariant(), nameEn.Trim(), nameAr?.Trim(), groupId);
    }

    public void Update(string nameEn, string? nameAr, TestGroupId? groupId)
    {
        if (string.IsNullOrWhiteSpace(nameEn)) throw new DomainException("Test name is required.");
        NameEn = nameEn.Trim();
        NameAr = nameAr?.Trim();
        GroupId = groupId;
    }

    /// <summary>Detaches from its group when the group is deleted (mirrors the SET NULL FK).</summary>
    public void Ungroup() => GroupId = null;
}
