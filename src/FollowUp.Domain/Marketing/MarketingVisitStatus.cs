using FollowUp.Domain.Common;

namespace FollowUp.Domain.Marketing;

/// <summary>
/// Marketing-visit lifecycle (SRS FR-10, Workflows §8):
/// <code>Scheduled → Completed | Cancelled</code>
/// <see cref="Completed"/> and <see cref="Cancelled"/> are terminal.
/// </summary>
public sealed class MarketingVisitStatus : Enumeration
{
    public static readonly MarketingVisitStatus Scheduled = new(1, nameof(Scheduled));
    public static readonly MarketingVisitStatus Completed = new(2, nameof(Completed));
    public static readonly MarketingVisitStatus Cancelled = new(3, nameof(Cancelled));

    private static readonly Dictionary<MarketingVisitStatus, MarketingVisitStatus[]> Allowed = new()
    {
        [Scheduled] = new[] { Completed, Cancelled },
        [Completed] = Array.Empty<MarketingVisitStatus>(),
        [Cancelled] = Array.Empty<MarketingVisitStatus>(),
    };

    private MarketingVisitStatus(int id, string name) : base(id, name) { }

    public bool CanTransitionTo(MarketingVisitStatus target) => Allowed[this].Contains(target);

    public void EnsureCanTransitionTo(MarketingVisitStatus target)
    {
        if (!CanTransitionTo(target))
            throw new IllegalStateTransitionException(nameof(MarketingVisitStatus), Name, target.Name);
    }

    public bool IsTerminal => Allowed[this].Length == 0;
}
