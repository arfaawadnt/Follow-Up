using FollowUp.Application.Common.Exceptions;
using FollowUp.Application.Features.Accounting;
using FollowUp.Domain.Accounting;
using FollowUp.Domain.Identity;
using FollowUp.Domain.Laboratories;
using FollowUp.Domain.Reference;
using FollowUp.Domain.Representatives;
using Microsoft.EntityFrameworkCore;

namespace FollowUp.Infrastructure.Persistence.Queries;

/// <summary>
/// Read side of the Accounting module. Org scope is pushed into SQL wherever the row hangs off a lab (penalties,
/// collections) via <see cref="ScopeFilter.ApplyScope(IQueryable{Laboratory}, OrgScope)"/>; deductions are scoped on the
/// Areas dimension, treasuries on Branches, and the rep statement on the rep's own geography. Money is a converted
/// scalar, so rows are materialized before any <c>.Amount</c> arithmetic (same rule as the stats queries).
/// </summary>
internal sealed class AccountingQueries : IAccountingQueries
{
    private readonly FollowUpDbContext _db;
    public AccountingQueries(FollowUpDbContext db) => _db = db;

    // ---- Treasury ----

    public async Task<IReadOnlyList<TreasuryReasonDto>> TreasuryReasonsAsync(CancellationToken ct) =>
        await _db.TreasuryReasons.AsNoTracking().OrderBy(r => r.Name)
            .Select(r => new TreasuryReasonDto(r.Id.Value, r.Name, r.IsActive)).ToListAsync(ct);

    public async Task<IReadOnlyList<TreasuryDto>> TreasuriesAsync(OrgScope scope, CancellationToken ct)
    {
        var all = await _db.Treasuries.AsNoTracking().OrderBy(t => t.Name).ToListAsync(ct);
        return all.Where(t => TreasuryScope.IsVisible(scope, t.Branches))
            .Select(t => new TreasuryDto(t.Id.Value, t.Name, t.Branches.ToList(), t.IsActive)).ToList();
    }

    public async Task<IReadOnlyList<TreasuryEntryDto>> TreasuryEntriesAsync(DateOnly from, DateOnly to, Guid? treasuryId, OrgScope scope, CancellationToken ct)
    {
        var treasuries = (await _db.Treasuries.AsNoTracking().ToListAsync(ct))
            .Where(t => TreasuryScope.IsVisible(scope, t.Branches)).ToDictionary(t => t.Id);
        if (treasuryId is { } tid && !treasuries.ContainsKey(new TreasuryId(tid))) return Array.Empty<TreasuryEntryDto>();
        var visibleIds = treasuryId is { } one ? new List<TreasuryId> { new(one) } : treasuries.Keys.ToList();

        var entries = await _db.TreasuryEntries.AsNoTracking()
            .Where(e => visibleIds.Contains(e.TreasuryId) && e.Date >= from && e.Date <= to)
            .OrderByDescending(e => e.Date).ThenByDescending(e => e.Serial).ToListAsync(ct);
        var reasons = await _db.TreasuryReasons.AsNoTracking().ToDictionaryAsync(r => r.Id, r => r.Name, ct);

        return entries.Select(e => new TreasuryEntryDto(e.Id.Value, e.Serial, e.Date, e.TreasuryId.Value,
            treasuries.TryGetValue(e.TreasuryId, out var t) ? t.Name : "—",
            e.Debit.Amount, e.Credit.Amount, e.ReasonId.Value, reasons.GetValueOrDefault(e.ReasonId, "—"), e.Notes)).ToList();
    }

    // ---- Penalty statement ----

    public async Task<IReadOnlyList<PenaltyDto>> PenaltiesAsync(DateOnly from, DateOnly to, Guid? laboratoryId, OrgScope scope, bool canSeeEncrypted, CancellationToken ct)
    {
        var scopedLabs = _db.Laboratories.ApplyScope(scope).Select(l => l.Id);
        var q = _db.PenaltyRecords.AsNoTracking().Where(p => scopedLabs.Contains(p.LaboratoryId) && p.Date >= from && p.Date <= to);
        if (laboratoryId is { } lid) q = q.Where(p => p.LaboratoryId == new LaboratoryId(lid));
        var rows = await q.OrderByDescending(p => p.Date).ThenByDescending(p => p.Serial).ToListAsync(ct);
        var labs = await LabRowsAsync(rows.Select(p => p.LaboratoryId), ct);

        // Resolve the "performed by" names in two set-based lookups (reps and system users) rather than per row.
        var repIds = rows.Where(p => p.PerformedByRepId is not null).Select(p => p.PerformedByRepId!.Value).Distinct().ToList();
        var userIds = rows.Where(p => p.PerformedByUserId is not null).Select(p => p.PerformedByUserId!.Value).Distinct().ToList();
        var repNames = repIds.Count == 0 ? new Dictionary<RepresentativeId, string>()
            : await _db.Representatives.AsNoTracking().Where(r => repIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, r => r.FullName, ct);
        var userNames = userIds.Count == 0 ? new Dictionary<AppUserId, string>()
            : await _db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Username, ct);

        return rows.Select(p =>
        {
            labs.TryGetValue(p.LaboratoryId, out var lab);
            string? performedBy = p.PerformedByRepId is { } rid ? repNames.GetValueOrDefault(rid)
                : p.PerformedByUserId is { } uid ? userNames.GetValueOrDefault(uid) : null;
            return new PenaltyDto(p.Id.Value, p.Serial, p.Date, p.LaboratoryId.Value,
                lab is null ? "—" : DisplayCode.For(lab.Code.Value, lab.IsEncrypted, canSeeEncrypted), lab?.Name ?? "—",
                p.AccNo, p.PatientName, p.WrongTestCode, p.WrongTestName, p.WrongValue.Amount,
                p.RightTestCode, p.RightTestName, p.RightValue.Amount, p.PenaltyAmount.Amount,
                p.UserType.Name, p.PerformedByRepId?.Value ?? p.PerformedByUserId?.Value, performedBy);
        }).ToList();
    }

    public async Task<IReadOnlyList<PenaltyActorDto>> PenaltyActorsAsync(PenaltyUser userType, OrgScope scope, CancellationToken ct)
    {
        if (userType == PenaltyUser.Rep)
        {
            // Reps are org-scoped like every rep-linked read; the rep type is carried so the picker can disambiguate.
            var reps = await _db.Representatives.ApplyScope(scope).AsNoTracking().Where(r => r.IsActive)
                .Select(r => new { r.Id, r.FullName, r.Type }).ToListAsync(ct);
            return reps.OrderBy(r => r.FullName, StringComparer.OrdinalIgnoreCase)
                .Select(r => new PenaltyActorDto(r.Id.Value, r.FullName, r.Type.Name)).ToList();
        }
        var users = await _db.Users.AsNoTracking().Where(u => u.IsActive).Select(u => new { u.Id, u.Username }).ToListAsync(ct);
        return users.OrderBy(u => u.Username, StringComparer.OrdinalIgnoreCase)
            .Select(u => new PenaltyActorDto(u.Id.Value, u.Username, null)).ToList();
    }

    // ---- Deductions ----

    public async Task<IReadOnlyList<DeductionDto>> DeductionsAsync(DateOnly from, DateOnly to, Guid? areaId, OrgScope scope, CancellationToken ct)
    {
        var areas = await VisibleAreasAsync(scope, ct);
        if (areaId is { } aid && !areas.ContainsKey(new AreaId(aid))) return Array.Empty<DeductionDto>();
        var ids = areaId is { } one ? new List<AreaId> { new(one) } : areas.Keys.ToList();

        var rows = await _db.Deductions.AsNoTracking()
            .Where(d => ids.Contains(d.AreaId) && d.Date >= from && d.Date <= to)
            .OrderByDescending(d => d.Date).ThenByDescending(d => d.Serial).ToListAsync(ct);

        // The mirrored penalties' serials, for "Penalty #N" on the report (one set-based lookup).
        var penaltyIds = rows.Where(d => d.PenaltyRecordId is not null).Select(d => d.PenaltyRecordId!.Value).Distinct().ToList();
        var serials = penaltyIds.Count == 0 ? new Dictionary<PenaltyRecordId, long>()
            : await _db.PenaltyRecords.AsNoTracking().Where(p => penaltyIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Serial, ct);

        return rows.Select(d => new DeductionDto(d.Id.Value, d.Serial, d.Date, d.AreaId.Value,
            areas.TryGetValue(d.AreaId, out var a) ? a : "—", d.Reason.Name, d.Value.Amount, d.Notes, d.PeriodFrom, d.PeriodTo,
            d.Origin.Name, d.IsAdjusted, d.SystemNote, d.PenaltyRecordId?.Value,
            d.PenaltyRecordId is { } pid && serials.TryGetValue(pid, out var s) ? s : null)).ToList();
    }

    public async Task<DeductionSuggestionDto> SuggestDeductionAsync(Guid areaId, DeductionReason reason, DateOnly from, DateOnly to, OrgScope scope, CancellationToken ct)
    {
        var area = await _db.Areas.AsNoTracking().FirstOrDefaultAsync(a => a.Id == new AreaId(areaId), ct) ?? throw new NotFoundException("Area", areaId);
        if (!(scope.Areas.Contains(OrgScope.Wildcard) || scope.Areas.Contains(area.Name))) throw new NotFoundException("Area", areaId);

        // The area's labs (labs carry the area by name), narrowed to the caller's scope.
        var labs = await _db.Laboratories.ApplyScope(scope).AsNoTracking()
            .Where(l => l.Area == area.Name).Select(l => new { l.Id, l.Code }).ToListAsync(ct);

        if (reason == DeductionReason.Penalty)
        {
            var labIds = labs.Select(l => l.Id).ToList();
            var penalties = await _db.PenaltyRecords.AsNoTracking()
                .Where(p => labIds.Contains(p.LaboratoryId) && p.Date >= from && p.Date <= to).ToListAsync(ct);
            var total = penalties.Aggregate(0m, (acc, p) => acc + p.PenaltyAmount.Amount);
            return new DeductionSuggestionDto(decimal.Round(total, 2, MidpointRounding.ToEven),
                $"{penalties.Count} penalty row(s) for {labs.Count} lab(s) in {area.Name}, {from:dd/MM/yyyy}–{to:dd/MM/yyyy} (Σ wrong − right)");
        }

        if (reason == DeductionReason.PercentageDeal)
        {
            if (!area.PercentageDeal || area.Percentage is null)
                throw new ValidationException(new Dictionary<string, string[]> { ["areaId"] = new[] { $"Area '{area.Name}' has no active Percentage Deal." } });
            var codes = labs.Select(l => l.Code.Value.ToUpperInvariant()).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var stats = await _db.DailyLabStatistics.AsNoTracking().Where(s => s.Date >= from && s.Date <= to).ToListAsync(ct);
            var income = stats.Where(s => codes.Contains(s.LabCode)).Aggregate(0m, (acc, s) => acc + s.Income.Amount);
            var value = decimal.Round(income * area.Percentage.Value / 100m, 2, MidpointRounding.ToEven);
            return new DeductionSuggestionDto(value,
                $"Area income {income:N2} × {area.Percentage.Value:0.##}% ({labs.Count} lab(s), {from:dd/MM/yyyy}–{to:dd/MM/yyyy})");
        }

        return new DeductionSuggestionDto(0m, "Transportation is typed manually.");
    }

    // ---- Collections ----

    public async Task<IReadOnlyList<CollectionDto>> CollectionsAsync(DateOnly from, DateOnly to, Guid? laboratoryId, Guid? repId, OrgScope scope, bool canSeeEncrypted, CancellationToken ct)
    {
        var scopedLabs = _db.Laboratories.ApplyScope(scope).Select(l => l.Id);
        var q = _db.Collections.AsNoTracking().Where(c => scopedLabs.Contains(c.LaboratoryId) && c.Date >= from && c.Date <= to);
        if (laboratoryId is { } lid) q = q.Where(c => c.LaboratoryId == new LaboratoryId(lid));
        var rows = await q.OrderByDescending(c => c.Date).ThenByDescending(c => c.Serial).ToListAsync(ct);
        if (repId is { } rid) rows = rows.Where(c => c.RepIds.Contains(new RepresentativeId(rid))).ToList(); // jsonb list → in memory

        var labs = await LabRowsAsync(rows.Select(c => c.LaboratoryId), ct);
        var repIds = rows.SelectMany(c => c.RepIds).Distinct().ToList();
        var repNames = await _db.Representatives.AsNoTracking().Where(r => repIds.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, r => r.FullName, ct);

        return rows.Select(c =>
        {
            labs.TryGetValue(c.LaboratoryId, out var lab);
            return new CollectionDto(c.Id.Value, c.Serial, c.Date, c.LaboratoryId.Value,
                lab is null ? "—" : DisplayCode.For(lab.Code.Value, lab.IsEncrypted, canSeeEncrypted), lab?.Name ?? "—",
                c.Type.Name, c.RepIds.Select(r => r.Value).ToList(), c.RepIds.Select(r => repNames.GetValueOrDefault(r, "—")).ToList(),
                c.Cash.Amount, c.Bank.Amount, c.Total.Amount, c.Iban?.Name, c.DoneBy, c.Notes);
        }).ToList();
    }

    // ---- Rep statement ----

    public async Task<RepStatementDto?> RepStatementAsync(Guid repId, DateOnly from, DateOnly to, OrgScope scope, CancellationToken ct)
    {
        var rid = new RepresentativeId(repId);
        // The rep must be visible in the caller's scope (geographic dims) — otherwise "not found", never a leak.
        var rep = await _db.Representatives.ApplyScope(scope).AsNoTracking().FirstOrDefaultAsync(r => r.Id == rid, ct);
        if (rep is null) return null;

        // Debit 1 — Oracle-derived income: Σ synced daily income of the labs this rep is the assigned collector for
        // (operator decision). CollectorRepIds is a jsonb list, so the membership test runs in memory.
        var labCodes = (await _db.Laboratories.AsNoTracking().Select(l => new { l.Code, l.CollectorRepIds }).ToListAsync(ct))
            .Where(l => l.CollectorRepIds.Contains(rid)).Select(l => l.Code.Value.ToUpperInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var stats = await _db.DailyLabStatistics.AsNoTracking().Where(s => s.Date >= from && s.Date <= to).ToListAsync(ct);
        var oracleByDay = stats.Where(s => codesContains(s.LabCode)).GroupBy(s => s.Date)
            .ToDictionary(g => g.Key, g => g.Aggregate(0m, (acc, s) => acc + s.Income.Amount));
        bool codesContains(string code) => labCodes.Contains(code);

        // Debit 2 — manually recorded real income.
        var manual = await _db.RepIncomeEntries.AsNoTracking()
            .Where(e => e.RepresentativeId == rid && e.Date >= from && e.Date <= to).ToListAsync(ct);

        // Credit — collections this rep took part in (jsonb list → in memory).
        var collections = (await _db.Collections.AsNoTracking().Where(c => c.Date >= from && c.Date <= to).ToListAsync(ct))
            .Where(c => c.RepIds.Contains(rid)).ToList();

        var lines = new List<(DateOnly Date, int Order, string Kind, decimal Debit, decimal Credit, string? Notes, Guid? SourceId)>();
        foreach (var kv in oracleByDay.Where(kv => kv.Value != 0m))
            lines.Add((kv.Key, 0, "OracleIncome", kv.Value, 0m, "Synced income of the rep's collector labs", null));
        foreach (var e in manual)
            lines.Add((e.Date, 1, "ManualIncome", e.Amount.Amount, 0m, e.Notes, e.Id.Value));
        foreach (var c in collections)
            lines.Add((c.Date, 2, "Collection", 0m, c.Total.Amount, c.Notes ?? (c.Bank.Amount > 0 ? $"Bank (IBAN {c.Iban?.Name})" : "Cash"), c.Id.Value));

        var rows = new List<RepStatementRowDto>();
        decimal balance = 0m, totalDebit = 0m, totalCredit = 0m;
        foreach (var l in lines.OrderBy(l => l.Date).ThenBy(l => l.Order))
        {
            balance += l.Debit - l.Credit; totalDebit += l.Debit; totalCredit += l.Credit;
            rows.Add(new RepStatementRowDto(l.Date, l.Kind, l.Debit, l.Credit, l.Notes, balance, l.SourceId));
        }
        return new RepStatementDto(repId, rep.FullName, rows, totalDebit, totalCredit, balance);
    }

    // ---- helpers ----

    private async Task<Dictionary<LaboratoryId, LabRow>> LabRowsAsync(IEnumerable<LaboratoryId> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        if (list.Count == 0) return new Dictionary<LaboratoryId, LabRow>();
        return (await _db.Laboratories.AsNoTracking().Where(l => list.Contains(l.Id))
            .Select(l => new LabRow(l.Id, l.Code, l.IsEncrypted, l.Name)).ToListAsync(ct)).ToDictionary(l => l.Id);
    }

    /// <summary>Areas visible under the scope's Areas dimension (wildcard → all), as id → name.</summary>
    private async Task<Dictionary<AreaId, string>> VisibleAreasAsync(OrgScope scope, CancellationToken ct)
    {
        var all = await _db.Areas.AsNoTracking().Select(a => new { a.Id, a.Name }).ToListAsync(ct);
        var visible = scope.Areas.Contains(OrgScope.Wildcard) ? all : all.Where(a => scope.Areas.Contains(a.Name)).ToList();
        return visible.ToDictionary(a => a.Id, a => a.Name);
    }

    private sealed record LabRow(LaboratoryId Id, LabCode Code, bool IsEncrypted, string Name);
}
