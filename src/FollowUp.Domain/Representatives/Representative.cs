using FollowUp.Domain.Common;

namespace FollowUp.Domain.Representatives;

public readonly record struct RepresentativeId(Guid Value)
{
    public static RepresentativeId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}

/// <summary>
/// A field-workforce member (SRS FR-4). A domain record that may — but need not — be linked to a login
/// account (one login per rep). Carries employment/target attributes used by the commission engine.
/// Optimistic-concurrency guarded (row-version → 409 on stale update).
/// </summary>
public sealed class Representative : AggregateRoot<RepresentativeId>, IVersioned, IAuditable
{
    private Representative() { } // EF

    private Representative(
        RepresentativeId id,
        string fullName,
        RepresentativeType type,
        GoalDuration goalDuration,
        Money salary,
        Money target)
        : base(id)
    {
        FullName = fullName;
        Type = type;
        GoalDuration = goalDuration;
        Salary = salary;
        Target = target;
        IsActive = true;
    }

    public string FullName { get; private set; } = null!;
    public RepresentativeType Type { get; private set; } = null!;
    public GoalDuration GoalDuration { get; private set; } = null!;
    public string? GoalType { get; private set; }
    public string? Metric { get; private set; }
    public Money Salary { get; private set; }
    public Money Target { get; private set; }
    public string? Phone { get; private set; }
    public bool IsActive { get; private set; }

    // Org-scope attribution (used by the 6-dimension scope evaluator).
    public string? Branch { get; private set; }
    public string? Governorate { get; private set; }
    public string? City { get; private set; }
    public string? Area { get; private set; }
    public DateOnly? AppointedOn { get; private set; }

    /// <summary>Employment type (Full-time / Part-time / Contract), matching the reference platform.</summary>
    public string? EmploymentType { get; private set; }

    public uint RowVersion { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public string CreatedBy { get; private set; } = null!;
    public DateTimeOffset? UpdatedAt { get; private set; }
    public string? UpdatedBy { get; private set; }

    public static Representative Register(
        string fullName,
        RepresentativeType type,
        GoalDuration goalDuration,
        Money salary,
        Money target)
    {
        if (string.IsNullOrWhiteSpace(fullName))
            throw new DomainException("Representative name is required.");
        if (salary < Money.Zero || target < Money.Zero)
            throw new DomainException("Salary and target cannot be negative.");

        return new Representative(RepresentativeId.New(), fullName.Trim(), type, goalDuration, salary, target);
    }

    public void UpdateProfile(string fullName, Money salary, Money target, string? goalType, string? metric)
    {
        if (string.IsNullOrWhiteSpace(fullName))
            throw new DomainException("Representative name is required.");
        FullName = fullName.Trim();
        Salary = salary;
        Target = target;
        GoalType = goalType;
        Metric = metric;
    }

    public void SetContact(string? phone) => Phone = phone;

    public void AssignScope(string? branch, string? governorate, string? area = null, string? city = null)
    {
        Branch = branch;
        Governorate = governorate;
        Area = area;
        City = city;
    }

    public void SetEmployment(string? employmentType) => EmploymentType = employmentType;
    public void SetAppointedOn(DateOnly? appointedOn) => AppointedOn = appointedOn;

    public void Deactivate() => IsActive = false;
    public void Reactivate() => IsActive = true;
}
