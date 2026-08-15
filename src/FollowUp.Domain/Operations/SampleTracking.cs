using FollowUp.Domain.Common;

namespace FollowUp.Domain.Operations;

public readonly record struct SampleTrackingId(Guid Value)
{
    public static SampleTrackingId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}

/// <summary>A processing step stamped with who did it and when.</summary>
public sealed class TrackingStep : ValueObject
{
    public string User { get; }
    public DateTimeOffset At { get; }

    public TrackingStep(string user, DateTimeOffset at)
    {
        if (string.IsNullOrWhiteSpace(user)) throw new DomainException("Tracking step requires an acting user.");
        User = user;
        At = at;
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return User;
        yield return At;
    }
}

/// <summary>
/// Per-area, per-day sample-tracking record (SRS FR-8, Workflows §5). Strictly linear pipeline
/// <c>Data entry → Review → Sort</c>, each step capturing acting user + timestamp. A later step cannot be
/// recorded before its predecessor.
/// </summary>
public sealed class SampleTracking : AggregateRoot<SampleTrackingId>, IAuditable
{
    private SampleTracking() { } // EF

    private SampleTracking(SampleTrackingId id, string area, DateOnly date)
        : base(id)
    {
        Area = area;
        Date = date;
    }

    public string Area { get; private set; } = null!;
    public DateOnly Date { get; private set; }
    public int Count { get; private set; }

    public TrackingStep? DataEntry { get; private set; }
    public TrackingStep? Review { get; private set; }
    public TrackingStep? Sort { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public string CreatedBy { get; private set; } = null!;
    public DateTimeOffset? UpdatedAt { get; private set; }
    public string? UpdatedBy { get; private set; }

    public static SampleTracking Open(string area, DateOnly date)
    {
        if (string.IsNullOrWhiteSpace(area)) throw new DomainException("Sample-tracking area is required.");
        return new SampleTracking(SampleTrackingId.New(), area.Trim(), date);
    }

    public void RecordDataEntry(int count, string user, DateTimeOffset at)
    {
        if (count < 0) throw new DomainException("Count cannot be negative.");
        Count = count;
        DataEntry = new TrackingStep(user, at);
    }

    public void RecordReview(string user, DateTimeOffset at)
    {
        if (DataEntry is null) throw new DomainException("Review cannot be recorded before data entry.");
        Review = new TrackingStep(user, at);
    }

    public void RecordSort(string user, DateTimeOffset at)
    {
        if (Review is null) throw new DomainException("Sort cannot be recorded before review.");
        Sort = new TrackingStep(user, at);
    }

    public bool IsComplete => DataEntry is not null && Review is not null && Sort is not null;
}
