using FollowUp.Application.Common.Abstractions;
using FollowUp.Domain.Accounting;
using FollowUp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FollowUp.Infrastructure.Jobs;

/// <summary>
/// Links every cash collection without a mirroring treasury entry to the treasury serving its lab's branch (Pending).
/// Existing mirrors are NOT refreshed here — the collection handlers own that. Shared by the Treasury page's
/// "Sync collections" action (own SaveChanges) and by the nightly automation (which saves once for all its passes).
/// </summary>
internal sealed class CollectionTreasurySyncRunner : ICollectionTreasurySync
{
    private readonly FollowUpDbContext _db;
    private readonly ILogger<CollectionTreasurySyncRunner> _logger;

    public CollectionTreasurySyncRunner(FollowUpDbContext db, ILogger<CollectionTreasurySyncRunner> logger) { _db = db; _logger = logger; }

    public async Task<CollectionTreasurySyncResult> RunAsync(CancellationToken ct)
    {
        var result = await ReconcileAsync(_db, ct);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Collection → treasury sync (manual): linked {Linked}, unplaced {Unplaced}.", result.Linked, result.Unplaced);
        return result;
    }

    /// <summary>The reconcile pass without SaveChanges, for callers that batch it with other work.</summary>
    public static async Task<CollectionTreasurySyncResult> ReconcileAsync(FollowUpDbContext db, CancellationToken ct)
    {
        var mirrored = await db.TreasuryEntries.AsNoTracking().Where(e => e.CollectionId != null).Select(e => e.CollectionId!.Value).ToListAsync(ct);
        var mirroredSet = mirrored.ToHashSet();
        var unlinked = await db.Collections.AsNoTracking().Where(c => !mirrored.Contains(c.Id)).ToListAsync(ct);
        var linked = 0; var unplaced = 0;
        if (unlinked.Count == 0) return new CollectionTreasurySyncResult(0, 0);

        var activeTreasuries = await db.Treasuries.AsNoTracking().Where(t => t.IsActive).OrderBy(t => t.Name).ToListAsync(ct);
        var labIds = unlinked.Select(c => c.LaboratoryId).Distinct().ToList();
        var labs = await db.Laboratories.AsNoTracking().Where(l => labIds.Contains(l.Id))
            .Select(l => new { l.Id, l.Name, l.Branch }).ToDictionaryAsync(l => l.Id, ct);
        var repIds = unlinked.SelectMany(c => c.RepIds).Distinct().ToList();
        var repNames = await db.Representatives.AsNoTracking().Where(r => repIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, r => r.FullName, ct);
        foreach (var c in unlinked)
        {
            if (c.Cash.Amount <= 0 || mirroredSet.Contains(c.Id)) continue; // bank-only collections never reach a treasury
            if (!labs.TryGetValue(c.LaboratoryId, out var lab) || string.IsNullOrWhiteSpace(lab.Branch)) { unplaced++; continue; }
            var treasury = activeTreasuries.FirstOrDefault(t => t.Branches.Contains(lab.Branch, StringComparer.OrdinalIgnoreCase));
            if (treasury is null) { unplaced++; continue; }
            db.TreasuryEntries.Add(TreasuryEntry.FromCollection(treasury.Id, c, lab.Name, c.RepIds.Select(r => repNames.GetValueOrDefault(r, "—"))));
            mirroredSet.Add(c.Id);
            linked++;
        }
        return new CollectionTreasurySyncResult(linked, unplaced);
    }
}
