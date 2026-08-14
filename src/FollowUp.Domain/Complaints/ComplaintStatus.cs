using FollowUp.Domain.Common;

namespace FollowUp.Domain.Complaints;

/// <summary>
/// Complaint status state machine (SRS FR-11, BR-11, Workflows §7.1). Illegal transitions are
/// rejected — the Api maps <see cref="IllegalStateTransitionException"/> to HTTP 409.
/// <code>
///   Open        → InProgress | Resolved
///   InProgress  → Resolved   | Open
///   Resolved    → Open        (reopen)
/// </code>
/// This is the single authoritative gate for status changes (the staged-investigation workflow is
/// metadata only) — closing the reference build's CMP-STAGE defect.
/// </summary>
public sealed class ComplaintStatus : Enumeration
{
    public static readonly ComplaintStatus Open = new(1, nameof(Open));
    public static readonly ComplaintStatus InProgress = new(2, "InProgress");
    public static readonly ComplaintStatus Resolved = new(3, nameof(Resolved));

    private static readonly Dictionary<ComplaintStatus, ComplaintStatus[]> Allowed = new()
    {
        [Open] = new[] { InProgress, Resolved },
        [InProgress] = new[] { Resolved, Open },
        [Resolved] = new[] { Open },
    };

    private ComplaintStatus(int id, string name) : base(id, name) { }

    public bool CanTransitionTo(ComplaintStatus target) => Allowed[this].Contains(target);

    public void EnsureCanTransitionTo(ComplaintStatus target)
    {
        if (!CanTransitionTo(target))
            throw new IllegalStateTransitionException(nameof(ComplaintStatus), Name, target.Name);
    }
}
