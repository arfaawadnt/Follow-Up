using FollowUp.Application.Common.Abstractions;
using FollowUp.Application.Features.Setup;
using FollowUp.Domain.Common;
using FollowUp.Domain.Reference;
using FollowUp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FollowUp.Infrastructure.Jobs;

/// <summary>
/// Assigns every laboratory to the segment whose monthly target-income band contains the lab's achieved income for
/// a month. Achieved income = Σ(patient_fee + insurance_fee) over that lab's detailed registrations in the month.
/// Segments are ordered by their band upper bound; a lab lands in the first segment whose upper bound is ≥ its
/// income (a null upper bound is the unbounded top tier, e.g. VIP). Labs with no registrations count as zero income
/// and fall into the lowest tier. Overwrites the segment on every lab each run (income is the source of truth).
/// </summary>
internal sealed class SegmentAssignmentRunner : ISegmentAssignmentRunner
{
    private readonly FollowUpDbContext _db;
    private readonly ILogger<SegmentAssignmentRunner> _logger;

    public SegmentAssignmentRunner(FollowUpDbContext db, ILogger<SegmentAssignmentRunner> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<SegmentAssignmentResult> RunAsync(YearMonth month, bool manual, CancellationToken ct)
    {
        var mode = manual ? "manual" : "scheduled";
        var monthStart = new DateOnly(month.Year, month.Month, 1);
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);

        // 1. Segment bands: Segment ref items that carry a configured income band, ordered by upper bound ascending
        //    (a null upper bound sorts last — the unbounded top tier).
        var segments = await _db.RefItems.AsNoTracking()
            .Where(r => r.Type == RefType.Segment && (r.TargetIncomeFrom != null || r.TargetIncomeTo != null))
            .ToListAsync(ct);
        if (segments.Count == 0)
        {
            _logger.LogWarning("Segment assignment ({Mode}) {Month}: skipped — no segment income bands configured.", mode, month);
            return new SegmentAssignmentResult(false, "no-bands", month.ToString(), 0, 0, new Dictionary<string, int>());
        }
        var bands = segments.Select(s => (s.Code, UpperBound: s.TargetIncomeTo)).ToList();

        // 2. Achieved income per lab code for the month (combined cash + insurance fees).
        var incomeRows = await _db.DetailedRegistrations.AsNoTracking()
            .Where(s => s.Date >= monthStart && s.Date <= monthEnd && s.LabCode != null)
            .GroupBy(s => s.LabCode!)
            .Select(g => new { Code = g.Key, Income = g.Sum(x => x.PatientFee + x.InsuranceFee) })
            .ToListAsync(ct);
        var income = incomeRows.ToDictionary(x => x.Code!, x => x.Income, StringComparer.Ordinal);

        // 3. Assign every lab. LabCode and Laboratory.Code are both upper-cased, so the dictionary key matches.
        var labs = await _db.Laboratories.ToListAsync(ct);
        var reassigned = 0;
        var perSegment = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var lab in labs)
        {
            var amount = income.TryGetValue(lab.Code.Value, out var v) ? v : 0m;
            var target = SegmentBands.Match(bands, amount);
            if (target is null) continue;   // no upper-bound tier covers this income (misconfigured bands)
            perSegment[target] = perSegment.GetValueOrDefault(target) + 1;
            if (!string.Equals(lab.Segment, target, StringComparison.Ordinal))
            {
                lab.ReassignSegment(target);
                reassigned++;
            }
        }
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Segment assignment ({Mode}) {Month} [{From:yyyy-MM-dd}..{To:yyyy-MM-dd}]: {Evaluated} labs, {Reassigned} reassigned. Distribution: {Dist}",
            mode, month, monthStart, monthEnd, labs.Count, reassigned,
            string.Join(", ", perSegment.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}={kv.Value}")));

        return new SegmentAssignmentResult(true, "ok", month.ToString(), labs.Count, reassigned, perSegment);
    }
}
