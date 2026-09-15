namespace FollowUp.Application.Common.Abstractions;

/// <summary>Outcome of a collection → treasury reconcile.</summary>
/// <param name="Linked">Cash collections that had no mirroring treasury entry and received one.</param>
/// <param name="Unplaced">Cash collections whose lab has no serving branch, or whose branch no active treasury covers.</param>
public sealed record CollectionTreasurySyncResult(int Linked, int Unplaced);

/// <summary>
/// Mirrors every cash collection that has no treasury entry yet into the treasury serving its lab's branch (as a
/// Pending entry). The collection write handlers do this synchronously; this run covers collections recorded before
/// the mirroring existed and labs whose branch gained a treasury later. Implemented in Infrastructure; invoked from the
/// Treasury page ("Sync collections") and, as one pass, by the nightly accounting automation.
/// </summary>
public interface ICollectionTreasurySync
{
    Task<CollectionTreasurySyncResult> RunAsync(CancellationToken ct);
}
