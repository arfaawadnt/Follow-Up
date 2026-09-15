using FollowUp.Domain.Common;

namespace FollowUp.Domain.Representatives;

/// <summary>Field-workforce role of a representative (SRS FR-4; persisted by Name, guarded by the ck_representative_type CHECK).</summary>
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

    /// <summary>
    /// Day-to-day owner of a client lab's relationship and money: assignable as a <see cref="Laboratories.Laboratory"/>'s
    /// responsible, and the only type that can appear on an accounting Collection (it collects the labs' money).
    /// Renamed from <c>AreaResponsible</c> (id 5 kept; existing rows are renamed by migration).
    /// </summary>
    public static readonly RepresentativeType LabResponsible = new(5, nameof(LabResponsible));

    /// <summary>Manages an area's team and results; assignable as an <see cref="Reference.Area"/>'s manager.</summary>
    public static readonly RepresentativeType AreaManager = new(6, nameof(AreaManager));

    private RepresentativeType(int id, string name) : base(id, name) { }
}

/// <summary>Target-measurement cadence for a representative's goal (SRS FR-4).</summary>
public sealed class GoalDuration : Enumeration
{
    public static readonly GoalDuration Monthly = new(1, nameof(Monthly));
    public static readonly GoalDuration Quarterly = new(2, nameof(Quarterly));

    private GoalDuration(int id, string name) : base(id, name) { }
}
