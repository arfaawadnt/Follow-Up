namespace FollowUp.Application.Features.Laboratories.Contracts;

/// <summary>List-row projection of a laboratory (read model — never the domain entity).</summary>
public sealed record LabListItemDto(
    Guid Id,
    string DisplayCode,
    string Name,
    string Segment,
    string Status,
    string? Branch,
    string? Governorate,
    string? City,
    string? Area,
    string? Category,
    int? AvgMonthlySamples,
    double? Latitude,
    double? Longitude,
    IReadOnlyList<string> Collectors,
    string? Marketing,
    bool Encrypted,
    string Source = "Manual",
    /// <summary>Ids of the lab's assigned collectors — the manual-visit dialog filters its collector picker to these.</summary>
    IReadOnlyList<Guid>? CollectorRepIds = null,
    /// <summary>Operator-managed Credit flag (the lab settles on credit); never touched by the Oracle sync.</summary>
    bool Credit = false,
    /// <summary>Display name of the lab's responsible rep (type LabResponsible); null when unassigned.</summary>
    string? Responsible = null);

/// <summary>Contact-person projection.</summary>
public sealed record ContactDto(Guid Id, string Name, string Role, string? Phone, DateOnly? Birthday);

/// <summary>Full detail projection of a laboratory.</summary>
public sealed record LabDetailDto(
    Guid Id,
    string DisplayCode,
    string Name,
    string Segment,
    string Status,
    string? Branch,
    string? Governorate,
    string? City,
    string? Area,
    string? Category,
    string? Address,
    string? MappingCode,
    bool IsEncrypted,
    IReadOnlyList<string> Images,
    string? Payer,
    string? ContractType,
    string? LicenseNo,
    DateOnly? LicenseDate,
    int? AvgMonthlySamples,
    string? PreferredChannel,
    double? Latitude,
    double? Longitude,
    int MonthlyTarget,
    int LoyaltyPoints,
    string? LoyaltyTier,
    IReadOnlyList<Guid> CollectorRepIds,
    Guid? MarketingRepId,
    IReadOnlyList<string> WorkDays,
    IReadOnlyList<string> VisitTimes,
    IReadOnlyList<ContactDto> Contacts,
    uint RowVersion,
    /// <summary>Operator-managed Credit flag (the lab settles on credit); never touched by the Oracle sync.</summary>
    bool Credit = false,
    /// <summary>The lab's responsible rep (type LabResponsible); null when unassigned.</summary>
    Guid? ResponsibleRepId = null);

/// <summary>The lightweight lab picker row (GET /labs/lookup): every lab in the caller's scope, id + masked display code
/// + name. Exists because the paged list capped pickers at 500 of ~13k labs; this carries only what a picker shows.</summary>
public sealed record LabLookupDto(Guid Id, string DisplayCode, string Name);
