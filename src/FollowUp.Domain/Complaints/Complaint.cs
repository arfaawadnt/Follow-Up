using FollowUp.Domain.Common;
using FollowUp.Domain.Laboratories;

namespace FollowUp.Domain.Complaints;

public readonly record struct ComplaintId(Guid Value)
{
    public static ComplaintId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}

public sealed record ComplaintLogged(ComplaintId ComplaintId, LaboratoryId LaboratoryId, string Number) : DomainEvent;
public sealed record ComplaintResolved(ComplaintId ComplaintId, LaboratoryId LaboratoryId, string Number) : DomainEvent;

/// <summary>
/// A client-lab complaint (SRS FR-11). Carries a sequential business number <c>CMP-{n}</c> (BR-2), a
/// restricted status machine (BR-11 — illegal transitions → 409) and a staged-investigation narrative.
/// All status changes go through the state machine; resolution honours the optional e-signature gate.
/// </summary>
public sealed class Complaint : AggregateRoot<ComplaintId>, IAuditable
{
    private Complaint() { } // EF

    private Complaint(ComplaintId id, int number, LaboratoryId labId, string category,
        string viaChannel, string? assignedTeam, string details)
        : base(id)
    {
        Number = number;
        LaboratoryId = labId;
        Category = category;
        ViaChannel = viaChannel;
        AssignedTeam = assignedTeam;
        Details = details;
        Status = ComplaintStatus.Open;
        Stage = ComplaintStage.Logged;
        Raise(new ComplaintLogged(id, labId, Reference));
    }

    // Optional intake metadata (reference parity).
    public Guid? RepresentativeId { get; private set; }
    public DateTimeOffset? ReceivedAt { get; private set; }

    // Staged-investigation narrative payloads (reference parity: the Details popup fields).
    public bool? IsValid { get; private set; }
    public string? ValidityNotes { get; private set; }
    public string? InvestigationNotes { get; private set; }
    public string? OutcomeType { get; private set; }
    public string? OutcomeSummary { get; private set; }
    public string? ResolutionSummary { get; private set; }

    /// <summary>The sequential integer behind the <c>CMP-{n}</c> reference (BR-2).</summary>
    public int Number { get; private set; }

    /// <summary>Human reference, e.g. <c>CMP-42</c>.</summary>
    public string Reference => $"CMP-{Number}";

    public LaboratoryId LaboratoryId { get; private set; }
    public string Category { get; private set; } = null!;
    public string ViaChannel { get; private set; } = null!;
    public string? AssignedTeam { get; private set; }
    public string Details { get; private set; } = null!;

    public ComplaintStatus Status { get; private set; } = null!;
    public ComplaintStage Stage { get; private set; } = null!;

    public DateTimeOffset? ResolvedAt { get; private set; }
    public string? ResolvedBy { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public string CreatedBy { get; private set; } = null!;
    public DateTimeOffset? UpdatedAt { get; private set; }
    public string? UpdatedBy { get; private set; }

    /// <summary>
    /// Logs a new complaint. <paramref name="number"/> must be the next sequential value (max+1, BR-2),
    /// supplied by the application layer which owns the gap-free counter.
    /// </summary>
    public static Complaint Log(int number, LaboratoryId labId, string category, string viaChannel,
        string? assignedTeam, string details)
    {
        if (number <= 0) throw new DomainException("Complaint number must be positive.");
        if (string.IsNullOrWhiteSpace(category)) throw new DomainException("Complaint category is required.");
        if (string.IsNullOrWhiteSpace(viaChannel)) throw new DomainException("Complaint channel is required.");
        return new Complaint(ComplaintId.New(), number, labId, category.Trim(), viaChannel.Trim(),
            assignedTeam?.Trim(), details ?? string.Empty);
    }

    /// <summary>Optional intake metadata captured on the log form (rep involved, received date/time).</summary>
    public void SetIntake(Guid? representativeId, DateTimeOffset? receivedAt)
    {
        RepresentativeId = representativeId;
        ReceivedAt = receivedAt;
    }

    /// <summary>Validity check: valid continues the flow; invalid short-circuits to RejectedInvalid.</summary>
    public void CheckValidity(bool isValid, string? notes)
    {
        IsValid = isValid;
        ValidityNotes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        Stage = isValid ? ComplaintStage.ValidityChecked : ComplaintStage.RejectedInvalid;
    }

    /// <summary>Investigation notes / root-cause analysis (stage → Investigation).</summary>
    public void RecordInvestigation(string notes)
    {
        if (string.IsNullOrWhiteSpace(notes))
            throw new DomainException("Investigation notes are required.");
        InvestigationNotes = notes.Trim();
        Stage = ComplaintStage.Investigation;
    }

    /// <summary>Business outcome (communication type + summary; stage → BusinessOutcome).</summary>
    public void RecordOutcome(string outcomeType, string? summary)
    {
        if (string.IsNullOrWhiteSpace(outcomeType))
            throw new DomainException("An outcome type is required.");
        OutcomeType = outcomeType.Trim();
        OutcomeSummary = string.IsNullOrWhiteSpace(summary) ? null : summary.Trim();
        Stage = ComplaintStage.BusinessOutcome;
    }

    /// <summary>Resolution summary text shown on the closed complaint.</summary>
    public void SetResolutionSummary(string? summary) =>
        ResolutionSummary = string.IsNullOrWhiteSpace(summary) ? null : summary.Trim();

    /// <summary>Open → InProgress (start investigation).</summary>
    public void Start()
    {
        Status.EnsureCanTransitionTo(ComplaintStatus.InProgress);
        Status = ComplaintStatus.InProgress;
        // Stage stays Logged: acknowledging is an explicit workflow step (reference parity).
    }

    /// <summary>
    /// Resolves the complaint (Open/InProgress → Resolved). When e-signature enforcement is on, the
    /// application layer verifies a bound signature and passes <paramref name="eSignatureSatisfied"/>;
    /// the aggregate refuses to resolve otherwise so the gate cannot be skipped.
    /// </summary>
    public void Resolve(string actor, DateTimeOffset when, bool eSignatureSatisfied = true)
    {
        Status.EnsureCanTransitionTo(ComplaintStatus.Resolved);
        if (!eSignatureSatisfied)
            throw new DomainException("Resolution requires a valid electronic signature bound to the current state.");
        Status = ComplaintStatus.Resolved;
        Stage = ComplaintStage.Resolution;
        ResolvedAt = when;
        ResolvedBy = actor;
        Raise(new ComplaintResolved(Id, LaboratoryId, Reference));
    }

    /// <summary>Resolved/InProgress → Open (reopen or send back).</summary>
    public void Reopen()
    {
        Status.EnsureCanTransitionTo(ComplaintStatus.Open);
        Status = ComplaintStatus.Open;
        ResolvedAt = null;
        ResolvedBy = null;
    }

    /// <summary>Advances the investigation narrative (metadata only — never changes status).</summary>
    public void MoveToStage(ComplaintStage stage) => Stage = stage;
}
