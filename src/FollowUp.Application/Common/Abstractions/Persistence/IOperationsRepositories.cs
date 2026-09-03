using FollowUp.Domain.Operations;

namespace FollowUp.Application.Common.Abstractions.Persistence;

/// <summary>Aggregate repository for <see cref="DailyVisit"/> (write side; ADR-0005).</summary>
public interface IDailyVisitRepository
{
    Task<DailyVisit?> GetByIdAsync(DailyVisitId id, CancellationToken ct);
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
