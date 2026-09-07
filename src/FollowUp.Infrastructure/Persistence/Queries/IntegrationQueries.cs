using FollowUp.Application.Features.Integration;
using Microsoft.EntityFrameworkCore;

namespace FollowUp.Infrastructure.Persistence.Queries;

/// <summary>
/// Read-side projection of the Oracle integration config (SRS FR-17). The connection string is never selected,
/// so it can never be returned. Replaces the query handler's former write-side repository dependency (finding M-21).
/// </summary>
internal sealed class IntegrationQueries : IIntegrationQueries
{
    private readonly FollowUpDbContext _db;
    public IntegrationQueries(FollowUpDbContext db) => _db = db;

    public async Task<OracleConfigDto?> GetConfigAsync(CancellationToken ct)
    {
        var cfg = await _db.OracleConfigs.AsNoTracking().FirstOrDefaultAsync(ct);
        return cfg is null
            ? null
            : new OracleConfigDto(cfg.Enabled, cfg.IntervalHours,
                cfg.Queries.Select(q => q.Name).ToArray(), cfg.LastSyncAt, cfg.LastStatus);
    }
}
