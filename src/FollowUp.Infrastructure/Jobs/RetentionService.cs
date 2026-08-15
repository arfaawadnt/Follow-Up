using FollowUp.Application.Common.Abstractions;
using FollowUp.Application.Common.Abstractions.Persistence;
using FollowUp.Domain.Audit;
using FollowUp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FollowUp.Infrastructure.Jobs;

/// <summary>
/// Bounded data-retention purge (SRS FR-18/FR-20/NFR-PRIV-2). Reads the retention window (minimum 30 days;
/// default keep-everything), writes a summary audit entry FIRST, then deletes rows older than a
/// transaction-local declared cutoff — the only permitted audit deletion, gated by the DB GUC.
/// </summary>
public sealed class RetentionService
{
    private readonly FollowUpDbContext _db;
    private readonly IAppSettingRepository _settings;
    private readonly IClock _clock;
    private readonly ILogger<RetentionService> _logger;

    public RetentionService(FollowUpDbContext db, IAppSettingRepository settings, IClock clock, ILogger<RetentionService> logger)
    {
        _db = db;
        _settings = settings;
        _clock = clock;
        _logger = logger;
    }

    public async Task<int> PurgeAsync(CancellationToken ct = default)
    {
        var setting = await _settings.GetAsync("retention.days", ct);
        if (!int.TryParse(setting?.Value, out var days))
        {
            _logger.LogInformation("Retention not configured (keep-everything); nothing purged.");
            return 0;
        }
        if (days < 30) days = 30; // enforced minimum (SRS FR-18)

        var cutoff = _clock.UtcNow.AddDays(-days);

        // Summary audit entry written and committed BEFORE the purge (FR-20).
        _db.Set<AuditEntry>().Add(AuditEntry.Record(_clock.UtcNow, "system", "Retention", "purge", "Purge",
            null, $"{{\"cutoff\":\"{cutoff:O}\",\"days\":{days}}}", "retention-job"));
        await _db.SaveChangesAsync(ct);

        // Purge older-than-cutoff rows. Audit deletion is permitted only under the GUC, in the same batch.
        var affected = await _db.Database.ExecuteSqlInterpolatedAsync($@"
SET followup.allow_audit_purge='on';
DELETE FROM user_session WHERE COALESCE(revoked_at, expires_at) < {cutoff};
DELETE FROM notification_delivery_log WHERE queued_at < {cutoff};
DELETE FROM audit_entry WHERE occurred_at < {cutoff};", ct);

        _logger.LogInformation("Retention purge removed {Count} rows older than {Cutoff:O}", affected, cutoff);
        return affected;
    }
}
