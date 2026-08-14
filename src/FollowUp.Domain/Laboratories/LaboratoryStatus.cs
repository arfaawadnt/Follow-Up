using FollowUp.Domain.Common;

namespace FollowUp.Domain.Laboratories;

/// <summary>
/// Lifecycle status of a client laboratory (SRS: lab status CHECK = 8 values; BR-5 auto-derived
/// from activity). The eight members are not enumerated in the source docs — see docs/ASSUMPTIONS.md
/// (A1) — and are modeled here from the design-system badge domain-state mapping. Isolated behind this
/// value object so the exact set can be adjusted without touching aggregates.
/// </summary>
public sealed class LaboratoryStatus : Enumeration
{
    /// <summary>Just registered; not yet producing collections.</summary>
    public static readonly LaboratoryStatus New = new(1, nameof(New));

    /// <summary>Discovered by a Scanning rep, pending onboarding.</summary>
    public static readonly LaboratoryStatus Scanned = new(2, nameof(Scanned));

    /// <summary>Producing collections normally.</summary>
    public static readonly LaboratoryStatus Active = new(3, nameof(Active));

    /// <summary>Onboarded but currently no activity.</summary>
    public static readonly LaboratoryStatus Inactive = new(4, nameof(Inactive));

    /// <summary>Awaiting a first visit / activation step.</summary>
    public static readonly LaboratoryStatus Pending = new(5, nameof(Pending));

    /// <summary>Temporarily halted (e.g. commercial hold).</summary>
    public static readonly LaboratoryStatus Suspended = new(6, nameof(Suspended));

    /// <summary>Service stopped.</summary>
    public static readonly LaboratoryStatus Stopped = new(7, nameof(Stopped));

    /// <summary>Relationship lost.</summary>
    public static readonly LaboratoryStatus Churned = new(8, nameof(Churned));

    private LaboratoryStatus(int id, string name) : base(id, name) { }

    /// <summary>Statuses in which a lab is scheduled onto the daily board.</summary>
    public bool IsSchedulable => this == Active || this == Pending || this == New;
}

/// <summary>Commercial tier of a client laboratory (A/B/C).</summary>
public sealed class Segment : Enumeration
{
    public static readonly Segment A = new(1, nameof(A));
    public static readonly Segment B = new(2, nameof(B));
    public static readonly Segment C = new(3, nameof(C));

    private Segment(int id, string name) : base(id, name) { }
}
