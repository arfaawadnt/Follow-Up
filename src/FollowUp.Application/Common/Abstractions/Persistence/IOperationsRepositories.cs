using FollowUp.Domain.Operations;

namespace FollowUp.Application.Common.Abstractions.Persistence;

/// <summary>Aggregate repository for <see cref="DailyVisit"/> (write side; ADR-0005).</summary>
public interface IDailyVisitRepository
{
    Task<DailyVisit?> GetByIdAsync(DailyVisitId id, CancellationToken ct);
    /// <summary>The scheduled-time slots already taken for a lab on a date (the unique-slot key), so a manual
    /// entry can pick a free slot instead of colliding with the (lab, date, time) unique index (finding OPS-006).</summary>
    Task<IReadOnlyList<TimeOnly>> TakenSlotsAsync(Domain.Laboratories.LaboratoryId labId, DateOnly date, CancellationToken ct);
    void Add(DailyVisit visit);
}

/// <summary>Aggregate repository for <see cref="OutsourceSample"/>.</summary>
public interface IOutsourceSampleRepository
{
    Task<OutsourceSample?> GetByIdAsync(OutsourceSampleId id, CancellationToken ct);
    Task<bool> ExistsForAsync(Domain.Laboratories.LaboratoryId labId, DateOnly visitDate, CancellationToken ct);
    void Add(OutsourceSample sample);
    void Remove(OutsourceSample sample);
}

/// <summary>Aggregate repository for <see cref="SampleTracking"/>.</summary>
public interface ISampleTrackingRepository
{
    Task<SampleTracking?> GetByIdAsync(SampleTrackingId id, CancellationToken ct);
    Task<SampleTracking?> GetByAreaDateAsync(string area, DateOnly date, CancellationToken ct);
    void Add(SampleTracking tracking);
    void Remove(SampleTracking tracking);
}
