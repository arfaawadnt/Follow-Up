using FollowUp.Domain.Common;
using FollowUp.Domain.Laboratories;
using FollowUp.Domain.Representatives;

namespace FollowUp.Domain.Marketing;

public readonly record struct MarketingVisitId(Guid Value)
{
    public static MarketingVisitId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}

/// <summary>Raised when a marketing visit is scheduled — queues a notification (SRS FR-10/FR-16).</summary>
public sealed record MarketingVisitScheduled(MarketingVisitId VisitId, LaboratoryId LaboratoryId) : Common.DomainEvent;

/// <summary>
/// A planned relationship/growth visit to a client lab (SRS FR-10, Workflows §8). One of seven purposes;
/// lifecycle Scheduled → Completed | Cancelled. Listings surface Scheduled first (BR-10).
/// </summary>
public sealed class MarketingVisit : AggregateRoot<MarketingVisitId>, IAuditable
{
    private MarketingVisit() { } // EF

    private MarketingVisit(MarketingVisitId id, LaboratoryId labId, RepresentativeId repId,
        MarketingPurpose purpose, DateOnly scheduledDate)
        : base(id)
    {
        LaboratoryId = labId;
        RepresentativeId = repId;
        Purpose = purpose;
        ScheduledDate = scheduledDate;
        Status = MarketingVisitStatus.Scheduled;
        Raise(new MarketingVisitScheduled(id, labId));
    }

    public LaboratoryId LaboratoryId { get; private set; }
    public RepresentativeId RepresentativeId { get; private set; }
    public MarketingPurpose Purpose { get; private set; } = null!;
    public DateOnly ScheduledDate { get; private set; }
    public MarketingVisitStatus Status { get; private set; } = null!;

    public string? Outcome { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public string? CancellationReason { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public string CreatedBy { get; private set; } = null!;
    public DateTimeOffset? UpdatedAt { get; private set; }
    public string? UpdatedBy { get; private set; }

    public static MarketingVisit Schedule(LaboratoryId labId, RepresentativeId repId,
        MarketingPurpose purpose, DateOnly scheduledDate) =>
        new(MarketingVisitId.New(), labId, repId, purpose, scheduledDate);

    public void Complete(string outcome, DateTimeOffset when)
    {
        Status.EnsureCanTransitionTo(MarketingVisitStatus.Completed);
        if (string.IsNullOrWhiteSpace(outcome))
            throw new DomainException("An outcome is required to complete a marketing visit.");
        Status = MarketingVisitStatus.Completed;
        Outcome = outcome.Trim();
        CompletedAt = when;
    }

    public void Cancel(string? reason)
    {
        Status.EnsureCanTransitionTo(MarketingVisitStatus.Cancelled);
        Status = MarketingVisitStatus.Cancelled;
        CancellationReason = reason?.Trim();
    }
}
