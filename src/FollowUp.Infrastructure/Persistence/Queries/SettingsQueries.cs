using FollowUp.Application.Features.Setup;
using Microsoft.EntityFrameworkCore;

namespace FollowUp.Infrastructure.Persistence.Queries;

/// <summary>Lists application settings with secret values masked (SRS FR-2/NFR-SEC-7).</summary>
internal sealed class SettingsQueries : ISettingsQueries
{
    private readonly FollowUpDbContext _db;
    public SettingsQueries(FollowUpDbContext db) => _db = db;

    public async Task<IReadOnlyList<SettingDto>> ListAsync(CancellationToken ct)
    {
        var settings = await _db.Settings.AsNoTracking().OrderBy(s => s.Id).ToListAsync(ct);
        return settings
            .Select(s => new SettingDto(s.Key, s.IsSecret ? "********" : s.Value, s.IsSecret))
            .ToList();
    }
}
