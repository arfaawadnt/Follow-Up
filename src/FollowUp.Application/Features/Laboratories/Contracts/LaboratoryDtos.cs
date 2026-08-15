namespace FollowUp.Application.Features.Laboratories.Contracts;

/// <summary>List-row projection of a laboratory (read model — never the domain entity).</summary>
public sealed record LabListItemDto(
    Guid Id,
    string DisplayCode,
    string Name,
    string Segment,
    string Status,
    string? Governorate,
    string? City,
    string? Area,
    bool Encrypted);

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
    string? Payer,
    string? ContractType,
    double? Latitude,
    double? Longitude,
    int MonthlyTarget,
    int LoyaltyPoints,
    string? LoyaltyTier,
    Guid? CollectorRepId,
    Guid? MarketingRepId,
    IReadOnlyList<string> WorkDays,
    IReadOnlyList<string> VisitTimes,
    IReadOnlyList<ContactDto> Contacts,
    uint RowVersion);
