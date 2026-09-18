using FollowUp.Application.Common.Exceptions;
using FollowUp.Application.Common.Security;
using FollowUp.Application.Features.Accounting;
using FollowUp.Domain.Accounting;
using FollowUp.Domain.Identity;
using FollowUp.Domain.Laboratories;
using FollowUp.Domain.Operations;
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

    public async Task<IReadOnlyList<TreasuryDto>> TreasuriesAsync(OrgScope scope, TreasuryAccessMap access, CancellationToken ct)
    {
        // Visible = within the caller's Branches scope AND granted View (administrators see everything in scope).
        var all = await _db.Treasuries.AsNoTracking().OrderBy(t => t.Name).ToListAsync(ct);
        return all.Where(t => TreasuryScope.IsVisible(scope, t.Branches) && access.CanView(t.Id))
            .Select(t => new TreasuryDto(t.Id.Value, t.Name, t.Branches.ToList(), t.IsActive, access.CanValidate(t.Id), access.CanUpdate(t.Id))).ToList();
    }

    public async Task<IReadOnlyList<TreasuryEntryDto>> TreasuryEntriesAsync(DateOnly from, DateOnly to, Guid? treasuryId, OrgScope scope, TreasuryAccessMap access, CancellationToken ct)
    {
        var treasuries = (await _db.Treasuries.AsNoTracking().ToListAsync(ct))
            .Where(t => TreasuryScope.IsVisible(scope, t.Branches) && access.CanView(t.Id)).ToDictionary(t => t.Id);
        if (treasuryId is { } tid && !treasuries.ContainsKey(new TreasuryId(tid))) return Array.Empty<TreasuryEntryDto>();
        var visibleIds = treasuryId is { } one ? new List<TreasuryId> { new(one) } : treasuries.Keys.ToList();

        var entries = await _db.TreasuryEntries.AsNoTracking()
            .Where(e => visibleIds.Contains(e.TreasuryId) && e.Date >= from && e.Date <= to)
            .OrderByDescending(e => e.Date).ThenByDescending(e => e.Serial).ToListAsync(ct);
        var reasons = await _db.TreasuryReasons.AsNoTracking().ToDictionaryAsync(r => r.Id, r => r.Name, ct);

        return entries.Select(e => new TreasuryEntryDto(e.Id.Value, e.Serial, e.Date, e.TreasuryId.Value,
            treasuries.TryGetValue(e.TreasuryId, out var t) ? t.Name : "—",
            e.Debit.Amount, e.Credit.Amount, e.ReasonId?.Value,
            e.ReasonId is { } rid ? reasons.GetValueOrDefault(rid, "—") : "Collection", e.Notes,
            e.Origin.Name, e.ValidationStatus.Name, e.CollectionId?.Value, e.CollectedCash?.Amount, e.SystemNote,
            e.ValidatedAt, e.ValidatedBy, e.ValidationNote)).ToList();
    }

    public async Task<IReadOnlyList<TreasuryGrantDto>> TreasuryGrantsAsync(RoleId roleId, CancellationToken ct)
    {
        var treasuries = await _db.Treasuries.AsNoTracking().OrderBy(t => t.Name).ToListAsync(ct);
        var grants = await _db.TreasuryGrants.AsNoTracking().Where(g => g.RoleId == roleId).ToDictionaryAsync(g => g.TreasuryId, ct);
        return treasuries.Select(t =>
        {
            grants.TryGetValue(t.Id, out var g);
            return new TreasuryGrantDto(t.Id.Value, t.Name, t.IsActive, g?.CanView ?? false, g?.CanValidate ?? false, g?.CanUpdate ?? false);
        }).ToList();
    }

    // ---- Penalty statement ----

    public async Task<IReadOnlyList<PenaltyDto>> PenaltiesAsync(DateOnly from, DateOnly to, Guid? laboratoryId, Guid? areaId, OrgScope scope, bool canSeeEncrypted, CancellationToken ct)
    {
        var scopedLabsQ = _db.Laboratories.ApplyScope(scope);
        if (areaId is { } aid)
        {
            // Labs carry their area by name.
            var areaName = await _db.Areas.AsNoTracking().Where(a => a.Id == new AreaId(aid)).Select(a => a.Name).FirstOrDefaultAsync(ct);
            if (areaName is null) return Array.Empty<PenaltyDto>();
            scopedLabsQ = scopedLabsQ.Where(l => l.Area == areaName);
        }
        var scopedLabs = scopedLabsQ.Select(l => l.Id);
        var q = _db.PenaltyRecords.AsNoTracking().Where(p => scopedLabs.Contains(p.LaboratoryId) && p.Date >= from && p.Date <= to);
        if (laboratoryId is { } lid) q = q.Where(p => p.LaboratoryId == new LaboratoryId(lid));
        var rows = await q.OrderByDescending(p => p.Date).ThenByDescending(p => p.Serial).ToListAsync(ct);
        if (rows.Count == 0) return Array.Empty<PenaltyDto>();

        var labIds = rows.Select(p => p.LaboratoryId).Distinct().ToList();
        var labs = await _db.Laboratories.AsNoTracking().Where(l => labIds.Contains(l.Id))
            .Select(l => new { l.Id, l.Code, l.IsEncrypted, l.Name, l.ResponsibleRepId }).ToDictionaryAsync(l => l.Id, ct);

        // Resolve the "performed by" and Lab Responsible names in two set-based lookups (reps and system users) rather than per row.
        var repIds = rows.Where(p => p.PerformedByRepId is not null).Select(p => p.PerformedByRepId!.Value)
            .Concat(labs.Values.Where(l => l.ResponsibleRepId is not null).Select(l => l.ResponsibleRepId!.Value)).Distinct().ToList();
        var userIds = rows.Where(p => p.PerformedByUserId is not null).Select(p => p.PerformedByUserId!.Value).Distinct().ToList();
        var repNames = repIds.Count == 0 ? new Dictionary<RepresentativeId, string>()
            : await _db.Representatives.AsNoTracking().Where(r => repIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, r => r.FullName, ct);
        var userNames = userIds.Count == 0 ? new Dictionary<AppUserId, string>()
            : await _db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Username, ct);

        // LDM validation against the synced registration lines (detailed_registration mirrors Oracle reg/reg_lines): the
        // Acc No must exist, belong to the penalised lab, and carry the tests named on the record.
        var accNos = rows.Select(p => p.AccNo.Trim()).Distinct().ToList();
        var regLines = await _db.DetailedRegistrations.AsNoTracking().Where(r => accNos.Contains(r.AccNo))
            .Select(r => new { r.AccNo, r.LabCode, r.TestCode }).ToListAsync(ct);
        var byAcc = regLines.GroupBy(r => r.AccNo.Trim(), StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        return rows.Select(p =>
        {
            labs.TryGetValue(p.LaboratoryId, out var lab);
            string? performedBy = p.PerformedByRepId is { } rid ? repNames.GetValueOrDefault(rid)
                : p.PerformedByUserId is { } uid ? userNames.GetValueOrDefault(uid) : null;
            var responsibleId = lab?.ResponsibleRepId;
            var labCode = lab?.Code.Value.ToUpperInvariant();
            string status; string? note;
            if (!byAcc.TryGetValue(p.AccNo.Trim(), out var lines))
            { status = "AccNotFound"; note = "Acc No not found in the synced LDM registrations"; }
            else
            {
                var regLabs = lines.Select(l => l.LabCode).Where(c => !string.IsNullOrEmpty(c)).Select(c => c!.ToUpperInvariant()).Distinct().ToList();
                if (labCode is not null && !regLabs.Contains(labCode))
                { status = "LabMismatch"; note = regLabs.Count == 0 ? "Acc No is registered without a lab in LDM" : $"Acc No belongs to lab {string.Join(", ", regLabs)} in LDM, not to this lab"; }
                else
                {
                    var tests = lines.Select(l => l.TestCode.ToUpperInvariant()).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    var missing = new[] { p.WrongTestCode, p.RightTestCode }.Where(c => !string.IsNullOrWhiteSpace(c) && !tests.Contains(c!.Trim().ToUpperInvariant())).Select(c => c!.Trim()).ToList();
                    if (missing.Count > 0) { status = "TestMissing"; note = $"Test(s) {string.Join(", ", missing)} not on this registration in LDM"; }
                    else { status = "Valid"; note = null; }
                }
            }
            return new PenaltyDto(p.Id.Value, p.Serial, p.Date, p.LaboratoryId.Value,
                lab is null ? "—" : DisplayCode.For(lab.Code.Value, lab.IsEncrypted, canSeeEncrypted), lab?.Name ?? "—",
                p.AccNo, p.PatientName, p.WrongTestCode, p.WrongTestName, p.WrongValue.Amount,
                p.RightTestCode, p.RightTestName, p.RightValue.Amount, p.PenaltyAmount.Amount,
                p.UserType.Name, p.PerformedByRepId?.Value ?? p.PerformedByUserId?.Value, performedBy,
                responsibleId?.Value, responsibleId is { } rr ? repNames.GetValueOrDefault(rr) : null, status, note);
        }).ToList();
    }

    public async Task<IReadOnlyList<PenaltyActorDto>> PenaltyActorsAsync(PenaltyUser userType, OrgScope scope, CancellationToken ct)
    {
        if (userType == PenaltyUser.LabRequest) return Array.Empty<PenaltyActorDto>(); // the lab asked for the wrong test — nobody to pick
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

        return rows.Select(d => new DeductionDto(d.Id.Value, d.Serial, d.Date, d.AreaId.Value,
            areas.TryGetValue(d.AreaId, out var a) ? a : "—", d.Reason.Name, d.Value.Amount, d.Notes, d.PeriodFrom, d.PeriodTo,
            d.Origin.Name, d.IsAdjusted, d.SystemNote)).ToList();
    }

    public async Task<DeductionSuggestionDto> SuggestDeductionAsync(Guid areaId, DeductionReason reason, DateOnly from, DateOnly to, OrgScope scope, CancellationToken ct)
    {
        var area = await _db.Areas.AsNoTracking().FirstOrDefaultAsync(a => a.Id == new AreaId(areaId), ct) ?? throw new NotFoundException("Area", areaId);
        if (!(scope.Areas.Contains(OrgScope.Wildcard) || scope.Areas.Contains(area.Name))) throw new NotFoundException("Area", areaId);

        // The area's labs (labs carry the area by name), narrowed to the caller's scope.
        var labs = await _db.Laboratories.ApplyScope(scope).AsNoTracking()
            .Where(l => l.Area == area.Name).Select(l => new { l.Id, l.Code }).ToListAsync(ct);

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

    public async Task<IReadOnlyList<CollectionDto>> CollectionsAsync(DateOnly from, DateOnly to, Guid? repId, OrgScope scope, CancellationToken ct)
    {
        // A collection is its reps' act: it is visible when every rep on it is within the caller's rep scope (fail-closed,
        // matching the write side's EnsureRepsInScope). The shares are a jsonb list, so the membership tests run in memory.
        var rows = await _db.Collections.AsNoTracking().Where(c => c.Date >= from && c.Date <= to)
            .OrderByDescending(c => c.Date).ThenByDescending(c => c.Serial).ToListAsync(ct);
        var scopedReps = (await _db.Representatives.ApplyScope(scope).AsNoTracking().Select(r => r.Id).ToListAsync(ct)).ToHashSet();
        rows = rows.Where(c => c.RepIds.All(scopedReps.Contains)).ToList();
        if (repId is { } rid) rows = rows.Where(c => c.RepIds.Contains(new RepresentativeId(rid))).ToList();

        var repIds = rows.SelectMany(c => c.RepIds).Distinct().ToList();
        var repNames = await _db.Representatives.AsNoTracking().Where(r => repIds.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, r => r.FullName, ct);

        return rows.Select(c => new CollectionDto(c.Id.Value, c.Serial, c.Date, c.Type.Name,
            c.Shares.Select(s => new CollectionShareDto(s.RepId.Value, repNames.GetValueOrDefault(s.RepId, "—"), s.Amount.Amount)).ToList(),
            c.RepIds.Select(r => r.Value).ToList(), c.RepIds.Select(r => repNames.GetValueOrDefault(r, "—")).ToList(),
            c.Cash.Amount, c.Bank.Amount, c.Total.Amount, c.Iban?.Name, c.DoneBy, c.Notes)).ToList();
    }

    // ---- Rep statement ----

    public async Task<RepStatementDto?> RepStatementAsync(Guid repId, DateOnly from, DateOnly to, OrgScope scope, CancellationToken ct)
    {
        var st = await StatementAsync(StatementBy.Responsible, repId, from, to, scope, ct);
        return st is null ? null : new RepStatementDto(st.SubjectId, st.SubjectName, st.Rows, st.TotalDebit, st.TotalCredit, st.Balance);
    }

    /// <summary>
    /// Statement by dimension (operator decisions, 2026-09-18). Debit = the total required entered on the Rep Income sheet per
    /// day + the RIGHT test of every penalty (what the lab owes) [+ legacy manual lines, Responsible only]. Credit = the
    /// actual collections (the rep's share, Responsible only), the deductions of the subject's area(s) (Area and
    /// Responsible views) and the WRONG test of every penalty (what was charged in error) — each noted with its record.
    /// A Rep penalty follows the representative who made it; the other types follow the lab's Lab Responsible.
    /// Running balance = Σ debit − Σ credit.
    /// </summary>
    public async Task<StatementDto?> StatementAsync(string by, Guid id, DateOnly from, DateOnly to, OrgScope scope, CancellationToken ct)
    {
        string subjectName; RepresentativeId? rid = null; List<LaboratoryId> labIds; HashSet<string> labCodes;
        switch (by)
        {
            case StatementBy.Responsible:
                {
                    var r = new RepresentativeId(id);
                    var rep = await _db.Representatives.ApplyScope(scope).AsNoTracking().FirstOrDefaultAsync(x => x.Id == r, ct);
                    if (rep is null) return null;
                    rid = r; subjectName = rep.FullName;
                    // CollectorRepIds is a jsonb list, so the membership test runs in memory.
                    var labs = (await _db.Laboratories.ApplyScope(scope).AsNoTracking().Select(l => new { l.Id, l.Code, l.CollectorRepIds, l.ResponsibleRepId }).ToListAsync(ct))
                        .Where(l => l.ResponsibleRepId == r || l.CollectorRepIds.Contains(r)).ToList();
                    labIds = labs.Select(l => l.Id).ToList(); labCodes = labs.Select(l => l.Code.Value.ToUpperInvariant()).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    break;
                }
            case StatementBy.Area:
                {
                    var areas = await VisibleAreasAsync(scope, ct);
                    if (!areas.TryGetValue(new AreaId(id), out var areaName)) return null;
                    subjectName = areaName;
                    var labs = await _db.Laboratories.ApplyScope(scope).AsNoTracking().Where(l => l.Area == areaName).Select(l => new { l.Id, l.Code }).ToListAsync(ct);
                    labIds = labs.Select(l => l.Id).ToList(); labCodes = labs.Select(l => l.Code.Value.ToUpperInvariant()).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    break;
                }
            case StatementBy.Lab:
                {
                    var lab = await _db.Laboratories.ApplyScope(scope).AsNoTracking().Where(l => l.Id == new LaboratoryId(id)).Select(l => new { l.Id, l.Code, l.Name }).FirstOrDefaultAsync(ct);
                    if (lab is null) return null;
                    subjectName = lab.Name; labIds = new List<LaboratoryId> { lab.Id }; labCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { lab.Code.Value.ToUpperInvariant() };
                    break;
                }
            default: return null;
        }

        var lines = new List<(DateOnly Date, int Order, string Kind, decimal Debit, decimal Credit, string? Notes, Guid? SourceId)>();

        // Debit — the total required recorded on the Rep Income sheet (what the labs owe for the day), one line per day.
        var sheetQuery = _db.RepLabIncomes.AsNoTracking().Where(e => e.Date >= from && e.Date <= to);
        sheetQuery = rid is { } rr ? sheetQuery.Where(e => e.RepresentativeId == rr) : sheetQuery.Where(e => labIds.Contains(e.LaboratoryId));
        foreach (var g in (await sheetQuery.ToListAsync(ct)).GroupBy(e => e.Date))
        {
            var total = g.Sum(e => e.TotalRequired.Amount);
            if (total != 0m) lines.Add((g.Key, 0, "TotalRequired", total, 0m, $"Rep Income sheet · total required of {g.Count()} lab(s)", null));
        }

        // Penalties — the right test is what the lab owes (Debit), the wrong test what was charged in error (Credit), each
        // noted with the record. A Rep penalty follows the representative who made the error; DataEntry / Technician /
        // LabRequest penalties follow the lab's Lab Responsible.
        var penaltyQ = _db.PenaltyRecords.AsNoTracking().Where(p => p.Date >= from && p.Date <= to);
        if (rid is { } prid)
        {
            var responsibleLabs = await _db.Laboratories.AsNoTracking().Where(l => l.ResponsibleRepId == prid).Select(l => l.Id).ToListAsync(ct);
            var repType = PenaltyUser.Rep;
            penaltyQ = penaltyQ.Where(p => (p.UserType == repType && p.PerformedByRepId == prid) || (p.UserType != repType && responsibleLabs.Contains(p.LaboratoryId)));
        }
        else penaltyQ = penaltyQ.Where(p => labIds.Contains(p.LaboratoryId));
        var penalties = await penaltyQ.ToListAsync(ct);
        var penaltyLabs = await LabRowsAsync(penalties.Select(p => p.LaboratoryId), ct);
        foreach (var p in penalties)
        {
            penaltyLabs.TryGetValue(p.LaboratoryId, out var lab);
            var record = $"Penalty #{p.Serial} · {lab?.Name ?? "—"} · Acc {p.AccNo} · {p.PatientName} · {PenaltyUserLabel(p.UserType)}";
            if (p.WrongValue.Amount != 0m) lines.Add((p.Date, 1, "PenaltyWrong", 0m, p.WrongValue.Amount, $"{record} · wrong test {p.WrongTestName} ({p.WrongTestCode})", p.Id.Value));
            if (p.RightValue.Amount != 0m) lines.Add((p.Date, 1, "PenaltyRight", p.RightValue.Amount, 0m, $"{record} · right test {p.RightTestName} ({p.RightTestCode})", p.Id.Value));
        }

        if (rid is { } repId)
        {
            // Debit — legacy manually recorded real income (Responsible only).
            var manual = await _db.RepIncomeEntries.AsNoTracking().Where(e => e.RepresentativeId == repId && e.Date >= from && e.Date <= to).ToListAsync(ct);
            foreach (var e in manual) lines.Add((e.Date, 0, "ManualIncome", e.Amount.Amount, 0m, e.Notes, e.Id.Value));

            // Credit — the actual collections (Collection page): this rep's share, noted with how it was collected.
            var collections = (await _db.Collections.AsNoTracking().Where(c => c.Date >= from && c.Date <= to).ToListAsync(ct)).Where(c => c.RepIds.Contains(repId));
            foreach (var c in collections)
            {
                var how = c.Cash.Amount > 0 && c.Bank.Amount > 0 ? "Cash + Bank" : c.Bank.Amount > 0 ? "Bank" : "Cash";
                var note = $"Collection #{c.Serial} · {how} · {c.Type.Name} · cash {c.Cash.Amount:0.00} · bank {c.Bank.Amount:0.00}"
                    + (c.Iban is not null ? $" (IBAN {c.Iban.Name})" : "") + (c.DoneBy is not null ? $" · by {c.DoneBy}" : "")
                    + (c.RepIds.Count > 1 ? $" · share of {c.Total.Amount:0.00}" : "") + (c.Notes is not null ? $" · {c.Notes}" : "");
                lines.Add((c.Date, 3, "Collection", 0m, c.ShareOf(repId).Amount, note, c.Id.Value));
            }
        }

        // Credit — the recorded deductions (Transportation / Percentage Deal) of the subject's area(s), noted with the record:
        // the area itself on the Area view; the areas of the responsible's labs on the Lab Responsible view; none for a lab.
        var deductionAreas = new List<AreaId>();
        if (by == StatementBy.Area) deductionAreas.Add(new AreaId(id));
        else if (rid is { } drid)
        {
            var areaNames = await _db.Laboratories.AsNoTracking().Where(l => l.ResponsibleRepId == drid && l.Area != null).Select(l => l.Area!).Distinct().ToListAsync(ct);
            if (areaNames.Count > 0) deductionAreas.AddRange(await _db.Areas.AsNoTracking().Where(a => areaNames.Contains(a.Name)).Select(a => a.Id).ToListAsync(ct));
        }
        if (deductionAreas.Count > 0)
        {
            var areaNamesById = await _db.Areas.AsNoTracking().Where(a => deductionAreas.Contains(a.Id)).ToDictionaryAsync(a => a.Id, a => a.Name, ct);
            var deductions = await _db.Deductions.AsNoTracking().Where(d => deductionAreas.Contains(d.AreaId) && d.Date >= from && d.Date <= to).ToListAsync(ct);
            foreach (var d in deductions)
            {
                var period = d.PeriodFrom is { } pf && d.PeriodTo is { } pt ? $" · {pf:dd/MM/yyyy}–{pt:dd/MM/yyyy}" : "";
                var note = $"Deduction #{d.Serial} · {areaNamesById.GetValueOrDefault(d.AreaId, "—")} · {(d.Reason == DeductionReason.PercentageDeal ? "Percentage Deal" : d.Reason.Name)}{period}"
                    + (d.Notes is not null ? $" · {d.Notes}" : "") + (d.SystemNote is not null ? $" · {d.SystemNote}" : "");
                lines.Add((d.Date, 4, "Deduction", 0m, d.Value.Amount, note, d.Id.Value));
            }
        }

        var rows = new List<RepStatementRowDto>();
        decimal balance = 0m, totalDebit = 0m, totalCredit = 0m;
        foreach (var l in lines.OrderBy(l => l.Date).ThenBy(l => l.Order))
        {
            balance += l.Debit - l.Credit; totalDebit += l.Debit; totalCredit += l.Credit;
            rows.Add(new RepStatementRowDto(l.Date, l.Kind, l.Debit, l.Credit, l.Notes, balance, l.SourceId));
        }
        return new StatementDto(by, id, subjectName, rows, totalDebit, totalCredit, balance);
    }

    // ---- helpers ----

    private static string PenaltyUserLabel(PenaltyUser u) =>
        u == PenaltyUser.DataEntry ? "Data Entry" : u == PenaltyUser.LabRequest ? "Lab Request" : u.Name;

    // ---- Real income sheet (Lab Responsible × area × date) ----

    public async Task<IReadOnlyList<RealIncomeRepDto>> RealIncomeRepsAsync(Guid areaId, OrgScope scope, CancellationToken ct)
    {
        var area = await _db.Areas.AsNoTracking().FirstOrDefaultAsync(a => a.Id == new AreaId(areaId), ct);
        if (area is null) return Array.Empty<RealIncomeRepDto>();
        var responsibles = await _db.Laboratories.ApplyScope(scope).AsNoTracking()
            .Where(l => l.Area == area.Name && l.ResponsibleRepId != null).Select(l => l.ResponsibleRepId!.Value).ToListAsync(ct);
        var counts = responsibles.GroupBy(r => r).ToDictionary(g => g.Key, g => g.Count());
        var ids = counts.Keys.ToList();
        var reps = await _db.Representatives.ApplyScope(scope).AsNoTracking().Where(r => ids.Contains(r.Id)).OrderBy(r => r.FullName).ToListAsync(ct);
        return reps.Select(r => new RealIncomeRepDto(r.Id.Value, r.FullName, counts[r.Id])).ToList();
    }

    public async Task<IReadOnlyList<RealIncomeLabDto>> RealIncomeLabsAsync(Guid areaId, OrgScope scope, bool canSeeEncrypted, CancellationToken ct)
    {
        var area = await _db.Areas.AsNoTracking().FirstOrDefaultAsync(a => a.Id == new AreaId(areaId), ct);
        if (area is null) return Array.Empty<RealIncomeLabDto>();
        var labs = await _db.Laboratories.ApplyScope(scope).AsNoTracking().Where(l => l.Area == area.Name).OrderBy(l => l.Name)
            .Select(l => new { l.Id, l.Code, l.IsEncrypted, l.Name }).ToListAsync(ct);
        return labs.Select(l => new RealIncomeLabDto(l.Id.Value, DisplayCode.For(l.Code.Value, l.IsEncrypted, canSeeEncrypted), l.Name)).ToList();
    }

    public async Task<RealIncomeSheetDto?> RealIncomeSheetAsync(Guid areaId, DateOnly date, Guid repId, OrgScope scope, bool canSeeEncrypted, CancellationToken ct)
    {
        var area = await _db.Areas.AsNoTracking().FirstOrDefaultAsync(a => a.Id == new AreaId(areaId), ct);
        if (area is null) return null;
        var rid = new RepresentativeId(repId);
        var rep = await _db.Representatives.ApplyScope(scope).AsNoTracking().FirstOrDefaultAsync(r => r.Id == rid, ct);
        if (rep is null) return null;

        var labs = await _db.Laboratories.ApplyScope(scope).AsNoTracking().Where(l => l.Area == area.Name)
            .Select(l => new { l.Id, l.Code, l.IsEncrypted, l.Name, l.ResponsibleRepId }).ToListAsync(ct);
        var labIds = labs.Select(l => l.Id).ToList();

        // A "record visit" = a check-in with samples on the date (Visited, or Received afterwards): today's board + the archive.
        var visited = VisitStatus.Visited; var received = VisitStatus.Received;
        var live = await _db.DailyVisits.AsNoTracking()
            .Where(v => v.VisitDate == date && labIds.Contains(v.LaboratoryId) && (v.Status == visited || v.Status == received))
            .Select(v => new { v.LaboratoryId, v.TotalRequired, v.SampleCount }).ToListAsync(ct);
        var archived = await _db.VisitHistory.AsNoTracking()
            .Where(h => h.VisitDate == date && labIds.Contains(h.LaboratoryId) && (h.Status == visited.Name || h.Status == received.Name))
            .Select(h => new { h.LaboratoryId, h.TotalRequired, h.SampleCount }).ToListAsync(ct);
        var visits = live.Concat(archived).GroupBy(v => v.LaboratoryId).ToDictionary(g => g.Key, g => (
            Required: g.Any(x => x.TotalRequired != null) ? g.Sum(x => x.TotalRequired ?? 0) : (int?)null,
            Samples: g.Any(x => x.SampleCount != null) ? g.Sum(x => x.SampleCount ?? 0) : (int?)null));

        var entries = (await _db.RepLabIncomes.AsNoTracking().Where(e => e.RepresentativeId == rid && e.Date == date).ToListAsync(ct))
            .ToDictionary(e => e.LaboratoryId);

        // LDM transactions of the rep's labs that day (the synced lab statistics): income + registration count per lab code.
        var repLabCodes = labs.Where(l => l.ResponsibleRepId == rid).Select(l => l.Code.Value.ToUpperInvariant()).ToList();
        var ldmRows = (await _db.DailyLabStatistics.AsNoTracking().Where(s => s.Date == date && repLabCodes.Contains(s.LabCode.ToUpper())).ToListAsync(ct))
            .GroupBy(s => s.LabCode.ToUpperInvariant()).ToDictionary(g => g.Key, g => (Income: g.Sum(s => s.Income.Amount), Registrations: g.Sum(s => s.Registrations)));
        bool HasLdm(string code) => ldmRows.TryGetValue(code, out var x) && (x.Registrations > 0 || x.Income != 0m);
        var ldm = ldmRows.ToDictionary(kv => kv.Key, kv => kv.Value.Income);
        // Rows = the rep's labs with a recorded visit that day ∪ the rep's labs with LDM transactions that day (Oracle sync)
        // ∪ labs the rep already entered for that day.
        var rowLabs = labs.Where(l => (l.ResponsibleRepId == rid && (visits.ContainsKey(l.Id) || HasLdm(l.Code.Value.ToUpperInvariant()))) || entries.ContainsKey(l.Id)).OrderBy(l => l.Name).ToList();
        var rowIds = rowLabs.Select(l => l.Id).ToList();
        // Penalty column = Σ (right − wrong) of every penalty type recorded on the lab that day (2026-09-18).
        var penalties = (await _db.PenaltyRecords.AsNoTracking().Where(p => p.Date == date && rowIds.Contains(p.LaboratoryId)).ToListAsync(ct))
            .GroupBy(p => p.LaboratoryId).ToDictionary(g => g.Key, g => g.Sum(p => p.PenaltyAmount.Amount));
        // Remaining carried from earlier days (any rep): Σ(total required − paid) − Σ delayed payments before the date.
        var previous = (await _db.RepLabIncomes.AsNoTracking().Where(e => e.Date < date && rowIds.Contains(e.LaboratoryId)).ToListAsync(ct))
            .GroupBy(e => e.LaboratoryId).ToDictionary(g => g.Key, g => g.Sum(e => e.TotalRequired.Amount - e.Paid.Amount - e.DelayedPayment.Amount));

        var rows = rowLabs.Select(l =>
        {
            visits.TryGetValue(l.Id, out var v); entries.TryGetValue(l.Id, out var e);
            return new RealIncomeRowDto(l.Id.Value, DisplayCode.For(l.Code.Value, l.IsEncrypted, canSeeEncrypted), l.Name,
                visits.ContainsKey(l.Id), v.Required, v.Samples,
                ldm.GetValueOrDefault(l.Code.Value.ToUpperInvariant()), penalties.GetValueOrDefault(l.Id), previous.GetValueOrDefault(l.Id),
                e?.Id.Value, e?.Samples ?? 0, e?.TotalRequired.Amount ?? 0m, e?.Paid.Amount ?? 0m, e?.Remaining.Amount ?? 0m, e?.DelayedPayment.Amount ?? 0m, e?.Notes);
        }).ToList();
        return new RealIncomeSheetDto(date, area.Id.Value, area.Name, rep.Id.Value, rep.FullName, rows);
    }

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
