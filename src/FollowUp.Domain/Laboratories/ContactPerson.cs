using FollowUp.Domain.Common;

namespace FollowUp.Domain.Laboratories;

public enum ContactRole
{
    Manager = 1,
    Receptionist = 2,
}

public readonly record struct ContactPersonId(Guid Value)
{
    public static ContactPersonId New() => new(Guid.NewGuid());
}

/// <summary>
/// A person at a client lab (data subject, never a system user). Birthdays drive reminder
/// notifications (SRS FR-16). A child entity inside the <see cref="Laboratory"/> aggregate — created,
/// modified and removed only through the lab (CASCADE with its parent).
/// </summary>
public sealed class ContactPerson : Entity<ContactPersonId>
{
    private ContactPerson() { } // EF

    internal ContactPerson(ContactPersonId id, string name, ContactRole role, string? phone, DateOnly? birthday)
        : base(id)
    {
        Name = name;
        Role = role;
        Phone = phone;
        Birthday = birthday;
    }

    public string Name { get; private set; } = null!;
    public ContactRole Role { get; private set; }
    public string? Phone { get; private set; }
    public DateOnly? Birthday { get; private set; }

    internal void Update(string name, ContactRole role, string? phone, DateOnly? birthday)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Contact name is required.");
        Name = name.Trim();
        Role = role;
        Phone = phone;
        Birthday = birthday;
    }

    /// <summary>True when this contact's birthday (month/day) falls on the given date.</summary>
    public bool HasBirthdayOn(DateOnly date) =>
        Birthday is { } b && b.Month == date.Month && b.Day == date.Day;
}
