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
        string? destinationLab, int quantity)
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
    /// <summary>Null while unassigned (auto-created from check-in); set before dispatch.</summary>
    public string? DestinationLab { get; private set; }
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

    public static OutsourceSample Create(LaboratoryId labId, DateOnly visitDate, string? destinationLab, int quantity, string? notes = null)
    {
        if (quantity <= 0)
            throw new DomainException("Outsource quantity must be positive.");
        var sample = new OutsourceSample(OutsourceSampleId.New(), labId, visitDate,
            string.IsNullOrWhiteSpace(destinationLab) ? null : destinationLab.Trim(), quantity);
        sample.SetNotes(notes);
        return sample;
    }

    public void SetNotes(string? notes) => Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();

    /// <summary>Inline row edit (reference parity): quantity, destination and notes.</summary>
    public void Update(int quantity, string? destinationLab, string? notes)
    {
        if (quantity <= 0)
            throw new DomainException("Outsource quantity must be positive.");
        Quantity = quantity;
        DestinationLab = string.IsNullOrWhiteSpace(destinationLab) ? null : destinationLab.Trim();
        SetNotes(notes);
    }

    public void AdvanceTo(OutsourceStatus target, DateTimeOffset when)
    {
        if (target == OutsourceStatus.Sent && DestinationLab is null)
            throw new DomainException("Set the destination lab before dispatching an outsource sample.");
        Status.EnsureCanTransitionTo(target);
        Status = target;
        if (target == OutsourceStatus.Sent) SentAt = when;
        else if (target == OutsourceStatus.Received) ReceivedAt = when;
    }
}
