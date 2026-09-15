using FollowUp.Application.Common.Abstractions;
using FollowUp.Application.Features.Accounting;
using FollowUp.Domain.Accounting;
using FollowUp.Domain.Common;
using FollowUp.Domain.Reference;
using FollowUp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FollowUp.Infrastructure.Jobs;

/// <summary>
/// The deductions automation (operator decisions, 2026-09-15). Two idempotent passes, one SaveChanges:
/// <list type="number">
/// <item><b>Penalty reconcile</b> — every penalty record gets its mirroring AutoPenalty deduction if it has none (the
/// write handlers keep them in step synchronously; this covers records that predate the automation and labs that were
/// placed in an area later). Values are NOT refreshed here — the penalty handlers own that.</item>
/// <item><b>Percentage Deal for the month of <c>through</c></b> — for every area with an active deal: income of the area's
/// labs from the 1st through <c>through</c> × deal %, written to the area's single AutoDeal row for that month
/// (created on first sight). Rows an operator adjusted are left alone. Only that one month is touched, so a month
/// that has ended is never recalculated once the calendar moves on — except that the run on the 1st, whose latest
/// synced day is the previous month's last day, completes that month with its final day's income.</item>
/// </list>
/// Runs as the system principal (global scope): the automation sees every area and lab.
/// </summary>
internal sealed class DeductionAutomationRunner : IDeductionAutomationRunner
{
    private readonly FollowUpDbContext _db;
    private readonly ILogger<DeductionAutomationRunner> _logger;

    public DeductionAutomationRunner(FollowUpDbContext db, ILogger<DeductionAutomationRunner> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<DeductionAutomationResult> RunAsync(DateOnly through, bool manual, CancellationToken ct)
    {
        var mode = manual ? "manual" : "scheduled";
        var month = YearMonth.From(through);
        var monthStart = new DateOnly(month.Year, month.Month, 1);

        // Areas by name — labs carry their area as a name string. Ordered so a duplicated name resolves deterministically.
        var areas = await _db.Areas.ToListAsync(ct);
        var areaByName = areas.OrderBy(a => a.Name, StringComparer.Ordinal)
            .GroupBy(a => a.Name, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        // ---- 1. Penalty reconcile ---------------------------------------------------------------------------------
        var mirrored = await _db.Deductions.AsNoTracking().Where(d => d.PenaltyRecordId != null).Select(d => d.PenaltyRecordId!.Value).ToListAsync(ct);
        var mirroredSet = mirrored.ToHashSet();
        var unlinked = await _db.PenaltyRecords.AsNoTracking().Where(p => !mirrored.Contains(p.Id)).ToListAsync(ct);
        var linked = 0; var unplaced = 0;
        if (unlinked.Count > 0)
        {
            var labIds = unlinked.Select(p => p.LaboratoryId).Distinct().ToList();
            var labs = await _db.Laboratories.AsNoTracking().Where(l => labIds.Contains(l.Id))
                .Select(l => new { l.Id, l.Name, l.Area }).ToDictionaryAsync(l => l.Id, ct);
            foreach (var p in unlinked)
            {
                if (mirroredSet.Contains(p.Id)) continue;
                if (!labs.TryGetValue(p.LaboratoryId, out var lab) || string.IsNullOrWhiteSpace(lab.Area) || !areaByName.TryGetValue(lab.Area, out var area))
                { unplaced++; continue; }
                _db.Deductions.Add(Deduction.FromPenalty(area.Id, p, lab.Name));
                mirroredSet.Add(p.Id);
                linked++;
            }
        }

        // ---- 2. Percentage Deal for the month ---------------------------------------------------------------------
        var dealAreas = areas.Where(a => a.PercentageDeal && a.Percentage is not null).ToList();
        var created = 0; var recalculated = 0; var skipped = 0;
        if (dealAreas.Count > 0)
        {
            var dealNames = dealAreas.Select(a => a.Name).ToHashSet(StringComparer.Ordinal);
            // Lab → area name for every lab in a deal area, and the month's income per lab code (both in one query each).
            var labRows = await _db.Laboratories.AsNoTracking().Where(l => l.Area != null && dealNames.Contains(l.Area!))
                .Select(l => new { l.Code, l.Area }).ToListAsync(ct);
            var codeToArea = labRows.GroupBy(l => l.Code.Value.ToUpperInvariant())
                .ToDictionary(g => g.Key, g => g.First().Area!, StringComparer.OrdinalIgnoreCase);
            var stats = await _db.DailyLabStatistics.AsNoTracking()
                .Where(s => s.Date >= monthStart && s.Date <= through).ToListAsync(ct);
            var incomeByArea = new Dictionary<string, decimal>(StringComparer.Ordinal);
            var labCountByArea = labRows.GroupBy(l => l.Area!).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
            foreach (var s in stats)
                if (codeToArea.TryGetValue(s.LabCode, out var areaName))
                    incomeByArea[areaName] = incomeByArea.GetValueOrDefault(areaName) + s.Income.Amount;

            var existingRows = await _db.Deductions
                .Where(d => d.Origin == DeductionOrigin.AutoDeal && d.PeriodFrom == monthStart).ToListAsync(ct);
            var existingByArea = existingRows.ToDictionary(d => d.AreaId);

            foreach (var area in dealAreas)
            {
                var income = incomeByArea.GetValueOrDefault(area.Name);
                var value = decimal.Round(income * area.Percentage!.Value / 100m, 2, MidpointRounding.ToEven);
                var basis = $"Auto-calculated: area income {income:N2} ({labCountByArea.GetValueOrDefault(area.Name)} lab(s)) × {area.Percentage.Value:0.##}% for {monthStart:dd/MM/yyyy}–{through:dd/MM/yyyy}";
                if (existingByArea.TryGetValue(area.Id, out var row))
                {
                    if (row.IsAdjusted) { skipped++; continue; }
                    row.Recalculate(through, value, basis);
                    recalculated++;
                }
                else
                {
                    _db.Deductions.Add(Deduction.AutoDeal(area.Id, month, through, value, basis));
                    created++;
                }
            }
        }

        // ---- 3. Collection → treasury reconcile (shared with the Treasury page's "Sync collections") -----------------
        var collections = await CollectionTreasurySyncRunner.ReconcileAsync(_db, ct);
        var collectionsLinked = collections.Linked; var collectionsUnplaced = collections.Unplaced;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Deductions automation ({Mode}) {Month} through {Through:yyyy-MM-dd}: deal areas {DealAreas} (created {Created}, recalculated {Recalculated}, adjusted-skipped {Skipped}); penalties linked {Linked}, unplaced {Unplaced}; collections linked {CLinked}, unplaced {CUnplaced}.",
            mode, month, through, dealAreas.Count, created, recalculated, skipped, linked, unplaced, collectionsLinked, collectionsUnplaced);

        return new DeductionAutomationResult(month.ToString(), through, dealAreas.Count, created, recalculated, skipped, linked, unplaced, collectionsLinked, collectionsUnplaced);
    }
}
