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
public sealed class Complaint : AggregateRoot<ComplaintId>, IVersioned, IAuditable
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
    /// <summary>Optimistic-concurrency token (Postgres xmin); concurrent workflow edits conflict (409). Finding CMP-6.</summary>
    public uint RowVersion { get; private set; }

    /// <summary>
    /// Monotonic content version (SIG-4): incremented on every change to a field covered by the signature
    /// content hash, so a material edit — even one later reverted (A→B→A) — always yields a strictly higher
    /// version and can never resurrect a signature bound to an earlier state (SRS line 322: a material change
    /// creates a new version). Distinct from <see cref="RowVersion"/> (xmin), which bumps on ANY update
    /// including audit-only saves and so would over-invalidate. Starts at 1 when the complaint is logged.
    /// </summary>
    public uint ContentVersion { get; private set; } = 1;

    private void BumpContentVersion() => ContentVersion++;

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
        BumpContentVersion();
    }

    /// <summary>Validity check: valid continues the flow; invalid closes the complaint immediately (CMP-21).</summary>
    public void CheckValidity(bool isValid, string? notes, string actor, DateTimeOffset when)
    {
        EnsureNotResolved();
        IsValid = isValid;
        ValidityNotes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        if (isValid)
        {
            Stage = ComplaintStage.ValidityChecked;
        }
        else
        {
            // CMP-21: an invalid complaint is closed on the spot — it needs no resolution e-signature and must not
            // keep inflating the Open KPIs. Modeled explicitly as a resolution into the RejectedInvalid stage.
            Status.EnsureCanTransitionTo(ComplaintStatus.Resolved);
            Stage = ComplaintStage.RejectedInvalid;
            Status = ComplaintStatus.Resolved;
            ResolvedAt = when;
            ResolvedBy = actor;
            Raise(new ComplaintResolved(Id, LaboratoryId, Reference));
        }
        BumpContentVersion();
    }

    /// <summary>Investigation notes / root-cause analysis (stage → Investigation).</summary>
    public void RecordInvestigation(string notes)
    {
        EnsureNotResolved();
        if (string.IsNullOrWhiteSpace(notes))
            throw new DomainException("Investigation notes are required.");
        InvestigationNotes = notes.Trim();
        Stage = ComplaintStage.Investigation;
        BumpContentVersion();
    }

    /// <summary>Business outcome (communication type + summary; stage → BusinessOutcome).</summary>
    public void RecordOutcome(string outcomeType, string? summary)
    {
        EnsureNotResolved();
        if (string.IsNullOrWhiteSpace(outcomeType))
            throw new DomainException("An outcome type is required.");
        OutcomeType = outcomeType.Trim();
        OutcomeSummary = string.IsNullOrWhiteSpace(summary) ? null : summary.Trim();
        Stage = ComplaintStage.BusinessOutcome;
        BumpContentVersion();
    }

    /// <summary>Resolution summary text shown on the closed complaint.</summary>
    public void SetResolutionSummary(string? summary)
    {
        ResolutionSummary = string.IsNullOrWhiteSpace(summary) ? null : summary.Trim();
        BumpContentVersion();
    }

    /// <summary>Open → InProgress (start investigation).</summary>
    public void Start()
    {
        Status.EnsureCanTransitionTo(ComplaintStatus.InProgress);
        Status = ComplaintStatus.InProgress;
        // Stage stays Logged: acknowledging is an explicit workflow step (reference parity).
        BumpContentVersion();
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
        BumpContentVersion();
        Raise(new ComplaintResolved(Id, LaboratoryId, Reference));
    }

    /// <summary>Resolved/InProgress → Open (reopen or send back).</summary>
    public void Reopen()
    {
        Status.EnsureCanTransitionTo(ComplaintStatus.Open);
        Status = ComplaintStatus.Open;
        ResolvedAt = null;
        ResolvedBy = null;
        // CMP-20: return to Investigation so the reopened complaint can flow forward again instead of dead-ending
        // at Resolution; the resolution summary is deliberately kept as an audit-trail record of the prior close.
        Stage = ComplaintStage.Investigation;
        BumpContentVersion();
    }

    /// <summary>
    /// Advances the investigation-narrative stage (metadata only — never changes status). The two terminal
    /// stages are reached solely through their gated operations: <see cref="Resolution"/> via
    /// <see cref="Resolve"/> (status machine + optional e-signature gate + ResolveComplaints privilege) and
    /// <see cref="RejectedInvalid"/> via <see cref="CheckValidity"/>. A bare stage move to either — and any
    /// stage edit after resolution — is refused with a 409-mapped exception, so the resolve/e-signature gate
    /// cannot be bypassed through the stage field (finding CMP-2, SRS FR-11 CMP-STAGE consistency rule).
    /// </summary>
    public void MoveToStage(ComplaintStage stage)
    {
        EnsureNotResolved();
        if (stage == ComplaintStage.Resolution || stage == ComplaintStage.RejectedInvalid)
            throw new IllegalStateTransitionException(nameof(ComplaintStage), Stage.Name, stage.Name);
        Stage = stage;
        BumpContentVersion();
    }

    /// <summary>A resolved complaint is closed: its investigation narrative is frozen (finding CMP-2).</summary>
    private void EnsureNotResolved()
    {
        if (Status == ComplaintStatus.Resolved)
            throw new IllegalStateTransitionException(nameof(Complaint), ComplaintStatus.Resolved.Name, "edit stage");
    }
}
