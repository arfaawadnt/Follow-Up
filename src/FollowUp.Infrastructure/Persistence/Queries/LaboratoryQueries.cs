using FollowUp.Application.Common.Models;
using FollowUp.Application.Features.Laboratories.Contracts;
using FollowUp.Application.Features.Representatives.Contracts;
using FollowUp.Domain.Common;
using FollowUp.Domain.Identity;
using FollowUp.Domain.Laboratories;
using Microsoft.EntityFrameworkCore;

namespace FollowUp.Infrastructure.Persistence.Queries;

/// <summary>Computes the caller-visible display code (real code, or the deterministic ENC alias).</summary>
internal static class DisplayCode
{
    public static string For(string realCode, bool canSeeEncrypted) =>
        canSeeEncrypted ? realCode : LabCode.Create(realCode).ToEncryptedAlias();
}

internal sealed class LaboratoryQueries : ILaboratoryQueries
{
    private readonly FollowUpDbContext _db;
    public LaboratoryQueries(FollowUpDbContext db) => _db = db;

    public async Task<PagedResult<LabListItemDto>> SearchAsync(
        LabSearchCriteria criteria, OrgScope scope, bool canSeeEncrypted, CancellationToken ct)
    {
        var query = _db.Laboratories.AsNoTracking().ApplyScope(scope);

        if (!string.IsNullOrWhiteSpace(criteria.Status))
        {
            var status = Enumeration.FromName<LaboratoryStatus>(criteria.Status);
            query = query.Where(l => l.Status == status);
        }
        if (!string.IsNullOrWhiteSpace(criteria.Segment))
            query = query.Where(l => l.Segment == criteria.Segment);
        if (!string.IsNullOrWhiteSpace(criteria.Governorate))
            query = query.Where(l => l.Governorate == criteria.Governorate);
        if (!string.IsNullOrWhiteSpace(criteria.Search))
        {
            var term = $"%{criteria.Search.Trim()}%";
            query = query.Where(l => EF.Functions.ILike(l.Name, term));
        }

        var total = await query.CountAsync(ct);

        // Materialize the page's lab aggregates (small page) so the jsonb collector list + owned location convert.
        var labs = await query.OrderBy(l => l.Name).Skip(criteria.Skip).Take(criteria.PageSize).ToListAsync(ct);

        var repNames = await ResolveRepNamesAsync(
            labs.SelectMany(l => l.CollectorRepIds)
                .Concat(labs.Where(l => l.MarketingRepId != null).Select(l => l.MarketingRepId!.Value)), ct);

        var items = labs.Select(l => new LabListItemDto(
            // Per-lab confidentiality: only labs flagged encrypted are masked for non-privileged users.
            l.Id.Value, DisplayCode.For(l.Code.Value, canSeeEncrypted || !l.IsEncrypted), l.Name, l.Segment, l.Status.Name,
            l.Branch, l.Governorate, l.City, l.Area, l.Category, l.AvgMonthlySamples,
            l.Location?.Latitude, l.Location?.Longitude,
            l.CollectorRepIds.Select(c => repNames.GetValueOrDefault(c, "—")).ToList(),
            l.MarketingRepId is { } m ? repNames.GetValueOrDefault(m) : null,
            l.IsEncrypted && !canSeeEncrypted)).ToList();

        return PagedResult<LabListItemDto>.Create(items, total, criteria.Page, criteria.PageSize);
    }

    public async Task<LabDetailDto?> GetByIdAsync(Guid id, bool canSeeEncrypted, CancellationToken ct)
    {
        var lab = await _db.Laboratories.AsNoTracking().FirstOrDefaultAsync(l => l.Id == new LaboratoryId(id), ct);
        if (lab is null) return null;

        return new LabDetailDto(
            lab.Id.Value, DisplayCode.For(lab.Code.Value, canSeeEncrypted || !lab.IsEncrypted), lab.Name, lab.Segment, lab.Status.Name,
            lab.Branch, lab.Governorate, lab.City, lab.Area, lab.Category, lab.Address,
            lab.MappingCode, lab.IsEncrypted, lab.ImagePaths.ToList(), lab.Payer, lab.ContractType,
            lab.LicenseNo, lab.LicenseDate, lab.AvgMonthlySamples, lab.PreferredChannel,
            lab.Location?.Latitude, lab.Location?.Longitude, lab.MonthlyTarget, lab.LoyaltyPoints, lab.LoyaltyTier,
            lab.CollectorRepIds.Select(c => c.Value).ToList(), lab.MarketingRepId?.Value,
            lab.Schedule.WorkDays.Select(d => d.ToString()).ToList(),
            lab.Schedule.VisitTimes.Select(t => t.ToString("HH:mm")).ToList(),
            lab.Contacts.Select(c => new ContactDto(c.Id.Value, c.Name, c.Role.ToString(), c.Phone, c.Birthday)).ToList(),
            lab.RowVersion);
    }

    private async Task<Dictionary<Domain.Representatives.RepresentativeId, string>> ResolveRepNamesAsync(
        IEnumerable<Domain.Representatives.RepresentativeId> ids, CancellationToken ct)
    {
        var idList = ids.Distinct().ToList();
        if (idList.Count == 0) return new();
        return await _db.Representatives.AsNoTracking().Where(r => idList.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, r => r.FullName, ct);
    }
}

internal sealed class RepresentativeQueries : IRepresentativeQueries
{
    private readonly FollowUpDbContext _db;
    public RepresentativeQueries(FollowUpDbContext db) => _db = db;

    public async Task<PagedResult<Application.Features.Representatives.Contracts.RepListItemDto>> SearchAsync(
        Application.Features.Representatives.Contracts.RepSearchCriteria criteria, OrgScope scope, CancellationToken ct)
    {
        var query = _db.Representatives.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(criteria.Type))
            query = query.Where(r => r.Type == Enumeration.FromName<Domain.Representatives.RepresentativeType>(criteria.Type));
        if (criteria.ActiveOnly == true)
            query = query.Where(r => r.IsActive);
        if (!string.IsNullOrWhiteSpace(criteria.Search))
            query = query.Where(r => EF.Functions.ILike(r.FullName, $"%{criteria.Search.Trim()}%"));

        var total = await query.CountAsync(ct);
        var rows = await query
            .OrderBy(r => r.FullName)
            .Skip(criteria.Skip).Take(criteria.PageSize)
            .Select(r => new { r.Id, r.FullName, r.Type, r.GoalDuration, r.GoalType, r.Metric, r.Target, r.Salary, r.Phone, r.IsActive, r.Branch, r.Governorate, r.Area, r.EmploymentType })
            .ToListAsync(ct);

        // Assigned-lab counts (collector across the jsonb list, or marketing rep) — materialize and count in memory.
        var labReps = await _db.Laboratories.AsNoTracking().Select(l => new { l.CollectorRepIds, l.MarketingRepId }).ToListAsync(ct);
        var counts = new Dictionary<Domain.Representatives.RepresentativeId, int>();
        foreach (var lr in labReps)
        {
            foreach (var c in lr.CollectorRepIds) counts[c] = counts.GetValueOrDefault(c) + 1;
            if (lr.MarketingRepId is { } mk) counts[mk] = counts.GetValueOrDefault(mk) + 1;
        }

        var items = rows.Select(r => new Application.Features.Representatives.Contracts.RepListItemDto(
            r.Id.Value, r.FullName, r.Type.Name, r.GoalDuration.Name, r.GoalType, r.Metric,
            r.Target.Amount, r.Salary.Amount, r.Phone, counts.GetValueOrDefault(r.Id), r.IsActive,
            r.Branch, r.Governorate, r.Area, r.EmploymentType)).ToList();

        return PagedResult<Application.Features.Representatives.Contracts.RepListItemDto>
            .Create(items, total, criteria.Page, criteria.PageSize);
    }

    public async Task<Application.Features.Representatives.Contracts.RepDetailDto?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        var r = await _db.Representatives.AsNoTracking().FirstOrDefaultAsync(x => x.Id == new Domain.Representatives.RepresentativeId(id), ct);
        return r is null ? null : new Application.Features.Representatives.Contracts.RepDetailDto(
            r.Id.Value, r.FullName, r.Type.Name, r.GoalDuration.Name, r.GoalType, r.Metric,
            r.Salary.Amount, r.Target.Amount, r.Phone, r.Branch, r.Governorate, r.Area, r.EmploymentType, r.IsActive, r.RowVersion);
    }
}
