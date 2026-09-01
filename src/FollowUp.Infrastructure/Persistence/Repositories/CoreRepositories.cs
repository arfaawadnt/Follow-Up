using FollowUp.Application.Common.Abstractions.Persistence;
using FollowUp.Domain.Laboratories;
using FollowUp.Domain.Representatives;
using Microsoft.EntityFrameworkCore;

namespace FollowUp.Infrastructure.Persistence.Repositories;

internal sealed class LaboratoryRepository : ILaboratoryRepository
{
    private readonly FollowUpDbContext _db;
    public LaboratoryRepository(FollowUpDbContext db) => _db = db;

    public Task<Laboratory?> GetByIdAsync(LaboratoryId id, CancellationToken ct) =>
        _db.Laboratories.FirstOrDefaultAsync(x => x.Id == id, ct);

    public Task<Laboratory?> GetByCodeAsync(LabCode code, CancellationToken ct) =>
        _db.Laboratories.FirstOrDefaultAsync(x => x.Code == code, ct);

    public Task<bool> CodeExistsAsync(LabCode code, CancellationToken ct) =>
        _db.Laboratories.AnyAsync(x => x.Code == code, ct);

    public async Task<string> NextCodeAsync(CancellationToken ct)
    {
        const string prefix = "MGL-";
        // Bounded at seed scale; extracts the numeric suffix of existing codes and returns the next.
        var codes = await _db.Laboratories
            .Select(x => x.Code)
            .ToListAsync(ct);

        var max = codes
            .Select(c => c.Value)
            .Where(v => v.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(v => int.TryParse(v[prefix.Length..], out var n) ? n : 0)
            .DefaultIfEmpty(0)
            .Max();

        return $"{prefix}{max + 1:0000}";
    }

    public async Task<IReadOnlyList<Laboratory>> GetAllAsync(CancellationToken ct) =>
        await _db.Laboratories.ToListAsync(ct);

    public void Add(Laboratory laboratory) => _db.Laboratories.Add(laboratory);
}

internal sealed class RepresentativeRepository : IRepresentativeRepository
{
    private readonly FollowUpDbContext _db;
    public RepresentativeRepository(FollowUpDbContext db) => _db = db;

    public Task<Representative?> GetByIdAsync(RepresentativeId id, CancellationToken ct) =>
        _db.Representatives.FirstOrDefaultAsync(x => x.Id == id, ct);

    public Task<bool> ExistsAsync(RepresentativeId id, CancellationToken ct) =>
        _db.Representatives.AnyAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyList<Representative>> GetAllAsync(CancellationToken ct) =>
        await _db.Representatives.ToListAsync(ct);

    public void Add(Representative representative) => _db.Representatives.Add(representative);
}
