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
    private readonly List<RepresentativeId> _collectorRepIds = new();
    private readonly List<string> _imagePaths = new();

    private Laboratory() { } // EF

    private Laboratory(LaboratoryId id, LabCode code, string name, string segment)
        : base(id)
    {
        Code = code;
        Name = name;
        Segment = segment;
        Status = LaboratoryStatus.Interactive;
        Schedule = VisitSchedule.Empty;
        Raise(new LaboratoryRegistered(id, code.Value));
    }

    public LabCode Code { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    /// <summary>Commercial segment code (configurable reference data, RefType.Segment). Validated at the application layer.</summary>
    public string Segment { get; private set; } = null!;
    public LaboratoryStatus Status { get; private set; } = null!;

    // Geographic hierarchy (also the org-scope dimensions).
    public string? Branch { get; private set; }
    public string? Governorate { get; private set; }
    public string? City { get; private set; }
    public string? Area { get; private set; }
    public string? Category { get; private set; }
    public string? Address { get; private set; }

    /// <summary>External statistics/mapping code (e.g. the Oracle lab code used by imports).</summary>
    public string? MappingCode { get; private set; }
    /// <summary>Confidential lab: code is masked for users without ShowEncryptedLabs (BR-7).</summary>
    public bool IsEncrypted { get; private set; }

    // Commercial.
    public string? Payer { get; private set; }
    public string? ContractType { get; private set; }
    public string? LicenseNo { get; private set; }
    public DateOnly? LicenseDate { get; private set; }
    public int? AvgMonthlySamples { get; private set; }
    public string? PreferredChannel { get; private set; }

    public VisitSchedule Schedule { get; private set; } = null!;
    public GeoLocation? Location { get; private set; }

    // Rep assignments — multiple collectors, single marketing (matches the reference platform).
    public IReadOnlyCollection<RepresentativeId> CollectorRepIds => _collectorRepIds.AsReadOnly();
    public IReadOnlyCollection<string> ImagePaths => _imagePaths.AsReadOnly();
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

    public static Laboratory Register(LabCode code, string name, string segment, LaboratoryStatus? status = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Laboratory name is required.");
        if (string.IsNullOrWhiteSpace(segment))
            throw new DomainException("Segment is required.");
        var lab = new Laboratory(LaboratoryId.New(), code, name.Trim(), segment.Trim());
        if (status is not null) lab.Status = status;
        return lab;
    }

    public void UpdateProfile(string name, string segment, string? payer, string? contractType, string? category,
        string? licenseNo, DateOnly? licenseDate, int? avgMonthlySamples, string? preferredChannel)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Laboratory name is required.");
        if (string.IsNullOrWhiteSpace(segment))
            throw new DomainException("Segment is required.");
        if (avgMonthlySamples is < 0)
            throw new DomainException("Average monthly samples cannot be negative.");
        Name = name.Trim();
        Segment = segment.Trim();
        Payer = payer;
        ContractType = contractType;
        Category = category;
        LicenseNo = licenseNo;
        LicenseDate = licenseDate;
        AvgMonthlySamples = avgMonthlySamples;
        PreferredChannel = preferredChannel;
    }

    public void PlaceInHierarchy(string? branch, string? governorate, string? city, string? area)
    {
        // Normalized like Name/Segment — sample-tracking rows key on the exact Area string.
        static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        var oldArea = Area;
        Branch = Clean(branch);
        Governorate = Clean(governorate);
        City = Clean(city);
        Area = Clean(area);
        if (!string.Equals(oldArea, Area, StringComparison.Ordinal))
            Raise(new LaboratoryAreaChanged(Id, oldArea, Area));
    }

    public void SetLocation(GeoLocation? location) => Location = location;

    public void SetAddress(string? address) => Address = string.IsNullOrWhiteSpace(address) ? null : address.Trim();

    public void SetMappingCode(string? mappingCode) =>
        MappingCode = string.IsNullOrWhiteSpace(mappingCode) ? null : mappingCode.Trim();

    public void SetEncrypted(bool isEncrypted) => IsEncrypted = isEncrypted;

    /// <summary>Replaces the attached image paths (uploaded separately, linked on save).</summary>
    public void SetImages(IEnumerable<string> paths)
    {
        _imagePaths.Clear();
        _imagePaths.AddRange(paths.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()).Distinct());
    }

    public void SetSchedule(VisitSchedule schedule)
    {
        Schedule = schedule ?? VisitSchedule.Empty;
        Raise(new LaboratoryScheduleChanged(Id));
    }

    /// <summary>Assigns the lab's collector reps (multiple allowed, matching the reference); replaces the set.</summary>
    public void AssignCollectors(IEnumerable<RepresentativeId> repIds)
    {
        _collectorRepIds.Clear();
        _collectorRepIds.AddRange(repIds.Distinct());
    }

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
        if (Status == LaboratoryStatus.Interactive || Status == LaboratoryStatus.Pending ||
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
}
