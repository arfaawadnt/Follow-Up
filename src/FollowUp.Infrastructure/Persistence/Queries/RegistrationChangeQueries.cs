using FollowUp.Application.Features.RegistrationChanges;
using FollowUp.Domain.Identity;
using FollowUp.Domain.Reference;
using Microsoft.EntityFrameworkCore;

namespace FollowUp.Infrastructure.Persistence.Queries;

/// <summary>
/// Registration Changes read side (2026-09-30): the synced REG_LOG rows in the requested registration-date and/or
/// modification-date ranges, enriched with the lab's name and stamped geography and the registration branch name.
/// Scope (finding B-6 pattern): a non-global caller sees only changes of registrations resolving to a lab in scope;
/// "No lab" rows are visible to a global scope only. Filtering by change type / user / lab happens client-side.
/// </summary>
internal sealed class RegistrationChangeQueries : IRegistrationChangeQueries
{
    private readonly FollowUpDbContext _db;
    public RegistrationChangeQueries(FollowUpDbContext db) => _db = db;

    public async Task<IReadOnlyList<RegistrationChangeDto>> ListAsync(DateOnly? regFrom, DateOnly? regTo, DateOnly? modFrom, DateOnly? modTo, OrgScope scope, CancellationToken ct)
    {
        var q = _db.RegistrationChanges.AsNoTracking();
        if (regFrom is { } rf && regTo is { } rt) q = q.Where(c => c.RegDate >= rf && c.RegDate <= rt);
        if (modFrom is { } mf && modTo is { } mt) q = q.Where(c => c.ModifiedDate >= mf && c.ModifiedDate <= mt);

        var global = scope.Branches.Contains(OrgScope.Wildcard) && scope.Governorates.Contains(OrgScope.Wildcard) &&
            scope.Cities.Contains(OrgScope.Wildcard) && scope.Areas.Contains(OrgScope.Wildcard) &&
            scope.Categories.Contains(OrgScope.Wildcard) && scope.Segments.Contains(OrgScope.Wildcard);
        if (!global)
        {
            var allowed = (await _db.Laboratories.ApplyScope(scope).Select(l => l.Code).ToListAsync(ct)).Select(c => c.Value.ToUpperInvariant()).ToList();
            q = q.Where(c => c.LabCode != null && allowed.Contains(c.LabCode));
        }
        var rows = await q.OrderByDescending(c => c.ModifiedAt).ThenByDescending(c => c.TransId).ToListAsync(ct);

        var labInfo = (await _db.Laboratories.AsNoTracking()
                .Select(l => new { l.Code, l.Name, l.Governorate, l.City, l.Area }).ToListAsync(ct))
            .GroupBy(l => l.Code.Value.ToUpperInvariant()).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var branchName = (await _db.RefItems.AsNoTracking().Where(r => r.Type == RefType.Branch)
                .Select(r => new { r.Code, r.NameEn }).ToListAsync(ct))
            .GroupBy(r => r.Code).ToDictionary(g => g.Key, g => g.First().NameEn, StringComparer.OrdinalIgnoreCase);

        return rows.Select(c =>
        {
            var l = c.LabCode != null && labInfo.TryGetValue(c.LabCode, out var found) ? found : null;
            var branch = c.RegBranchCode != null && branchName.TryGetValue(c.RegBranchCode, out var bn) ? bn : c.RegBranchCode;
            return new RegistrationChangeDto(c.TransId, c.RegKey, c.AccNo, c.PatientName, c.RegCreatedAt, c.RegDate, branch,
                c.LabCode, l?.Name, l?.Governorate, l?.City, l?.Area, c.Column, c.OldValue, c.NewValue, c.ModifiedBy, c.ModifiedAt,
                c.DelayFromCreation is { } d ? Math.Round(d.TotalMinutes, 1) : null);
        }).ToList();
    }
}
