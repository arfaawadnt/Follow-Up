using FollowUp.Application.Common.Models;
using FollowUp.Domain.Identity;

namespace FollowUp.Application.Features.Representatives.Contracts;

public sealed record RepListItemDto(
    Guid Id, string FullName, string Type, string GoalDuration, string? GoalType, string? Metric,
    decimal Target, decimal Salary, string? Phone, int AssignedCount, bool IsActive,
    string? Branch, string? Governorate, string? City, string? Area, string? EmploymentType, DateOnly? AppointedOn,
    string Source = "Manual");

public sealed record RepDetailDto(
    Guid Id, string FullName, string Type, string GoalDuration, string? GoalType, string? Metric,
    decimal Salary, decimal Target, string? Phone, string? Branch, string? Governorate, string? City, string? Area,
    string? EmploymentType, DateOnly? AppointedOn, bool IsActive, uint RowVersion);

/// <summary>Read-side query interface for representatives (ADR-0005).</summary>
public interface IRepresentativeQueries
{
    Task<PagedResult<RepListItemDto>> SearchAsync(RepSearchCriteria criteria, OrgScope scope, CancellationToken ct);
    Task<RepDetailDto?> GetByIdAsync(Guid id, CancellationToken ct);
}

public sealed record RepSearchCriteria : ListQuery
{
    public string? Type { get; init; }
    public bool? ActiveOnly { get; init; }
}
