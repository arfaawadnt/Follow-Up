using FollowUp.Domain.Laboratories;

namespace FollowUp.Application.Common.Abstractions.Persistence;

/// <summary>
/// Aggregate repository for <see cref="Laboratory"/> (write side). Loads and persists the whole aggregate
/// (lab + contacts + schedule). Not a generic/per-table repository — it speaks the Laboratory ubiquitous
/// language (ADR-0005).
/// </summary>
public interface ILaboratoryRepository
{
    Task<Laboratory?> GetByIdAsync(LaboratoryId id, CancellationToken ct);

    /// <summary>Loads by business code (case-insensitive) — used for the uniqueness invariant (BR-1).</summary>
    Task<Laboratory?> GetByCodeAsync(LabCode code, CancellationToken ct);

    Task<bool> CodeExistsAsync(LabCode code, CancellationToken ct);

    /// <summary>The next unused sequential lab code (SRS FR-3 next-code helper).</summary>
    Task<string> NextCodeAsync(CancellationToken ct);

    /// <summary>All labs (tracked) — used by the Oracle mirror to upsert/deactivate by code.</summary>
    Task<IReadOnlyList<Laboratory>> GetAllAsync(CancellationToken ct);

    void Add(Laboratory laboratory);
}
