using FollowUp.Application.Common.Abstractions;
using FollowUp.Application.Common.Abstractions.Persistence;
using FollowUp.Application.Features.Setup;
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
public sealed class RetentionService : IRetentionRunner
{
    // A pending attachment is bound to its visit within the same upload→record flow (seconds/minutes). One left
    // unbound for this long is an abandoned upload whose bytes can be reclaimed (finding OPS-008).
    private static readonly TimeSpan OrphanAttachmentMaxAge = TimeSpan.FromHours(24);

    private readonly FollowUpDbContext _db;
    private readonly IAppSettingRepository _settings;
    private readonly IAttachmentStorage _attachments;
    private readonly IClock _clock;
    private readonly ILogger<RetentionService> _logger;

    public RetentionService(FollowUpDbContext db, IAppSettingRepository settings, IAttachmentStorage attachments,
        IClock clock, ILogger<RetentionService> logger)
    {
        _db = db;
        _settings = settings;
        _attachments = attachments;
        _clock = clock;
        _logger = logger;
    }

    public async Task<int> PurgeAsync(CancellationToken ct = default)
    {
        // Always sweep abandoned uploads, independent of whether a retention window is configured.
        await SweepOrphanAttachmentsAsync(ct);

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

    /// <summary>
    /// Deletes visit attachments that were uploaded but never bound to a visit (VisitId null) and are older than
    /// <see cref="OrphanAttachmentMaxAge"/> — abandoned uploads whose bytes would otherwise accumulate on the
    /// private volume forever (finding OPS-008). Removes the file first (best-effort), then the row.
    /// </summary>
    private async Task<int> SweepOrphanAttachmentsAsync(CancellationToken ct)
    {
        var cutoff = _clock.UtcNow - OrphanAttachmentMaxAge;
        var orphans = await _db.VisitAttachments
            .Where(a => a.VisitId == null && a.CreatedAt < cutoff)
            .ToListAsync(ct);
        if (orphans.Count == 0) return 0;

        foreach (var orphan in orphans)
        {
            await _attachments.DeleteAsync(orphan.StoredName, ct);
            _db.VisitAttachments.Remove(orphan);
        }
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Swept {Count} orphaned (never-bound) visit attachments older than {Cutoff:O}", orphans.Count, cutoff);
        return orphans.Count;
    }
}
