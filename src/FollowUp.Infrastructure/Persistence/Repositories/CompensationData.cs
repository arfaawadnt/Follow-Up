using FollowUp.Application.Common.Abstractions.Persistence;
using FollowUp.Domain.Common;
using FollowUp.Domain.Laboratories;
using FollowUp.Domain.Representatives;
using Microsoft.EntityFrameworkCore;

namespace FollowUp.Infrastructure.Persistence.Repositories;

/// <summary>
/// Achieved monthly volumes for the compensation engine (from <c>monthly_sample</c>). Kept separate from the
/// DTO projections so the engine reads exactly what it needs.
/// </summary>
internal sealed class CompensationData : ICompensationData
{
    private readonly FollowUpDbContext _db;
    public CompensationData(FollowUpDbContext db) => _db = db;

    public async Task<int> GetLabAchievedSamplesAsync(LaboratoryId labId, YearMonth period, CancellationToken ct) =>
        await _db.MonthlySamples.Where(m => m.LaboratoryId == labId && m.Period == period)
            .SumAsync(m => (int?)m.SampleCount, ct) ?? 0;

    public async Task<IReadOnlyDictionary<LaboratoryId, int>> GetLabAchievedSamplesForPeriodAsync(YearMonth period, CancellationToken ct) =>
        (await _db.MonthlySamples.AsNoTracking().Where(m => m.Period == period)
            .Select(m => new { m.LaboratoryId, m.SampleCount }).ToListAsync(ct))
        .GroupBy(x => x.LaboratoryId)
        .ToDictionary(g => g.Key, g => g.Sum(x => x.SampleCount));

    public async Task<int> GetRepAchievedSamplesAsync(RepresentativeId repId, YearMonth period, CancellationToken ct) =>
        await _db.MonthlySamples.Where(m => m.CollectorRepId == repId && m.Period == period)
            .SumAsync(m => (int?)m.SampleCount, ct) ?? 0;
}
