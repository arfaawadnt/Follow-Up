using FollowUp.Domain.Common;

namespace FollowUp.Domain.Representatives;

/// <summary>Field-workforce role of a representative (SRS FR-4, rep type CHECK = 4 values).</summary>
public sealed class RepresentativeType : Enumeration
{
    /// <summary>Collects biological samples from client labs on daily rounds.</summary>
    public static readonly RepresentativeType Collector = new(1, nameof(Collector));

    /// <summary>Owns the commercial relationship and growth.</summary>
    public static readonly RepresentativeType Marketing = new(2, nameof(Marketing));

    /// <summary>Transports collected samples to the hub.</summary>
    public static readonly RepresentativeType Transfer = new(3, nameof(Transfer));

    /// <summary>Discovers and onboards new client labs.</summary>
    public static readonly RepresentativeType Scanning = new(4, nameof(Scanning));

    private RepresentativeType(int id, string name) : base(id, name) { }
}

/// <summary>Target-measurement cadence for a representative's goal (SRS FR-4).</summary>
public sealed class GoalDuration : Enumeration
{
    public static readonly GoalDuration Monthly = new(1, nameof(Monthly));
    public static readonly GoalDuration Quarterly = new(2, nameof(Quarterly));

    private GoalDuration(int id, string name) : base(id, name) { }
}
