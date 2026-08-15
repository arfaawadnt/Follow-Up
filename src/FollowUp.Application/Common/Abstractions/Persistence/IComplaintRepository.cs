using FollowUp.Domain.Complaints;

namespace FollowUp.Application.Common.Abstractions.Persistence;

/// <summary>Aggregate repository for <see cref="Complaint"/> (write side; ADR-0005).</summary>
public interface IComplaintRepository
{
    Task<Complaint?> GetByIdAsync(ComplaintId id, CancellationToken ct);

    /// <summary>The next sequential complaint number (max+1, gap-free — BR-2).</summary>
    Task<int> NextNumberAsync(CancellationToken ct);

    void Add(Complaint complaint);
}
