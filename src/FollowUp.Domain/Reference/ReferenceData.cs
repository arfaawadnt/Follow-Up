using FollowUp.Domain.Common;
using FollowUp.Domain.Representatives;

namespace FollowUp.Domain.Reference;

/// <summary>The kinds of reference item (SRS FR-18). Used to partition the single ref-item catalogue.</summary>
public sealed class RefType : Enumeration
{
    public static readonly RefType Governorate = new(1, nameof(Governorate));
    public static readonly RefType Branch = new(2, nameof(Branch));
    public static readonly RefType MarketingPurpose = new(3, nameof(MarketingPurpose));
    public static readonly RefType ComplaintCategory = new(4, nameof(ComplaintCategory));
    public static readonly RefType Team = new(5, nameof(Team));
    public static readonly RefType Channel = new(6, nameof(Channel));
    public static readonly RefType Payer = new(7, nameof(Payer));
    public static readonly RefType ContractType = new(8, nameof(ContractType));
    public static readonly RefType LabCategory = new(9, nameof(LabCategory));

    private RefType(int id, string name) : base(id, name) { }
}

public readonly record struct RefItemId(Guid Value)
{
    public static RefItemId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}

/// <summary>
/// A single reference-catalogue item (governorate, branch, purpose, category, team, channel, payer,
/// contract type, lab category). Bilingual. Renames cascade to referencing records at the application layer.
/// </summary>
public sealed class RefItem : AggregateRoot<RefItemId>, IAuditable
{
    private RefItem() { } // EF

    private RefItem(RefItemId id, RefType type, string code, string nameEn, string? nameAr, int sortOrder) : base(id)
    {
        Type = type;
        Code = code;
        NameEn = nameEn;
        NameAr = nameAr;
        SortOrder = sortOrder;
    }

    public RefType Type { get; private set; } = null!;
    public string Code { get; private set; } = null!;
    public string NameEn { get; private set; } = null!;
    public string? NameAr { get; private set; }
    public int SortOrder { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public string CreatedBy { get; private set; } = null!;
    public DateTimeOffset? UpdatedAt { get; private set; }
    public string? UpdatedBy { get; private set; }

    public static RefItem Create(RefType type, string code, string nameEn, string? nameAr, int sortOrder = 0)
    {
        if (string.IsNullOrWhiteSpace(code)) throw new DomainException("Reference code is required.");
        if (string.IsNullOrWhiteSpace(nameEn)) throw new DomainException("Reference name is required.");
        return new RefItem(RefItemId.New(), type, code.Trim(), nameEn.Trim(), nameAr?.Trim(), sortOrder);
    }

    public void Rename(string nameEn, string? nameAr)
    {
        if (string.IsNullOrWhiteSpace(nameEn)) throw new DomainException("Reference name is required.");
        NameEn = nameEn.Trim();
        NameAr = nameAr?.Trim();
    }

    public void Reorder(int sortOrder) => SortOrder = sortOrder;
}

public readonly record struct CityId(Guid Value)
{
    public static CityId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}

/// <summary>A city within a governorate (SRS FR-18).</summary>
public sealed class City : AggregateRoot<CityId>, IAuditable
{
    private City() { } // EF

    private City(CityId id, string name, string governorate) : base(id)
    {
        Name = name;
        Governorate = governorate;
    }

    public string Name { get; private set; } = null!;
    public string Governorate { get; private set; } = null!;

    public DateTimeOffset CreatedAt { get; private set; }
    public string CreatedBy { get; private set; } = null!;
    public DateTimeOffset? UpdatedAt { get; private set; }
    public string? UpdatedBy { get; private set; }

    public static City Create(string name, string governorate)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("City name is required.");
        if (string.IsNullOrWhiteSpace(governorate)) throw new DomainException("Governorate is required.");
        return new City(CityId.New(), name.Trim(), governorate.Trim());
    }

    public void Rename(string name) => Name = string.IsNullOrWhiteSpace(name)
        ? throw new DomainException("City name is required.") : name.Trim();
}

public readonly record struct AreaId(Guid Value)
{
    public static AreaId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}

/// <summary>
/// An area within a city (SRS FR-18). Carries a transportation-required flag and the transfer reps that
/// serve it (used by the transfers module, FR-6).
/// </summary>
public sealed class Area : AggregateRoot<AreaId>, IAuditable
{
    private readonly List<RepresentativeId> _transferReps = new();

    private Area() { } // EF

    private Area(AreaId id, string name, CityId cityId, bool transportationRequired) : base(id)
    {
        Name = name;
        CityId = cityId;
        TransportationRequired = transportationRequired;
    }

    public string Name { get; private set; } = null!;
    public CityId CityId { get; private set; }
    public bool TransportationRequired { get; private set; }
    public IReadOnlyCollection<RepresentativeId> TransferReps => _transferReps.AsReadOnly();

    public DateTimeOffset CreatedAt { get; private set; }
    public string CreatedBy { get; private set; } = null!;
    public DateTimeOffset? UpdatedAt { get; private set; }
    public string? UpdatedBy { get; private set; }

    public static Area Create(string name, CityId cityId, bool transportationRequired)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("Area name is required.");
        return new Area(AreaId.New(), name.Trim(), cityId, transportationRequired);
    }

    public void SetTransportation(bool required) => TransportationRequired = required;

    public void SetTransferReps(IEnumerable<RepresentativeId> reps)
    {
        _transferReps.Clear();
        _transferReps.AddRange(reps.Distinct());
    }
}
