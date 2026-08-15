using FollowUp.Domain.Common;
using FollowUp.Domain.Representatives;

namespace FollowUp.Domain.Laboratories;

public readonly record struct LaboratoryId(Guid Value)
{
    public static LaboratoryId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}

/// <summary>
/// A client laboratory — the hub aggregate of the system (SRS FR-3). Owns its contacts, schedule, geo
/// placement, commercial attributes and rep assignments; enforces unique-code (BR-1), exclusive rep
/// assignment (BR-4), activity-derived status (BR-5) and optimistic concurrency (FR-3).
/// </summary>
public sealed class Laboratory : AggregateRoot<LaboratoryId>, IVersioned, IAuditable
{
    private readonly List<ContactPerson> _contacts = new();

    private Laboratory() { } // EF

    private Laboratory(LaboratoryId id, LabCode code, string name, Segment segment)
        : base(id)
    {
        Code = code;
        Name = name;
        Segment = segment;
        Status = LaboratoryStatus.New;
        Schedule = VisitSchedule.Empty;
        Raise(new LaboratoryRegistered(id, code.Value));
    }

    public LabCode Code { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public Segment Segment { get; private set; } = null!;
    public LaboratoryStatus Status { get; private set; } = null!;

    // Geographic hierarchy (also the org-scope dimensions).
    public string? Branch { get; private set; }
    public string? Governorate { get; private set; }
    public string? City { get; private set; }
    public string? Area { get; private set; }
    public string? Category { get; private set; }

    // Commercial.
    public string? Payer { get; private set; }
    public string? ContractType { get; private set; }

    public VisitSchedule Schedule { get; private set; } = null!;
    public GeoLocation? Location { get; private set; }

    // Rep assignments — exclusive per role (BR-4).
    public RepresentativeId? CollectorRepId { get; private set; }
    public RepresentativeId? MarketingRepId { get; private set; }

    // Loyalty snapshot (per-YM history lives in lab_loyalty_ledger).
    public int MonthlyTarget { get; private set; }
    public int LoyaltyPoints { get; private set; }
    public string? LoyaltyTier { get; private set; }

    public IReadOnlyCollection<ContactPerson> Contacts => _contacts.AsReadOnly();

    public uint RowVersion { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public string CreatedBy { get; private set; } = null!;
    public DateTimeOffset? UpdatedAt { get; private set; }
    public string? UpdatedBy { get; private set; }

    public static Laboratory Register(LabCode code, string name, Segment segment)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Laboratory name is required.");
        return new Laboratory(LaboratoryId.New(), code, name.Trim(), segment);
    }

    public void UpdateProfile(string name, Segment segment, string? payer, string? contractType, string? category)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Laboratory name is required.");
        Name = name.Trim();
        Segment = segment;
        Payer = payer;
        ContractType = contractType;
        Category = category;
    }

    public void PlaceInHierarchy(string? branch, string? governorate, string? city, string? area)
    {
        Branch = branch;
        Governorate = governorate;
        City = city;
        Area = area;
    }

    public void SetLocation(GeoLocation? location) => Location = location;

    public void SetSchedule(VisitSchedule schedule)
    {
        Schedule = schedule ?? VisitSchedule.Empty;
        Raise(new LaboratoryScheduleChanged(Id));
    }

    /// <summary>Assigns the collector rep (BR-4 — exactly one at a time; pass null to clear).</summary>
    public void AssignCollector(RepresentativeId? repId) => CollectorRepId = repId;

    /// <summary>Assigns the marketing rep (BR-4 — exactly one at a time; pass null to clear).</summary>
    public void AssignMarketing(RepresentativeId? repId) => MarketingRepId = repId;

    public void SetLoyalty(int monthlyTarget, int points, string? tier)
    {
        if (monthlyTarget < 0 || points < 0)
            throw new DomainException("Loyalty target and points cannot be negative.");
        MonthlyTarget = monthlyTarget;
        LoyaltyPoints = points;
        LoyaltyTier = tier;
    }

    /// <summary>Sets the monthly loyalty target only (SRS FR-12 set-target), leaving computed points/tier intact.</summary>
    public void SetMonthlyTarget(int monthlyTarget)
    {
        if (monthlyTarget < 0) throw new DomainException("Loyalty target cannot be negative.");
        MonthlyTarget = monthlyTarget;
    }

    /// <summary>Explicit status change through the validated set (audited by the caller).</summary>
    public void ChangeStatus(LaboratoryStatus status)
    {
        if (status == Status) return;
        var from = Status;
        Status = status;
        Raise(new LaboratoryStatusChanged(Id, from.Name, status.Name));
    }

    /// <summary>
    /// Activity-derived promotion (BR-5): a check-in/receipt promotes a dormant lab to Active.
    /// Terminal commercial states (Suspended/Stopped/Churned) are left untouched.
    /// </summary>
    public void DeriveActiveFromActivity()
    {
        if (Status == LaboratoryStatus.New || Status == LaboratoryStatus.Pending ||
            Status == LaboratoryStatus.Inactive || Status == LaboratoryStatus.Scanned)
        {
            ChangeStatus(LaboratoryStatus.Active);
        }
    }

    // --- Contacts (child entities) ---

    public ContactPerson AddContact(string name, ContactRole role, string? phone, DateOnly? birthday)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Contact name is required.");
        var contact = new ContactPerson(ContactPersonId.New(), name.Trim(), role, phone, birthday);
        _contacts.Add(contact);
        return contact;
    }

    public void UpdateContact(ContactPersonId contactId, string name, ContactRole role, string? phone, DateOnly? birthday)
    {
        var contact = _contacts.FirstOrDefault(c => c.Id == contactId)
            ?? throw new DomainException("Contact not found on this laboratory.");
        contact.Update(name, role, phone, birthday);
    }

    public void RemoveContact(ContactPersonId contactId) =>
        _contacts.RemoveAll(c => c.Id == contactId);

    /// <summary>The display code a caller sees: real code when permitted, else the ENC alias (BR-7).</summary>
    public string DisplayCode(bool canSeeEncrypted) =>
        canSeeEncrypted ? Code.Value : Code.ToEncryptedAlias();
}
