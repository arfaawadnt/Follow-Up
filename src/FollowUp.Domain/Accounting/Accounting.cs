using FollowUp.Domain.Common;
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
public readonly record struct PenaltyRecordId(Guid Value) { public static PenaltyRecordId New() => new(Guid.NewGuid()); public override string ToString() => Value.ToString(); }
public readonly record struct DeductionId(Guid Value) { public static DeductionId New() => new(Guid.NewGuid()); public override string ToString() => Value.ToString(); }
public readonly record struct CollectionId(Guid Value) { public static CollectionId New() => new(Guid.NewGuid()); public override string ToString() => Value.ToString(); }
public readonly record struct RepIncomeEntryId(Guid Value) { public static RepIncomeEntryId New() => new(Guid.NewGuid()); public override string ToString() => Value.ToString(); }

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

/// <summary>
/// One treasury movement. Debit = cash INTO the treasury (دخول نقدي للخزينة); Credit = expenses OUT of the lab
/// (خروج مصروفات من المعمل). An entry is one-sided: exactly one of the two is positive.
/// </summary>
public sealed class TreasuryEntry : AggregateRoot<TreasuryEntryId>, IAuditable
{
    private TreasuryEntry() { } // EF
    private TreasuryEntry(TreasuryEntryId id, TreasuryId treasuryId) : base(id) { TreasuryId = treasuryId; }

    /// <summary>DB-generated sequence number (identity) — the row's stable human-facing "Serial".</summary>
    public long Serial { get; private set; }
    public TreasuryId TreasuryId { get; private set; }
    public DateOnly Date { get; private set; }
    public Money Debit { get; private set; }
    public Money Credit { get; private set; }
    public TreasuryReasonId ReasonId { get; private set; }
    public string? Notes { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public string CreatedBy { get; private set; } = null!;
    public DateTimeOffset? UpdatedAt { get; private set; }
    public string? UpdatedBy { get; private set; }

    public static TreasuryEntry Create(TreasuryId treasuryId, DateOnly date, decimal debit, decimal credit, TreasuryReasonId reasonId, string? notes)
    {
        var e = new TreasuryEntry(TreasuryEntryId.New(), treasuryId);
        e.Update(date, debit, credit, reasonId, notes);
        return e;
    }

    public void Update(DateOnly date, decimal debit, decimal credit, TreasuryReasonId reasonId, string? notes)
    {
        var d = AccountingGuards.NonNegative(debit, "Debit");
        var c = AccountingGuards.NonNegative(credit, "Credit");
        if ((d.Amount > 0) == (c.Amount > 0))
            throw new DomainException("A treasury entry is either a debit (cash in) or a credit (expense out) — exactly one must be greater than zero.");
        Date = date; Debit = d; Credit = c; ReasonId = reasonId; Notes = AccountingGuards.Optional(notes, 500);
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
    public PenaltyUser User { get; private set; } = null!;

    /// <summary>The penalty = wrong − right (may be negative when the right test was the dearer one).</summary>
    public Money PenaltyAmount => WrongValue - RightValue;

    public DateTimeOffset CreatedAt { get; private set; }
    public string CreatedBy { get; private set; } = null!;
    public DateTimeOffset? UpdatedAt { get; private set; }
    public string? UpdatedBy { get; private set; }

    public static PenaltyRecord Create(LaboratoryId labId, DateOnly date, string accNo, string patientName,
        string wrongTestCode, string wrongTestName, decimal wrongValue,
        string rightTestCode, string rightTestName, decimal rightValue, PenaltyUser user)
    {
        var p = new PenaltyRecord(PenaltyRecordId.New(), labId);
        p.Update(date, accNo, patientName, wrongTestCode, wrongTestName, wrongValue, rightTestCode, rightTestName, rightValue, user);
        return p;
    }

    public void Update(DateOnly date, string accNo, string patientName,
        string wrongTestCode, string wrongTestName, decimal wrongValue,
        string rightTestCode, string rightTestName, decimal rightValue, PenaltyUser user)
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
        User = user ?? throw new DomainException("The responsible user is required.");
    }
}

// ---- Deductions ----

/// <summary>
/// A deduction applied to an area. Rows are entered manually; for the Penalty and PercentageDeal reasons the value is
/// suggested by the server from the Penalty Statement / the area's income × deal percentage over a period, then kept
/// editable (operator decision). The period the suggestion covered is stored for the audit trail.
/// </summary>
public sealed class Deduction : AggregateRoot<DeductionId>, IAuditable
{
    private Deduction() { } // EF
    private Deduction(DeductionId id, AreaId areaId) : base(id) { AreaId = areaId; }

    public long Serial { get; private set; }
    public DateOnly Date { get; private set; }
    public AreaId AreaId { get; private set; }
    public DeductionReason Reason { get; private set; } = null!;
    public Money Value { get; private set; }
    public string? Notes { get; private set; }
    /// <summary>The period a suggested (Penalty / PercentageDeal) value was computed over; null for typed values.</summary>
    public DateOnly? PeriodFrom { get; private set; }
    public DateOnly? PeriodTo { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public string CreatedBy { get; private set; } = null!;
    public DateTimeOffset? UpdatedAt { get; private set; }
    public string? UpdatedBy { get; private set; }

    public static Deduction Create(AreaId areaId, DateOnly date, DeductionReason reason, decimal value, string? notes, DateOnly? periodFrom, DateOnly? periodTo)
    {
        var d = new Deduction(DeductionId.New(), areaId);
        d.Update(date, reason, value, notes, periodFrom, periodTo);
        return d;
    }

    public void Update(DateOnly date, DeductionReason reason, decimal value, string? notes, DateOnly? periodFrom, DateOnly? periodTo)
    {
        if (periodFrom is { } f && periodTo is { } t && t < f) throw new DomainException("The period end must not precede its start.");
        if ((periodFrom is null) != (periodTo is null)) throw new DomainException("A period needs both a start and an end.");
        Date = date;
        Reason = reason ?? throw new DomainException("A deduction reason is required.");
        Value = AccountingGuards.NonNegative(value, "Deduction value");
        Notes = AccountingGuards.Optional(notes, 500);
        PeriodFrom = periodFrom; PeriodTo = periodTo;
    }
}

// ---- Collection ----

/// <summary>
/// Money collected from a lab by one rep (Single) or several (Group), split into Cash and Bank. A bank amount must name
/// the IBAN it was paid into; with no bank amount the IBAN is cleared.
/// </summary>
public sealed class Collection : AggregateRoot<CollectionId>, IAuditable
{
    private readonly List<RepresentativeId> _repIds = new();

    private Collection() { } // EF
    private Collection(CollectionId id, LaboratoryId labId) : base(id) { LaboratoryId = labId; }

    public long Serial { get; private set; }
    public DateOnly Date { get; private set; }
    public LaboratoryId LaboratoryId { get; private set; }
    public CollectionType Type { get; private set; } = null!;
    public IReadOnlyCollection<RepresentativeId> RepIds => _repIds.AsReadOnly();
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

    public static Collection Create(LaboratoryId labId, DateOnly date, CollectionType type, IEnumerable<RepresentativeId> reps,
        decimal cash, decimal bank, IbanOption? iban, string? doneBy, string? notes)
    {
        var c = new Collection(CollectionId.New(), labId);
        c.Update(date, type, reps, cash, bank, iban, doneBy, notes);
        return c;
    }

    public void Update(DateOnly date, CollectionType type, IEnumerable<RepresentativeId> reps,
        decimal cash, decimal bank, IbanOption? iban, string? doneBy, string? notes)
    {
        var repList = reps.Distinct().ToList();
        Type = type ?? throw new DomainException("A collection type is required.");
        if (type == CollectionType.Single && repList.Count != 1) throw new DomainException("A single collection names exactly one rep.");
        if (type == CollectionType.Group && repList.Count < 2) throw new DomainException("A group collection names at least two reps.");

        var c = AccountingGuards.NonNegative(cash, "Cash");
        var b = AccountingGuards.NonNegative(bank, "Bank");
        if (c.Amount + b.Amount <= 0) throw new DomainException("A collection must carry a cash or bank amount.");
        if (b.Amount > 0 && iban is null) throw new DomainException("A bank amount requires the IBAN it was paid into.");

        Date = date; Cash = c; Bank = b;
        Iban = b.Amount > 0 ? iban : null; // no bank amount → no IBAN, so a stale account can never linger
        DoneBy = AccountingGuards.Optional(doneBy, 200);
        Notes = AccountingGuards.Optional(notes, 500);
        _repIds.Clear();
        _repIds.AddRange(repList);
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
