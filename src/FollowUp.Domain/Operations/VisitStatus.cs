using FollowUp.Domain.Common;

namespace FollowUp.Domain.Operations;

/// <summary>
/// State of a daily collection visit (board item). Encodes the legal transitions of the visit
/// state machine (SRS FR-5, Workflows §3):
/// <code>
///   Pending → Visited        (check-in)
///   Pending → Missed         (evening sweep or manual miss)
///   Visited → Received       (transfer → lab check-in, FR-7)
///   Visited → Pending        (undo — only if not yet collected/transferred; guarded by aggregate)
/// </code>
/// <see cref="Received"/> is the terminal "at laboratory" state.
/// </summary>
public sealed class VisitStatus : Enumeration
{
    public static readonly VisitStatus Pending = new(1, nameof(Pending));
    public static readonly VisitStatus Visited = new(2, nameof(Visited));
    public static readonly VisitStatus Missed = new(3, nameof(Missed));
    public static readonly VisitStatus Received = new(4, nameof(Received));

    private static readonly Dictionary<VisitStatus, VisitStatus[]> Allowed = new()
    {
        [Pending] = new[] { Visited, Missed },
        [Visited] = new[] { Received, Pending },
        [Missed] = Array.Empty<VisitStatus>(),
        [Received] = Array.Empty<VisitStatus>(),
    };

    private VisitStatus(int id, string name) : base(id, name) { }

    public bool CanTransitionTo(VisitStatus target) => Allowed[this].Contains(target);

    /// <summary>Throws <see cref="IllegalStateTransitionException"/> when the move is not permitted.</summary>
    public void EnsureCanTransitionTo(VisitStatus target)
    {
        if (!CanTransitionTo(target))
            throw new IllegalStateTransitionException(nameof(VisitStatus), Name, target.Name);
    }

    public bool IsTerminal => Allowed[this].Length == 0;
}
