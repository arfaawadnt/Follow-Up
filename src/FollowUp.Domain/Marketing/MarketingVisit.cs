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

    private MarketingVisit(MarketingVisitId id, int number, LaboratoryId labId, RepresentativeId repId,
        MarketingPurpose purpose, DateOnly scheduledDate, TimeOnly? scheduledTime, string? plan)
        : base(id)
    {
        Number = number;
        LaboratoryId = labId;
        RepresentativeId = repId;
        Purpose = purpose;
        ScheduledDate = scheduledDate;
        ScheduledTime = scheduledTime;
        Plan = string.IsNullOrWhiteSpace(plan) ? null : plan.Trim();
        Status = MarketingVisitStatus.Scheduled;
        Raise(new MarketingVisitScheduled(id, labId));
    }

    /// <summary>The sequential integer behind the <c>MV{n}</c> reference (mirrors BR-2's pattern).</summary>
    public int Number { get; private set; }

    /// <summary>Human reference, e.g. <c>MV8</c>.</summary>
    public string Reference => $"MV{Number}";

    public LaboratoryId LaboratoryId { get; private set; }
    public RepresentativeId RepresentativeId { get; private set; }
    public MarketingPurpose Purpose { get; private set; } = null!;
    public DateOnly ScheduledDate { get; private set; }
    public TimeOnly? ScheduledTime { get; private set; }

    /// <summary>The planned agenda for the visit (shown as OUTCOME / PLAN while scheduled).</summary>
    public string? Plan { get; private set; }

    public MarketingVisitStatus Status { get; private set; } = null!;

    public string? Outcome { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public string? CancellationReason { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public string CreatedBy { get; private set; } = null!;
    public DateTimeOffset? UpdatedAt { get; private set; }
    public string? UpdatedBy { get; private set; }

    public static MarketingVisit Schedule(int number, LaboratoryId labId, RepresentativeId repId,
        MarketingPurpose purpose, DateOnly scheduledDate, TimeOnly? scheduledTime = null, string? plan = null)
    {
        if (number <= 0) throw new DomainException("Marketing visit number must be positive.");
        return new(MarketingVisitId.New(), number, labId, repId, purpose, scheduledDate, scheduledTime, plan);
    }

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
