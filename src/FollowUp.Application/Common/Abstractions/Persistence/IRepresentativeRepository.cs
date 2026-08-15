using FollowUp.Domain.Representatives;

namespace FollowUp.Application.Common.Abstractions.Persistence;

/// <summary>Aggregate repository for <see cref="Representative"/> (write side; ADR-0005).</summary>
public interface IRepresentativeRepository
{
    Task<Representative?> GetByIdAsync(RepresentativeId id, CancellationToken ct);
    Task<bool> ExistsAsync(RepresentativeId id, CancellationToken ct);
    void Add(Representative representative);
}
