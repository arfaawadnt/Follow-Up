namespace FollowUp.Domain.Common;

/// <summary>
/// Creation/modification stamps maintained by the infrastructure layer (not set by domain code).
/// Distinct from the immutable <c>audit_entry</c> trail — this is per-row provenance only.
/// </summary>
public interface IAuditable
{
    DateTimeOffset CreatedAt { get; }
    string CreatedBy { get; }
    DateTimeOffset? UpdatedAt { get; }
    string? UpdatedBy { get; }
}

/// <summary>
/// Optimistic-concurrency marker. Labs and reps carry a row-version token; a stale update yields 409
/// (SRS FR-3/FR-4, NFR-REL-4). Other aggregates are last-writer-wins.
/// </summary>
public interface IVersioned
{
    /// <summary>Monotonic row-version token; compared on update to detect concurrent modification.</summary>
    uint RowVersion { get; }
}
