using FollowUp.Application.Common.Models;
using FollowUp.Domain.Identity;

namespace FollowUp.Application.Features.Complaints.Contracts;

public sealed record ComplaintListItemDto(
    Guid Id, string Reference, Guid LaboratoryId, string LabDisplayCode, string Lab, string? LabCategory,
    string Category, string Via, string? AssignedTo, string Description, string Status, string Stage, int AgeDays,
    string? Resolution, DateTimeOffset CreatedAt);

public sealed record ComplaintDetailDto(
    Guid Id, string Reference, Guid LaboratoryId, string LabDisplayCode, string Lab, string Category, string ViaChannel,
    string? AssignedTeam, string Details, string Status, string Stage, DateTimeOffset? ResolvedAt, string? ResolvedBy,
    Guid? RepresentativeId, string? RepresentativeName, DateTimeOffset? ReceivedAt,
    bool? IsValid, string? ValidityNotes, string? InvestigationNotes,
    string? OutcomeType, string? OutcomeSummary, string? ResolutionSummary, DateTimeOffset CreatedAt);

public sealed record ComplaintAuditRowDto(
    DateTimeOffset OccurredAt, string Actor, string Action, string? Before, string? After);

/// <summary>Read-side query interface for complaints (ADR-0005).</summary>
public interface IComplaintQueries
{
    Task<PagedResult<ComplaintListItemDto>> SearchAsync(ComplaintSearchCriteria criteria, OrgScope scope,
        bool canSeeEncrypted, CancellationToken ct);
    Task<ComplaintDetailDto?> GetByIdAsync(Guid id, bool canSeeEncrypted, CancellationToken ct);
    Task<IReadOnlyList<ComplaintAuditRowDto>> GetAuditAsync(Guid id, CancellationToken ct);
}

public sealed record ComplaintSearchCriteria : ListQuery
{
    public string? Status { get; init; }
    public string? Category { get; init; }
    public Guid? LaboratoryId { get; init; }
}
