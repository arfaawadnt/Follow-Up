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

    public async Task<IReadOnlyList<Laboratory>> GetByIdsAsync(IReadOnlyCollection<LaboratoryId> ids, CancellationToken ct) =>
        await _db.Laboratories.Where(x => ids.Contains(x.Id)).ToListAsync(ct);

    public Task<Laboratory?> GetByCodeAsync(LabCode code, CancellationToken ct) =>
        _db.Laboratories.FirstOrDefaultAsync(x => x.Code == code, ct);

    public Task<bool> CodeExistsAsync(LabCode code, CancellationToken ct) =>
        _db.Laboratories.AnyAsync(x => x.Code == code, ct);

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
