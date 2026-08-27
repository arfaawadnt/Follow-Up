using FollowUp.Domain.Common;
using FollowUp.Domain.Laboratories;
using FollowUp.Domain.Representatives;

namespace FollowUp.Domain.Operations;

public readonly record struct VisitHistoryId(Guid Value)
{
    public static VisitHistoryId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}

/// <summary>
/// An immutable archive snapshot of a <see cref="DailyVisit"/> written by the midnight roll-over
/// (Workflows §4). Status is copied <b>verbatim</b> — which is exactly why the evening missed-sweep must run
/// first (JOBS-001) so a still-Pending visit is not archived as a non-terminal state. RESTRICT FK: history
/// cannot be silently removed.
/// </summary>
public sealed class VisitHistory : AggregateRoot<VisitHistoryId>
{
    private VisitHistory() { } // EF

    private VisitHistory(VisitHistoryId id, DailyVisitId originalVisitId, LaboratoryId labId,
        RepresentativeId? collectorRepId, DateOnly visitDate, string status, int? sampleCount,
        bool adminChecked, DateTimeOffset archivedAt)
        : base(id)
    {
        OriginalVisitId = originalVisitId;
        LaboratoryId = labId;
        CollectorRepId = collectorRepId;
        VisitDate = visitDate;
        Status = status;
        SampleCount = sampleCount;
        AdminChecked = adminChecked;
        ArchivedAt = archivedAt;
    }

    public DailyVisitId OriginalVisitId { get; private set; }
    public LaboratoryId LaboratoryId { get; private set; }
    public RepresentativeId? CollectorRepId { get; private set; }
    public DateOnly VisitDate { get; private set; }
    public string Status { get; private set; } = null!;
    public int? SampleCount { get; private set; }
    public bool AdminChecked { get; private set; }
    public DateTimeOffset ArchivedAt { get; private set; }

    // Lifecycle-stage snapshot (nullable; null on rows archived before these were captured).
    public TimeOnly? ScheduledTime { get; private set; }
    public DateTimeOffset? CheckedInAt { get; private set; }
    public DateTimeOffset? TransferConfirmedAt { get; private set; }
    public DateTimeOffset? ReceivedAt { get; private set; }

    // Transfer leg (flattened) so the lifecycle/motion reports keep driver data after archival.
    public RepresentativeId? TransferRepId { get; private set; }
    public string? DriverName { get; private set; }
    public string? DriverMobile { get; private set; }
    public string? CarPlate { get; private set; }

    /// <summary>Archives a visit verbatim. Callers must ensure the evening sweep already ran (JOBS-001).</summary>
    public static VisitHistory ArchiveFrom(DailyVisit visit, DateTimeOffset archivedAt) =>
        new(VisitHistoryId.New(), visit.Id, visit.LaboratoryId, visit.CollectorRepId, visit.VisitDate,
            visit.Status.Name, visit.SampleCount, visit.AdminChecked, archivedAt)
        {
            ScheduledTime = visit.ScheduledTime,
            CheckedInAt = visit.CheckedInAt,
            TransferConfirmedAt = visit.TransferConfirmedAt,
            ReceivedAt = visit.ReceivedAt,
            TransferRepId = visit.TransferRepId,
            DriverName = visit.Transfer?.DriverName,
            DriverMobile = visit.Transfer?.DriverMobile,
            CarPlate = visit.Transfer?.CarPlate,
        };
}
