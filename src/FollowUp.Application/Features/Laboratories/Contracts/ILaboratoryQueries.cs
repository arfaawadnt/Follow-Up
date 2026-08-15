using FollowUp.Application.Common.Models;
using FollowUp.Domain.Identity;

namespace FollowUp.Application.Features.Laboratories.Contracts;

/// <summary>
/// Read-side query interface for laboratories (ADR-0005). Implemented in Infrastructure with EF projections
/// straight to DTOs — no aggregate hydration, no IQueryable crossing the boundary. Scope is applied in SQL.
/// </summary>
public interface ILaboratoryQueries
{
    Task<PagedResult<LabListItemDto>> SearchAsync(
        LabSearchCriteria criteria, OrgScope scope, bool canSeeEncrypted, CancellationToken ct);

    Task<LabDetailDto?> GetByIdAsync(Guid id, bool canSeeEncrypted, CancellationToken ct);
}

/// <summary>Filter criteria for a laboratory search.</summary>
public sealed record LabSearchCriteria : ListQuery
{
    public string? Status { get; init; }
    public string? Segment { get; init; }
    public string? Governorate { get; init; }
}
