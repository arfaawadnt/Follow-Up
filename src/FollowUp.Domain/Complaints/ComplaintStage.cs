using FollowUp.Domain.Common;

namespace FollowUp.Domain.Complaints;

/// <summary>
/// The staged-investigation narrative position (SRS FR-11, Workflows §7.2). This is metadata that
/// records where the investigation stands; it is NOT the authority on status — every status change
/// flows through <see cref="ComplaintStatus"/> (closing the CMP-STAGE defect).
/// </summary>
public sealed class ComplaintStage : Enumeration
{
    public static readonly ComplaintStage Logged = new(1, nameof(Logged));
    public static readonly ComplaintStage Acknowledged = new(2, nameof(Acknowledged));
    public static readonly ComplaintStage ValidityChecked = new(3, nameof(ValidityChecked));
    public static readonly ComplaintStage Investigation = new(4, nameof(Investigation));
    public static readonly ComplaintStage BusinessOutcome = new(5, nameof(BusinessOutcome));
    public static readonly ComplaintStage Resolution = new(6, nameof(Resolution));
    public static readonly ComplaintStage RejectedInvalid = new(7, nameof(RejectedInvalid));

    private ComplaintStage(int id, string name) : base(id, name) { }
}
