using FollowUp.Domain.Common;

namespace FollowUp.Domain.Operations;

/// <summary>
/// State of an outsourced sample forwarded to an external destination lab (SRS FR-9, Workflows §6).
/// Strictly linear — no skips, no reverse:
/// <code>Collected → Sent → Received</code>
/// </summary>
public sealed class OutsourceStatus : Enumeration
{
    public static readonly OutsourceStatus Collected = new(1, nameof(Collected));
    public static readonly OutsourceStatus Sent = new(2, nameof(Sent));
    public static readonly OutsourceStatus Received = new(3, nameof(Received));

    private static readonly Dictionary<OutsourceStatus, OutsourceStatus[]> Allowed = new()
    {
        [Collected] = new[] { Sent },
        [Sent] = new[] { Received },
        [Received] = Array.Empty<OutsourceStatus>(),
    };

    private OutsourceStatus(int id, string name) : base(id, name) { }

    public bool CanTransitionTo(OutsourceStatus target) => Allowed[this].Contains(target);

    public void EnsureCanTransitionTo(OutsourceStatus target)
    {
        if (!CanTransitionTo(target))
            throw new IllegalStateTransitionException(nameof(OutsourceStatus), Name, target.Name);
    }

    public bool IsTerminal => Allowed[this].Length == 0;
}
