using FollowUp.Domain.Common;
using FollowUp.Domain.Identity;
using FollowUp.Domain.Laboratories;
using FollowUp.Domain.Reference;
using FollowUp.Domain.Representatives;

namespace FollowUp.Domain.Accounting;

// =====================================================================================================================
// Accounting module — the money ledgers behind the five Accounting report pages. Every aggregate is an operator-recorded
// row; nothing here is mirrored from Oracle. Amounts are Money (numeric(18,2)); "Serial" is a DB-generated identity so
// every row has a stable human-friendly sequence number; "Day" is derived from Date at read time and never stored.
// =====================================================================================================================

// ---- Enumerations (persisted by stable Name) ----

/// <summary>Who caused a penalty: the collecting rep, data entry, or the technician.</summary>
public sealed class PenaltyUser : Enumeration
{
    public static readonly PenaltyUser Rep = new(1, nameof(Rep));
    public static readonly PenaltyUser DataEntry = new(2, nameof(DataEntry));
    public static readonly PenaltyUser Technician = new(3, nameof(Technician));
    private PenaltyUser(int id, string name) : base(id, name) { }
}

/// <summary>
/// Why an area is deducted. Transportation is typed; Penalty is suggested from the Penalty Statement (Σ wrong − right for
/// the area's labs over a period); PercentageDeal is suggested from the area's income × its deal percentage.
/// </summary>
public sealed class DeductionReason : Enumeration
{
    public static readonly DeductionReason Transportation = new(1, nameof(Transportation));
    public static readonly DeductionReason Penalty = new(2, nameof(Penalty));
    public static readonly DeductionReason PercentageDeal = new(3, nameof(PercentageDeal));
    private DeductionReason(int id, string name) : base(id, name) { }
}

/// <summary>
/// Where a deduction row came from. <c>Manual</c> rows are typed by an operator. <c>AutoPenalty</c> rows mirror one
/// penalty record (created, refreshed and removed with it). <c>AutoDeal</c> rows are the one-per-area-per-month
/// Percentage Deal deductions the daily automation keeps current for the running month.
/// </summary>
public sealed class DeductionOrigin : Enumeration
{
    public static readonly DeductionOrigin Manual = new(1, nameof(Manual));
    public static readonly DeductionOrigin AutoPenalty = new(2, nameof(AutoPenalty));
    public static readonly DeductionOrigin AutoDeal = new(3, nameof(AutoDeal));
    private DeductionOrigin(int id, string name) : base(id, name) { }
}

/// <summary>A collection made by one rep (Single) or jointly by several (Group).</summary>
public sealed class CollectionType : Enumeration
{
    public static readonly CollectionType Single = new(1, nameof(Single));
    public static readonly CollectionType Group = new(2, nameof(Group));
    private CollectionType(int id, string name) : base(id, name) { }
}

/// <summary>The IBAN a bank collection was paid into — a fixed set of three account identifiers (operator decision).</summary>
public sealed class IbanOption : Enumeration
{
    public static readonly IbanOption Iban12 = new(12, "12");
    public static readonly IbanOption Iban16 = new(16, "16");
    public static readonly IbanOption Iban18 = new(18, "18");
    private IbanOption(int id, string name) : base(id, name) { }
}

// ---- Strongly-typed ids ----

public readonly record struct TreasuryReasonId(Guid Value) { public static TreasuryReasonId New() => new(Guid.NewGuid()); public override string ToString() => Value.ToString(); }
public readonly record struct TreasuryId(Guid Value) { public static TreasuryId New() => new(Guid.NewGuid()); public override string ToString() => Value.ToString(); }
public readonly record struct TreasuryEntryId(Guid Value) { public static TreasuryEntryId New() => new(Guid.NewGuid()); public override string ToString() => Value.ToString(); }
public readonly record struct TreasuryGrantId(Guid Value) { public static TreasuryGrantId New() => new(Guid.NewGuid()); public override string ToString() => Value.ToString(); }
public readonly record struct PenaltyRecordId(Guid Value) { public static PenaltyRecordId New() => new(Guid.NewGuid()); public override string ToString() => Value.ToString(); }
public readonly record struct DeductionId(Guid Value) { public static DeductionId New() => new(Guid.NewGuid()); public override string ToString() => Value.ToString(); }
public readonly record struct CollectionId(Guid Value) { public static CollectionId New() => new(Guid.NewGuid()); public override string ToString() => Value.ToString(); }
public readonly record struct RepIncomeEntryId(Guid Value) { public static RepIncomeEntryId New() => new(Guid.NewGuid()); public override string ToString() => Value.ToString(); }
public readonly record struct RepLabIncomeId(Guid Value) { public static RepLabIncomeId New() => new(Guid.NewGuid()); public override string ToString() => Value.ToString(); }

// ---- Shared guards ----

internal static class AccountingGuards
{
    public static string Required(string? value, string field, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new DomainException($"{field} is required.");
        var v = value.Trim();
        if (v.Length > max) throw new DomainException($"{field} must be at most {max} characters.");
        return v;
    }

    public static string? Optional(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var v = value.Trim();
        if (v.Length > max) throw new DomainException($"Text must be at most {max} characters.");
        return v;
    }

    public static Money NonNegative(decimal amount, string field)
    {
        if (amount < 0) throw new DomainException($"{field} cannot be negative.");
        return new Money(amount);
    }
}

// ---- Treasury configuration ----

/// <summary>A configurable reason for a treasury movement (maintained on the Accounting setup page). Deactivated, never
/// hard-deleted, so historical entries keep resolving their reason.</summary>
public sealed class TreasuryReason : AggregateRoot<TreasuryReasonId>, IAuditable
{
    private TreasuryReason() { } // EF
    private TreasuryReason(TreasuryReasonId id, string name) : base(id) { Name = name; }

    public string Name { get; private set; } = null!;
    public bool IsActive { get; private set; } = true;

    public DateTimeOffset CreatedAt { get; private set; }
    public string CreatedBy { get; private set; } = null!;
    public DateTimeOffset? UpdatedAt { get; private set; }
    public string? UpdatedBy { get; private set; }

    public static TreasuryReason Create(string name) => new(TreasuryReasonId.New(), AccountingGuards.Required(name, "Reason name", 100));
    public void Rename(string name) => Name = AccountingGuards.Required(name, "Reason name", 100);
    public void Activate(bool active) => IsActive = active;
}

/// <summary>
/// A cash treasury. Assigned to one or more branches (the Branch reference names labs and reps already carry), which is
/// also the dimension its rows are org-scoped on: a caller sees a treasury when their scope is wildcard on Branches or
/// covers at least one of its branches. Deactivated, never hard-deleted.
/// </summary>
public sealed class Treasury : AggregateRoot<TreasuryId>, IAuditable
{
    private readonly List<string> _branches = new();

    private Treasury() { } // EF
    private Treasury(TreasuryId id, string name) : base(id) { Name = name; }

    public string Name { get; private set; } = null!;
    public IReadOnlyCollection<string> Branches => _branches.AsReadOnly();
    public bool IsActive { get; private set; } = true;

    public DateTimeOffset CreatedAt { get; private set; }
    public string CreatedBy { get; private set; } = null!;
    public DateTimeOffset? UpdatedAt { get; private set; }
    public string? UpdatedBy { get; private set; }

    public static Treasury Create(string name, IEnumerable<string> branches)
    {
        var t = new Treasury(TreasuryId.New(), AccountingGuards.Required(name, "Treasury name", 100));
        t.SetBranches(branches);
        return t;
    }

    public void Rename(string name) => Name = AccountingGuards.Required(name, "Treasury name", 100);

    public void SetBranches(IEnumerable<string> branches)
    {
        var clean = branches.Where(b => !string.IsNullOrWhiteSpace(b)).Select(b => b.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (clean.Count == 0) throw new DomainException("A treasury must be assigned to at least one branch.");
        _branches.Clear();
        _branches.AddRange(clean);
    }

    public void Activate(bool active) => IsActive = active;
}

/// <summary>Where a treasury entry came from: typed by an operator, or mirrored from a Collection's cash.</summary>
public sealed class TreasuryEntryOrigin : Enumeration
{
    public static readonly TreasuryEntryOrigin Manual = new(1, nameof(Manual));
    public static readonly TreasuryEntryOrigin AutoCollection = new(2, nameof(AutoCollection));
    private TreasuryEntryOrigin(int id, string name) : base(id, name) { }
}

/// <summary>Validation state of a treasury entry. Manual rows need none; a mirrored collection is Pending until a
/// treasury user confirms the cash actually received (possibly correcting the amount), then Validated.</summary>
public sealed class TreasuryValidationStatus : Enumeration
{
    public static readonly TreasuryValidationStatus NotRequired = new(1, nameof(NotRequired));
    public static readonly TreasuryValidationStatus Pending = new(2, nameof(Pending));
    public static readonly TreasuryValidationStatus Validated = new(3, nameof(Validated));
    private TreasuryValidationStatus(int id, string name) : base(id, name) { }
}

/// <summary>
/// One treasury movement. Debit = cash INTO the treasury; Credit = expenses OUT of the lab. An entry is one-sided:
/// exactly one of the two is positive.
/// <para>Two origins (operator decisions, 2026-09-16): <b>Manual</b> rows are typed and carry a configured reason.
/// <b>AutoCollection</b> rows mirror one Collection's cash into the treasury serving the lab's branch — created, refreshed
/// and removed with the collection, no reason (the collection is the reason), the collection's particulars in
/// <see cref="SystemNote"/>. They start <see cref="TreasuryValidationStatus.Pending"/>; a treasury user with the Validate
/// grant confirms the cash received (correcting the debit if it differs), after which only the Update grant may change it.
/// A cash change on the collection re-opens validation.</para>
/// </summary>
public sealed class TreasuryEntry : AggregateRoot<TreasuryEntryId>, IAuditable
{
    private TreasuryEntry() { } // EF
    private TreasuryEntry(TreasuryEntryId id, TreasuryId treasuryId, TreasuryEntryOrigin origin) : base(id) { TreasuryId = treasuryId; Origin = origin; }

    /// <summary>DB-generated sequence number (identity) — the row's stable human-facing "Serial".</summary>
    public long Serial { get; private set; }
    public TreasuryId TreasuryId { get; private set; }
    public DateOnly Date { get; private set; }
    public Money Debit { get; private set; }
    public Money Credit { get; private set; }
    /// <summary>The configured reason of a manual row; null for a mirrored collection.</summary>
    public TreasuryReasonId? ReasonId { get; private set; }
    /// <summary>Operator-written notes. Never modified by the automation.</summary>
    public string? Notes { get; private set; }
    public TreasuryEntryOrigin Origin { get; private set; } = null!;
    /// <summary>The Collection an AutoCollection row mirrors; null for manual rows.</summary>
    public CollectionId? CollectionId { get; private set; }
    /// <summary>The cash the collection recorded when last synced — what the treasury was expected to receive.</summary>
    public Money? CollectedCash { get; private set; }
    /// <summary>System-written details of the mirrored collection.</summary>
    public string? SystemNote { get; private set; }
    public TreasuryValidationStatus ValidationStatus { get; private set; } = null!;
    public DateTimeOffset? ValidatedAt { get; private set; }
    public string? ValidatedBy { get; private set; }
    public string? ValidationNote { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public string CreatedBy { get; private set; } = null!;
    public DateTimeOffset? UpdatedAt { get; private set; }
    public string? UpdatedBy { get; private set; }

    // ---- Manual ----

    public static TreasuryEntry Create(TreasuryId treasuryId, DateOnly date, decimal debit, decimal credit, TreasuryReasonId reasonId, string? notes)
    {
        var e = new TreasuryEntry(TreasuryEntryId.New(), treasuryId, TreasuryEntryOrigin.Manual) { ValidationStatus = TreasuryValidationStatus.NotRequired };
        e.Update(date, debit, credit, reasonId, notes);
        return e;
    }

    /// <summary>Full edit of a manual row. Mirrored collections are changed through <see cref="Validate"/> / <see cref="AdjustValidated"/>.</summary>
    public void Update(DateOnly date, decimal debit, decimal credit, TreasuryReasonId reasonId, string? notes)
    {
        if (Origin != TreasuryEntryOrigin.Manual)
            throw new DomainException("A collection's treasury entry is validated or adjusted, not edited; its date and amount follow the collection.");
        var d = AccountingGuards.NonNegative(debit, "Debit");
        var c = AccountingGuards.NonNegative(credit, "Credit");
        if ((d.Amount > 0) == (c.Amount > 0))
            throw new DomainException("A treasury entry is either a debit (cash in) or a credit (expense out) — exactly one must be greater than zero.");
        Date = date; Debit = d; Credit = c; ReasonId = reasonId; Notes = AccountingGuards.Optional(notes, 500);
    }

    // ---- AutoCollection ----

    /// <summary>Mirrors a collection's cash as a pending debit of the treasury serving the collecting rep's branch.</summary>
    /// <param name="repNames">Display names of the collection's reps, in <see cref="Collection.Shares"/> order.</param>
    public static TreasuryEntry FromCollection(TreasuryId treasuryId, Collection collection, IEnumerable<string> repNames)
    {
        if (collection.Cash.Amount <= 0) throw new DomainException("Only a collection with a cash amount is mirrored into a treasury.");
        var e = new TreasuryEntry(TreasuryEntryId.New(), treasuryId, TreasuryEntryOrigin.AutoCollection)
        { CollectionId = collection.Id, ValidationStatus = TreasuryValidationStatus.Pending, Credit = Money.Zero };
        e.RefreshFromCollection(collection, repNames);
        return e;
    }

    /// <summary>
    /// Re-syncs after the collection changed. Date and details always follow the collection. The debit follows the
    /// collected cash while the row is Pending; a validated row keeps the amount the treasury confirmed unless the
    /// collected cash itself changed — then validation is re-opened (Pending) and the debit resets to the new cash.
    /// Operator notes are kept.
    /// </summary>
    public void RefreshFromCollection(Collection collection, IEnumerable<string> repNames)
    {
        if (Origin != TreasuryEntryOrigin.AutoCollection || CollectionId != collection.Id)
            throw new DomainException("This treasury entry does not mirror that collection.");
        if (collection.Cash.Amount <= 0) throw new DomainException("Only a collection with a cash amount is mirrored into a treasury.");
        var cashChanged = CollectedCash is null || CollectedCash.Value.Amount != collection.Cash.Amount;
        Date = collection.Date;
        CollectedCash = collection.Cash;
        // "Name 600.00, Other 400.00" for a group; just the name for a single collection (the whole amount is theirs).
        var names = repNames.ToList();
        var reps = string.Join(", ", collection.Shares.Select((s, i) =>
        {
            var name = i < names.Count && !string.IsNullOrWhiteSpace(names[i]) ? names[i] : "—";
            return collection.Type == CollectionType.Group ? $"{name} {s.Amount.Amount:0.00}" : name;
        }));
        SystemNote = AccountingGuards.Optional(
            $"Collection · {collection.Date:dd/MM/yyyy} · {collection.Type.Name} · rep(s) {reps} · cash {collection.Cash.Amount:0.00}"
            + (collection.Bank.Amount > 0 ? $" (bank {collection.Bank.Amount:0.00} not in treasury)" : "")
            + (string.IsNullOrWhiteSpace(collection.DoneBy) ? "" : $" · done by {collection.DoneBy}"), 1000);
        if (ValidationStatus == TreasuryValidationStatus.Pending || cashChanged)
        {
            Debit = collection.Cash;
            if (ValidationStatus == TreasuryValidationStatus.Validated)
            {
                ValidationStatus = TreasuryValidationStatus.Pending; // the confirmed amount no longer matches what was collected
                ValidatedAt = null; ValidatedBy = null;
            }
        }
    }

    /// <summary>The treasury confirms the cash actually received — the collected amount, or a corrected one.</summary>
    public void Validate(decimal receivedAmount, string? note, string validatedBy, DateTimeOffset at)
    {
        if (Origin != TreasuryEntryOrigin.AutoCollection) throw new DomainException("Only a collection's treasury entry is validated.");
        if (ValidationStatus == TreasuryValidationStatus.Validated) throw new DomainException("This entry is already validated; use an update to change it.");
        var received = AccountingGuards.NonNegative(receivedAmount, "Received amount");
        if (received.Amount <= 0) throw new DomainException("The received amount must be greater than zero.");
        Debit = received;
        ValidationStatus = TreasuryValidationStatus.Validated;
        ValidatedAt = at;
        ValidatedBy = AccountingGuards.Required(validatedBy, "Validated by", 100);
        ValidationNote = AccountingGuards.Optional(note, 500);
    }

    /// <summary>Post-validation correction of a mirrored entry (the Update grant): amount and operator notes only.</summary>
    public void AdjustValidated(decimal amount, string? notes)
    {
        if (Origin != TreasuryEntryOrigin.AutoCollection) throw new DomainException("Use Update for a manual treasury entry.");
        if (ValidationStatus != TreasuryValidationStatus.Validated) throw new DomainException("Validate the entry before adjusting it.");
        var a = AccountingGuards.NonNegative(amount, "Amount");
        if (a.Amount <= 0) throw new DomainException("The amount must be greater than zero.");
        Debit = a;
        Notes = AccountingGuards.Optional(notes, 500);
    }

    /// <summary>The confirmed amount differs from what the collection recorded.</summary>
    public bool HasDiscrepancy => Origin == TreasuryEntryOrigin.AutoCollection && CollectedCash is { } c && c.Amount != Debit.Amount;
}

/// <summary>
/// A role's rights on one treasury (operator decision, 2026-09-16): View its account, Validate its mirrored collections,
/// Update its entries after validation (and record / edit / delete its manual entries). Validate and Update imply View.
/// The built-in administrator role holds every right on every treasury without rows here. No row = no access.
/// </summary>
public sealed class TreasuryGrant : AggregateRoot<TreasuryGrantId>, IAuditable
{
    private TreasuryGrant() { } // EF
    private TreasuryGrant(TreasuryGrantId id, RoleId roleId, TreasuryId treasuryId) : base(id) { RoleId = roleId; TreasuryId = treasuryId; }
    public RoleId RoleId { get; private set; }
    public TreasuryId TreasuryId { get; private set; }
    public bool CanView { get; private set; }
    public bool CanValidate { get; private set; }
    public bool CanUpdate { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public string CreatedBy { get; private set; } = null!;
    public DateTimeOffset? UpdatedAt { get; private set; }
    public string? UpdatedBy { get; private set; }

    public static TreasuryGrant Create(RoleId roleId, TreasuryId treasuryId, bool view, bool validate, bool update)
    {
        var g = new TreasuryGrant(TreasuryGrantId.New(), roleId, treasuryId);
        g.Set(view, validate, update);
        return g;
    }

    public void Set(bool view, bool validate, bool update)
    {
        CanView = view || validate || update; // the finer rights need the page
        CanValidate = validate;
        CanUpdate = update;
    }

    public bool IsEmpty => !CanView && !CanValidate && !CanUpdate;

    /// <summary>A requested set of rights, normalised the same way the aggregate stores them.</summary>
    public readonly record struct Rights(bool View, bool Validate, bool Update)
    {
        public bool IsEmpty => !View && !Validate && !Update;
    }
}

// ---- Penalty Statement ----

/// <summary>
/// A penalty recorded against a lab: a wrong test was booked in place of the right one. The penalty amount is the
/// over/under-charge <c>WrongValue − RightValue</c> (operator decision) — derived, never stored — and is what the
/// Deductions report sums per area.
/// </summary>
public sealed class PenaltyRecord : AggregateRoot<PenaltyRecordId>, IAuditable
{
    private PenaltyRecord() { } // EF
    private PenaltyRecord(PenaltyRecordId id, LaboratoryId labId) : base(id) { LaboratoryId = labId; }

    public long Serial { get; private set; }
    public DateOnly Date { get; private set; }
    public LaboratoryId LaboratoryId { get; private set; }
    public string AccNo { get; private set; } = null!;
    public string PatientName { get; private set; } = null!;
    public string WrongTestCode { get; private set; } = null!;
    public string WrongTestName { get; private set; } = null!;
    public Money WrongValue { get; private set; }
    public string RightTestCode { get; private set; } = null!;
    public string RightTestName { get; private set; } = null!;
    public Money RightValue { get; private set; }
    /// <summary>Who made the error, by kind: a representative, a data-entry user or a technician.</summary>
    public PenaltyUser UserType { get; private set; } = null!;
    /// <summary>The system user who made the error — set exactly when <see cref="UserType"/> is DataEntry or Technician.</summary>
    public AppUserId? PerformedByUserId { get; private set; }
    /// <summary>The representative who made the error — set exactly when <see cref="UserType"/> is Rep.</summary>
    public RepresentativeId? PerformedByRepId { get; private set; }

    /// <summary>The penalty = wrong − right (may be negative when the right test was the dearer one).</summary>
    public Money PenaltyAmount => WrongValue - RightValue;

    public DateTimeOffset CreatedAt { get; private set; }
    public string CreatedBy { get; private set; } = null!;
    public DateTimeOffset? UpdatedAt { get; private set; }
    public string? UpdatedBy { get; private set; }

    public static PenaltyRecord Create(LaboratoryId labId, DateOnly date, string accNo, string patientName,
        string wrongTestCode, string wrongTestName, decimal wrongValue,
        string rightTestCode, string rightTestName, decimal rightValue,
        PenaltyUser userType, AppUserId? performedByUserId, RepresentativeId? performedByRepId)
    {
        var p = new PenaltyRecord(PenaltyRecordId.New(), labId);
        p.Update(date, accNo, patientName, wrongTestCode, wrongTestName, wrongValue, rightTestCode, rightTestName, rightValue,
            userType, performedByUserId, performedByRepId);
        return p;
    }

    public void Update(DateOnly date, string accNo, string patientName,
        string wrongTestCode, string wrongTestName, decimal wrongValue,
        string rightTestCode, string rightTestName, decimal rightValue,
        PenaltyUser userType, AppUserId? performedByUserId, RepresentativeId? performedByRepId)
    {
        Date = date;
        AccNo = AccountingGuards.Required(accNo, "Acc No", 50);
        PatientName = AccountingGuards.Required(patientName, "Patient name", 200);
        WrongTestCode = AccountingGuards.Required(wrongTestCode, "Wrong test", 32);
        WrongTestName = AccountingGuards.Required(wrongTestName, "Wrong test name", 200);
        WrongValue = AccountingGuards.NonNegative(wrongValue, "Wrong test value");
        RightTestCode = AccountingGuards.Required(rightTestCode, "Right test", 32);
        RightTestName = AccountingGuards.Required(rightTestName, "Right test name", 200);
        RightValue = AccountingGuards.NonNegative(rightValue, "Right test value");
        UserType = userType ?? throw new DomainException("The user type is required.");

        // Exactly one "performed by" link, and it must match the user type: a Rep penalty names a representative,
        // a DataEntry / Technician penalty names a system user. Mirrored by ck_penalty_record_performed_by in the DB.
        if (userType == PenaltyUser.Rep)
        {
            if (performedByRepId is null) throw new DomainException("Select the representative who made the error.");
            if (performedByUserId is not null) throw new DomainException("A representative penalty cannot also name a system user.");
        }
        else
        {
            if (performedByUserId is null) throw new DomainException("Select the system user who made the error.");
            if (performedByRepId is not null) throw new DomainException("A data-entry / technician penalty cannot also name a representative.");
        }
        PerformedByUserId = performedByUserId;
        PerformedByRepId = performedByRepId;
    }
}

// ---- Deductions ----

/// <summary>
/// A deduction applied to an area. Three origins (operator decisions, 2026-09-15):
/// <list type="bullet">
/// <item><b>Manual</b> — typed by an operator; for Penalty / PercentageDeal reasons a value may be suggested by the server
/// and stays editable. The period a suggestion covered is stored for the audit trail.</item>
/// <item><b>AutoPenalty</b> — mirrors one penalty record: created when the penalty is recorded, refreshed when it is
/// edited, removed when it is deleted. <see cref="SystemNote"/> carries the penalty details for investigation.</item>
/// <item><b>AutoDeal</b> — the single Percentage Deal deduction per area per month, recalculated daily by the automation
/// for the running month (income to date × the area's deal %). A month that has ended is never recalculated
/// automatically; an operator may still edit it or press "Suggest value", which marks it <see cref="IsAdjusted"/>.</item>
/// </list>
/// Operator notes (<see cref="Notes"/>) are never touched by the automation; system text lives in <see cref="SystemNote"/>.
/// </summary>
public sealed class Deduction : AggregateRoot<DeductionId>, IAuditable
{
    private Deduction() { } // EF
    private Deduction(DeductionId id, AreaId areaId, DeductionOrigin origin) : base(id) { AreaId = areaId; Origin = origin; }

    public long Serial { get; private set; }
    public DateOnly Date { get; private set; }
    public AreaId AreaId { get; private set; }
    public DeductionReason Reason { get; private set; } = null!;
    public Money Value { get; private set; }
    /// <summary>Operator-written notes. Never modified by the automation.</summary>
    public string? Notes { get; private set; }
    /// <summary>The period a suggested / automated value was computed over; null for typed values.</summary>
    public DateOnly? PeriodFrom { get; private set; }
    public DateOnly? PeriodTo { get; private set; }
    public DeductionOrigin Origin { get; private set; } = null!;
    /// <summary>An automated row whose value an operator changed (typed or via "Suggest value"). The automation leaves it alone.</summary>
    public bool IsAdjusted { get; private set; }
    /// <summary>System-written details: the mirrored penalty's particulars, or the deal calculation basis.</summary>
    public string? SystemNote { get; private set; }
    /// <summary>The penalty an AutoPenalty row mirrors; null for the other origins.</summary>
    public PenaltyRecordId? PenaltyRecordId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public string CreatedBy { get; private set; } = null!;
    public DateTimeOffset? UpdatedAt { get; private set; }
    public string? UpdatedBy { get; private set; }

    // ---- Manual ----

    public static Deduction Create(AreaId areaId, DateOnly date, DeductionReason reason, decimal value, string? notes, DateOnly? periodFrom, DateOnly? periodTo)
    {
        var d = new Deduction(DeductionId.New(), areaId, DeductionOrigin.Manual);
        d.Update(date, reason, value, notes, periodFrom, periodTo);
        return d;
    }

    /// <summary>Full edit of a manual row. Automated rows only accept <see cref="Adjust"/>.</summary>
    public void Update(DateOnly date, DeductionReason reason, decimal value, string? notes, DateOnly? periodFrom, DateOnly? periodTo)
    {
        if (Origin != DeductionOrigin.Manual)
            throw new DomainException("An automated deduction keeps its area, reason and period; only its value and notes can be adjusted.");
        ValidatePeriod(periodFrom, periodTo);
        Date = date;
        Reason = reason ?? throw new DomainException("A deduction reason is required.");
        Value = AccountingGuards.NonNegative(value, "Deduction value");
        Notes = AccountingGuards.Optional(notes, 500);
        PeriodFrom = periodFrom; PeriodTo = periodTo;
    }

    // ---- AutoPenalty ----

    /// <summary>
    /// Mirrors a penalty as a deduction of its lab's area. The deduction value is the penalty (wrong − right) floored at
    /// zero — an under-charge is not deducted from the area — while the signed amount is kept in the system note.
    /// </summary>
    public static Deduction FromPenalty(AreaId areaId, PenaltyRecord penalty, string labName)
    {
        var d = new Deduction(DeductionId.New(), areaId, DeductionOrigin.AutoPenalty) { PenaltyRecordId = penalty.Id, Reason = DeductionReason.Penalty };
        d.RefreshFromPenalty(penalty, labName);
        return d;
    }

    /// <summary>Re-syncs an AutoPenalty row after its penalty changed. The penalty is the source of truth, so a prior
    /// operator adjustment of the value is superseded; operator notes are kept.</summary>
    public void RefreshFromPenalty(PenaltyRecord penalty, string labName)
    {
        if (Origin != DeductionOrigin.AutoPenalty || PenaltyRecordId != penalty.Id)
            throw new DomainException("This deduction does not mirror that penalty.");
        Date = penalty.Date;
        PeriodFrom = penalty.Date; PeriodTo = penalty.Date;
        var signed = penalty.PenaltyAmount.Amount;
        Value = new Money(Math.Max(0m, signed));
        IsAdjusted = false;
        SystemNote = AccountingGuards.Optional(
            $"Penalty · {labName} · Acc {penalty.AccNo} · {penalty.PatientName} · wrong {penalty.WrongTestName} ({penalty.WrongTestCode}) {penalty.WrongValue.Amount:0.00} → right {penalty.RightTestName} ({penalty.RightTestCode}) {penalty.RightValue.Amount:0.00} · penalty {signed:0.00}"
            + (signed < 0 ? " (under-charge: nothing deducted)" : ""), 1000);
    }

    // ---- AutoDeal ----

    /// <summary>The month's single Percentage Deal deduction for an area: dated on the 1st, period = 1st → the day the
    /// income was calculated through.</summary>
    public static Deduction AutoDeal(AreaId areaId, YearMonth month, DateOnly through, decimal value, string basis)
    {
        var first = new DateOnly(month.Year, month.Month, 1);
        var d = new Deduction(DeductionId.New(), areaId, DeductionOrigin.AutoDeal) { Reason = DeductionReason.PercentageDeal, Date = first, PeriodFrom = first };
        d.Recalculate(through, value, basis);
        return d;
    }

    /// <summary>Daily recalculation of an AutoDeal row. Refused once an operator has adjusted it.</summary>
    public void Recalculate(DateOnly through, decimal value, string basis)
    {
        if (Origin != DeductionOrigin.AutoDeal) throw new DomainException("Only automated Percentage Deal deductions are recalculated.");
        if (IsAdjusted) throw new DomainException("A manually adjusted deduction is not recalculated automatically.");
        if (through < PeriodFrom!.Value) throw new DomainException("The calculation date precedes the deduction month.");
        PeriodTo = through;
        Value = AccountingGuards.NonNegative(value, "Deduction value");
        SystemNote = AccountingGuards.Optional(basis, 1000);
    }

    // ---- Operator edit of an automated row ----

    /// <summary>
    /// Operator edit of an automated row: value and notes only. A changed value marks the row manually adjusted (and,
    /// for AutoDeal, stops the daily recalculation); the optional <paramref name="basis"/> is what "Suggest value"
    /// computed and replaces the system note. Operator notes are the operator's — they are stored as given.
    /// </summary>
    public void Adjust(decimal value, string? notes, string? basis)
    {
        if (Origin == DeductionOrigin.Manual) throw new DomainException("Use Update for a manual deduction.");
        var newValue = AccountingGuards.NonNegative(value, "Deduction value");
        if (newValue != Value) IsAdjusted = true;
        Value = newValue;
        Notes = AccountingGuards.Optional(notes, 500);
        if (!string.IsNullOrWhiteSpace(basis)) SystemNote = AccountingGuards.Optional(basis, 1000);
    }

    private static void ValidatePeriod(DateOnly? periodFrom, DateOnly? periodTo)
    {
        if (periodFrom is { } f && periodTo is { } t && t < f) throw new DomainException("The period end must not precede its start.");
        if ((periodFrom is null) != (periodTo is null)) throw new DomainException("A period needs both a start and an end.");
    }
}

// ---- Collection ----

/// <summary>One rep's part of a collection: the amount (cash + bank) that rep handed in. Persisted as jsonb on the collection.</summary>
public readonly record struct CollectionShare(RepresentativeId RepId, Money Amount);

/// <summary>
/// Money collected by one rep (Single) or several (Group) and handed in, split into Cash and Bank. A collection is the
/// rep's act, not a lab's (a Lab Responsible collects from many labs), so it carries no lab; each rep's share is recorded
/// and, for a group, the shares must add up exactly to Cash + Bank. Only <c>LabResponsible</c> reps collect — enforced
/// by the application layer, which also knows the reps' types.
/// </summary>
public sealed class Collection : AggregateRoot<CollectionId>, IAuditable
{
    private readonly List<CollectionShare> _shares = new();

    private Collection() { } // EF
    private Collection(CollectionId id) : base(id) { }

    public long Serial { get; private set; }
    public DateOnly Date { get; private set; }
    public CollectionType Type { get; private set; } = null!;
    /// <summary>Per-rep amounts, in the order entered. Σ = <see cref="Total"/>.</summary>
    public IReadOnlyCollection<CollectionShare> Shares => _shares.AsReadOnly();
    /// <summary>The reps on this collection, in the order entered (derived from <see cref="Shares"/>).</summary>
    public IReadOnlyList<RepresentativeId> RepIds => _shares.Select(s => s.RepId).ToList();
    public Money Cash { get; private set; }
    public Money Bank { get; private set; }
    public IbanOption? Iban { get; private set; }
    public string? DoneBy { get; private set; }
    public string? Notes { get; private set; }

    public Money Total => Cash + Bank;

    public DateTimeOffset CreatedAt { get; private set; }
    public string CreatedBy { get; private set; } = null!;
    public DateTimeOffset? UpdatedAt { get; private set; }
    public string? UpdatedBy { get; private set; }

    /// <summary>The amount this rep handed in on this collection; zero when the rep is not on it.</summary>
    public Money ShareOf(RepresentativeId repId) => _shares.Where(s => s.RepId == repId).Select(s => s.Amount).FirstOrDefault(Money.Zero);

    /// <param name="shares">(rep, amount) pairs. For a Single collection the one amount is taken as the total whatever was
    /// passed; for a Group each amount must be positive and they must sum to cash + bank.</param>
    public static Collection Create(DateOnly date, CollectionType type, IEnumerable<(RepresentativeId RepId, decimal Amount)> shares,
        decimal cash, decimal bank, IbanOption? iban, string? doneBy, string? notes)
    {
        var c = new Collection(CollectionId.New());
        c.Update(date, type, shares, cash, bank, iban, doneBy, notes);
        return c;
    }

    public void Update(DateOnly date, CollectionType type, IEnumerable<(RepresentativeId RepId, decimal Amount)> shares,
        decimal cash, decimal bank, IbanOption? iban, string? doneBy, string? notes)
    {
        var list = shares.ToList();
        Type = type ?? throw new DomainException("A collection type is required.");
        if (list.Count == 0) throw new DomainException("A collection names at least one rep.");
        if (list.Select(s => s.RepId).Distinct().Count() != list.Count) throw new DomainException("A rep appears once on a collection.");
        if (type == CollectionType.Single && list.Count != 1) throw new DomainException("A single collection names exactly one rep.");
        if (type == CollectionType.Group && list.Count < 2) throw new DomainException("A group collection names at least two reps.");

        var c = AccountingGuards.NonNegative(cash, "Cash");
        var b = AccountingGuards.NonNegative(bank, "Bank");
        var total = c + b;
        if (total.Amount <= 0) throw new DomainException("A collection must carry a cash or bank amount.");
        if (b.Amount > 0 && iban is null) throw new DomainException("A bank amount requires the IBAN it was paid into.");

        List<CollectionShare> resolved;
        if (type == CollectionType.Single)
            resolved = new List<CollectionShare> { new(list[0].RepId, total) }; // one rep → the whole amount is theirs
        else
        {
            if (list.Any(s => s.Amount <= 0)) throw new DomainException("Each rep's collected amount must be greater than zero.");
            var sum = list.Sum(s => s.Amount);
            if (sum != total.Amount)
                throw new DomainException($"The reps' amounts ({sum:0.00}) must add up to cash + bank ({total.Amount:0.00}).");
            resolved = list.Select(s => new CollectionShare(s.RepId, new Money(s.Amount))).ToList();
        }

        Date = date; Cash = c; Bank = b;
        Iban = b.Amount > 0 ? iban : null; // no bank amount → no IBAN, so a stale account can never linger
        DoneBy = AccountingGuards.Optional(doneBy, 200);
        Notes = AccountingGuards.Optional(notes, 500);
        _shares.Clear();
        _shares.AddRange(resolved);
    }
}

// ---- Rep Statement (manual real income) ----

/// <summary>
/// A manually recorded "real income" line for a rep — sits beside the Oracle-derived income (the income of the labs the rep
/// collects for) on the Debit side of the Rep Statement; collections form the Credit side.
/// </summary>
public sealed class RepIncomeEntry : AggregateRoot<RepIncomeEntryId>, IAuditable
{
    private RepIncomeEntry() { } // EF
    private RepIncomeEntry(RepIncomeEntryId id, RepresentativeId repId) : base(id) { RepresentativeId = repId; }

    public long Serial { get; private set; }
    public DateOnly Date { get; private set; }
    public RepresentativeId RepresentativeId { get; private set; }
    public Money Amount { get; private set; }
    public string? Notes { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public string CreatedBy { get; private set; } = null!;
    public DateTimeOffset? UpdatedAt { get; private set; }
    public string? UpdatedBy { get; private set; }

    public static RepIncomeEntry Create(RepresentativeId repId, DateOnly date, decimal amount, string? notes)
    {
        if (amount <= 0) throw new DomainException("The income amount must be greater than zero.");
        return new RepIncomeEntry(RepIncomeEntryId.New(), repId) { Date = date, Amount = new Money(amount), Notes = AccountingGuards.Optional(notes, 500) };
    }
}

// ---- Rep Statement: the Lab Responsible's daily real-income sheet ----

/// <summary>
/// One line of a Lab Responsible's daily "real income" sheet (operator decision, 2026-09-16): what the rep reports for
/// ONE lab on ONE date — samples, the amount the lab had to pay (<see cref="TotalRequired"/>), what it actually paid
/// (<see cref="Paid"/>), what it paid against earlier days' remaining (<see cref="DelayedPayment"/>) and notes.
/// <see cref="Remaining"/> = TotalRequired − Paid carries to later days as that lab's "remaining for previous data"
/// (Σ remaining − Σ delayed payments of earlier lines). The rep's real income for a date = Σ(Paid + DelayedPayment).
/// One line per (rep, lab, date); an all-zero line with no notes is not worth storing (<see cref="IsEmpty"/>).
/// </summary>
public sealed class RepLabIncome : AggregateRoot<RepLabIncomeId>, IAuditable
{
    private RepLabIncome() { } // EF
    private RepLabIncome(RepLabIncomeId id, RepresentativeId repId, LaboratoryId labId, DateOnly date) : base(id)
    { RepresentativeId = repId; LaboratoryId = labId; Date = date; }

    public long Serial { get; private set; }
    public DateOnly Date { get; private set; }
    public RepresentativeId RepresentativeId { get; private set; }
    public LaboratoryId LaboratoryId { get; private set; }
    public int Samples { get; private set; }
    public Money TotalRequired { get; private set; }
    public Money Paid { get; private set; }
    public Money DelayedPayment { get; private set; }
    public string? Notes { get; private set; }

    public Money Remaining => TotalRequired - Paid;
    public bool IsEmpty => Samples == 0 && TotalRequired.Amount == 0 && Paid.Amount == 0 && DelayedPayment.Amount == 0 && Notes is null;

    public DateTimeOffset CreatedAt { get; private set; }
    public string CreatedBy { get; private set; } = null!;
    public DateTimeOffset? UpdatedAt { get; private set; }
    public string? UpdatedBy { get; private set; }

    public static RepLabIncome Create(RepresentativeId repId, LaboratoryId labId, DateOnly date, int samples, decimal totalRequired, decimal paid, decimal delayedPayment, string? notes)
    {
        var e = new RepLabIncome(RepLabIncomeId.New(), repId, labId, date);
        e.Update(samples, totalRequired, paid, delayedPayment, notes);
        return e;
    }

    public void Update(int samples, decimal totalRequired, decimal paid, decimal delayedPayment, string? notes)
    {
        if (samples < 0) throw new DomainException("Samples cannot be negative.");
        var required = AccountingGuards.NonNegative(totalRequired, "Total required");
        var p = AccountingGuards.NonNegative(paid, "Paid");
        var d = AccountingGuards.NonNegative(delayedPayment, "Delayed payment");
        if (p > required) throw new DomainException("Paid cannot exceed the total required; record the excess as a delayed payment.");
        Samples = samples; TotalRequired = required; Paid = p; DelayedPayment = d;
        Notes = AccountingGuards.Optional(notes, 500);
    }
}
