using FollowUp.Domain.Representatives;

namespace FollowUp.Application.Common.Abstractions;

/// <summary>
/// Decides which serving branch — and so which treasury — receives a collection's cash, now that a collection belongs to
/// its reps and not to a lab. Walks the reps in the order entered and returns the first branch that resolves:
/// the rep's own Branch when set, otherwise the most common serving branch among the labs the rep is responsible for
/// (ties by name). Null when no rep resolves, i.e. the collection cannot be placed (the collection is never blocked).
/// Implemented in Infrastructure (needs the lab table); shared by the collection commands and the reconcile runner.
/// </summary>
public interface ICollectionRouting
{
    Task<string?> ServingBranchAsync(IReadOnlyList<RepresentativeId> repIds, CancellationToken ct);
}
