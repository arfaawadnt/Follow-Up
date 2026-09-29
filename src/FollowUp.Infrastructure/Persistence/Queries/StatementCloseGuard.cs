using FollowUp.Application.Features.Accounting;
using FollowUp.Domain.Common;
using FollowUp.Domain.Laboratories;
using FollowUp.Domain.Reference;
using FollowUp.Domain.Representatives;
using Microsoft.EntityFrameworkCore;

namespace FollowUp.Infrastructure.Persistence.Queries;

/// <summary>
/// Database-backed <see cref="IStatementCloseGuard"/> (2026-09-28): a write dated inside a month that is closed on the
/// statement of any Lab Responsible it posts to is refused with a DomainException naming the month and the rep.
/// Penalties post to the lab's Lab Responsible (and, a Rep penalty, to the rep who made it); deductions to every Lab
/// Responsible of the area's labs; collections and the Rep Income sheet to their reps.
/// </summary>
internal sealed class StatementCloseGuard : IStatementCloseGuard
{
    private readonly FollowUpDbContext _db;
    public StatementCloseGuard(FollowUpDbContext db) => _db = db;

    public async Task EnsureOpenForRepsAsync(IEnumerable<RepresentativeId> reps, DateOnly date, CancellationToken ct)
    {
        var ids = reps.Distinct().ToList();
        if (ids.Count == 0) return;
        var closed = await _db.StatementMonthCloses.AsNoTracking()
            .Where(c => ids.Contains(c.RepresentativeId) && c.Year == date.Year && c.Month == date.Month)
            .Select(c => c.RepresentativeId).FirstOrDefaultAsync(ct);
        if (closed == default) return;
        var name = await _db.Representatives.AsNoTracking().Where(r => r.Id == closed).Select(r => r.FullName).FirstOrDefaultAsync(ct) ?? "—";
        throw new DomainException($"{StatementMonthText.Of(date.Year, date.Month)} is closed on the statement of {name}. Reopen the month first.");
    }

    public async Task EnsureOpenForPenaltyAsync(LaboratoryId lab, RepresentativeId? performedBy, DateOnly date, CancellationToken ct)
    {
        var reps = new List<RepresentativeId>();
        var responsible = await _db.Laboratories.AsNoTracking().Where(l => l.Id == lab).Select(l => l.ResponsibleRepId).FirstOrDefaultAsync(ct);
        if (responsible is { } rid) reps.Add(rid);
        if (performedBy is { } p) reps.Add(p);
        await EnsureOpenForRepsAsync(reps, date, ct);
    }

    public async Task EnsureOpenForAreaAsync(AreaId area, DateOnly date, CancellationToken ct)
    {
        var areaName = await _db.Areas.AsNoTracking().Where(a => a.Id == area).Select(a => a.Name).FirstOrDefaultAsync(ct);
        if (areaName is null) return;
        var reps = await _db.Laboratories.AsNoTracking().Where(l => l.Area == areaName && l.ResponsibleRepId != null)
            .Select(l => l.ResponsibleRepId!.Value).Distinct().ToListAsync(ct);
        await EnsureOpenForRepsAsync(reps, date, ct);
    }
}
