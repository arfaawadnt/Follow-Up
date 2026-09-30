using FollowUp.Domain.Common;

namespace FollowUp.Domain.Statistics;

public readonly record struct RegistrationChangeId(Guid Value)
{
    public static RegistrationChangeId New() => new(Guid.NewGuid());
}

/// <summary>
/// One change made to a registration in LDM (Oracle <c>REG_LOG</c>, 2026-09-30): which column of <c>REG</c> changed, the
/// old and new value, who changed it and when — joined to the registration (accession, patient, creation time, reg
/// date, branch, resolved lab) so the Registration Changes page can show what was edited how long after the
/// registration was created. Synced per modification-date window (delete the window, re-insert; <see cref="TransId"/>
/// is Oracle's own key and stays unique). All timestamps are LDM wall-clock values (no zone).
/// </summary>
public sealed class RegistrationChange : AggregateRoot<RegistrationChangeId>
{
    private RegistrationChange() { } // EF

    private RegistrationChange(RegistrationChangeId id) : base(id) { }

    /// <summary>Oracle REG_LOG.TRANS_ID — the change's own key.</summary>
    public long TransId { get; private set; }
    public long RegKey { get; private set; }
    public string AccNo { get; private set; } = "";
    public string PatientName { get; private set; } = "";
    public DateTime? RegCreatedAt { get; private set; }
    public DateOnly? RegDate { get; private set; }
    public string? RegBranchCode { get; private set; }
    /// <summary>The lab the registration's doctor resolves to (the LabStats mapping); null = "No lab".</summary>
    public string? LabCode { get; private set; }
    /// <summary>The changed REG column as LDM names it (e.g. "Patient Name") — the page's "change type".</summary>
    public string Column { get; private set; } = "";
    public string? OldValue { get; private set; }
    public string? NewValue { get; private set; }
    public string ModifiedBy { get; private set; } = "";
    public DateTime ModifiedAt { get; private set; }
    /// <summary>Calendar day of <see cref="ModifiedAt"/> — the sync window and the page's modification-date filter.</summary>
    public DateOnly ModifiedDate { get; private set; }

    /// <summary>How long after the registration was created the change was made; null when the creation time is unknown.</summary>
    public TimeSpan? DelayFromCreation => RegCreatedAt is { } c ? ModifiedAt - c : null;

    public static RegistrationChange Create(long transId, long regKey, string? accNo, string? patientName, DateTime? regCreatedAt,
        DateOnly? regDate, string? regBranchCode, string? labCode, string? column, string? oldValue, string? newValue,
        string? modifiedBy, DateTime modifiedAt)
    {
        if (transId <= 0) throw new DomainException("A registration change needs its LDM transaction id.");
        return new RegistrationChange(RegistrationChangeId.New())
        {
            TransId = transId,
            RegKey = regKey,
            AccNo = accNo?.Trim() ?? "",
            PatientName = patientName?.Trim() ?? "",
            RegCreatedAt = regCreatedAt is { } rc ? DateTime.SpecifyKind(rc, DateTimeKind.Unspecified) : null,
            RegDate = regDate,
            RegBranchCode = string.IsNullOrWhiteSpace(regBranchCode) ? null : regBranchCode.Trim(),
            LabCode = string.IsNullOrWhiteSpace(labCode) ? null : labCode.Trim().ToUpperInvariant(),
            Column = string.IsNullOrWhiteSpace(column) ? "—" : column.Trim(),
            OldValue = string.IsNullOrWhiteSpace(oldValue) ? null : oldValue.Trim(),
            NewValue = string.IsNullOrWhiteSpace(newValue) ? null : newValue.Trim(),
            ModifiedBy = string.IsNullOrWhiteSpace(modifiedBy) ? "—" : modifiedBy.Trim(),
            ModifiedAt = DateTime.SpecifyKind(modifiedAt, DateTimeKind.Unspecified),
            ModifiedDate = DateOnly.FromDateTime(modifiedAt),
        };
    }
}
