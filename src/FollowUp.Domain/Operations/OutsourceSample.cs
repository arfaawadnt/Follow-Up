using FollowUp.Domain.Common;
using FollowUp.Domain.Laboratories;

namespace FollowUp.Domain.Operations;

public readonly record struct OutsourceSampleId(Guid Value)
{
    public static OutsourceSampleId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}

/// <summary>
/// A sample forwarded to an external destination lab (SRS FR-9, Workflows §6). Unique per
/// (visit date, lab). Advances strictly Collected → Sent → Received. Often auto-created at check-in.
/// </summary>
public sealed class OutsourceSample : AggregateRoot<OutsourceSampleId>, IAuditable
{
    private OutsourceSample() { } // EF

    private OutsourceSample(OutsourceSampleId id, LaboratoryId labId, DateOnly visitDate,
        string destinationLab, int quantity)
        : base(id)
    {
        LaboratoryId = labId;
        VisitDate = visitDate;
        DestinationLab = destinationLab;
        Quantity = quantity;
        Status = OutsourceStatus.Collected;
    }

    public LaboratoryId LaboratoryId { get; private set; }
    public DateOnly VisitDate { get; private set; }
    public string DestinationLab { get; private set; } = null!;
    public int Quantity { get; private set; }
    public OutsourceStatus Status { get; private set; } = null!;

    public DateTimeOffset? SentAt { get; private set; }
    public DateTimeOffset? ReceivedAt { get; private set; }

    /// <summary>Free-text notes (reference parity).</summary>
    public string? Notes { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public string CreatedBy { get; private set; } = null!;
    public DateTimeOffset? UpdatedAt { get; private set; }
    public string? UpdatedBy { get; private set; }

    public static OutsourceSample Create(LaboratoryId labId, DateOnly visitDate, string destinationLab, int quantity, string? notes = null)
    {
        if (string.IsNullOrWhiteSpace(destinationLab))
            throw new DomainException("Destination lab is required for an outsourced sample.");
        if (quantity <= 0)
            throw new DomainException("Outsource quantity must be positive.");
        var sample = new OutsourceSample(OutsourceSampleId.New(), labId, visitDate, destinationLab.Trim(), quantity);
        sample.SetNotes(notes);
        return sample;
    }

    public void SetNotes(string? notes) => Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();

    public void AdvanceTo(OutsourceStatus target, DateTimeOffset when)
    {
        Status.EnsureCanTransitionTo(target);
        Status = target;
        if (target == OutsourceStatus.Sent) SentAt = when;
        else if (target == OutsourceStatus.Received) ReceivedAt = when;
    }
}
