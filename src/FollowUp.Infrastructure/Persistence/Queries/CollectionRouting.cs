using FollowUp.Application.Common.Abstractions;
using FollowUp.Domain.Representatives;
using Microsoft.EntityFrameworkCore;

namespace FollowUp.Infrastructure.Persistence.Queries;

/// <summary>
/// <see cref="ICollectionRouting"/> over the real tables: for each rep in order, the rep's own Branch when set, else the
/// most common serving branch of the labs the rep is responsible for (ties broken by name); the first rep that resolves
/// wins. The static overload lets the reconcile runner batch the same rule over many collections with one lab scan.
/// </summary>
internal sealed class CollectionRouting : ICollectionRouting
{
    private readonly FollowUpDbContext _db;
    public CollectionRouting(FollowUpDbContext db) => _db = db;

    public async Task<string?> ServingBranchAsync(IReadOnlyList<RepresentativeId> repIds, CancellationToken ct)
    {
        if (repIds.Count == 0) return null;
        var resolver = await BuildResolverAsync(_db, repIds, ct);
        return Resolve(repIds, resolver);
    }

    /// <summary>Loads what is needed to route the given reps; returns a lookup rep → branch (null when unresolved).</summary>
    public static async Task<IReadOnlyDictionary<RepresentativeId, string?>> BuildResolverAsync(FollowUpDbContext db, IReadOnlyCollection<RepresentativeId> repIds, CancellationToken ct)
    {
        var ids = repIds.Distinct().ToList();
        var own = await db.Representatives.AsNoTracking().Where(r => ids.Contains(r.Id))
            .Select(r => new { r.Id, r.Branch }).ToDictionaryAsync(r => r.Id, r => r.Branch, ct);
        // Strongly-typed ids convert fine in Where/Select; the grouping runs in memory (a responsible has a handful of labs).
        var labBranches = await db.Laboratories.AsNoTracking()
            .Where(l => l.ResponsibleRepId != null && ids.Contains(l.ResponsibleRepId!.Value) && l.Branch != null && l.Branch != "")
            .Select(l => new { Rep = l.ResponsibleRepId!.Value, l.Branch })
            .ToListAsync(ct);
        var byLabs = labBranches.GroupBy(x => x.Rep)
            .ToDictionary(g => g.Key, g => g.GroupBy(x => x.Branch!, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(x => x.Count()).ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase).First().Key);

        var result = new Dictionary<RepresentativeId, string?>();
        foreach (var id in ids)
        {
            var branch = own.TryGetValue(id, out var b) && !string.IsNullOrWhiteSpace(b) ? b : byLabs.GetValueOrDefault(id);
            result[id] = string.IsNullOrWhiteSpace(branch) ? null : branch;
        }
        return result;
    }

    /// <summary>First rep (in order) with a resolved branch; null when none.</summary>
    public static string? Resolve(IEnumerable<RepresentativeId> repIds, IReadOnlyDictionary<RepresentativeId, string?> resolver)
    {
        foreach (var id in repIds)
            if (resolver.TryGetValue(id, out var b) && b is not null) return b;
        return null;
    }
}
