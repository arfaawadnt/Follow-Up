using FollowUp.Domain.Common;

namespace FollowUp.Domain.Statistics;

public readonly record struct DetailedRegistrationId(Guid Value)
{
    public static DetailedRegistrationId New() => new(Guid.NewGuid());
}

/// <summary>
/// A single synced Oracle registration test-line (date, lab, accession, patient, test, fees) backing the
/// Detailed Statistics page (SRS FR-13/FR-17). Populated wholesale per date-window by the Oracle sync (delete the
/// window, then re-insert), so there is no per-row upsert key — a surrogate id is used. <see cref="LabCode"/> is
/// null when the registration's referring doctor does not resolve to a lab (rendered as "No lab"). Fees are the
/// raw patient (cash) + insurance components; the page shows the combined fee. Indexed by (date, lab code) for the
/// range reads; this table grows per test-line so a retention window may be applied later.
/// </summary>
public sealed class DetailedRegistration : AggregateRoot<DetailedRegistrationId>
{
    private DetailedRegistration() { } // EF

    private DetailedRegistration(DetailedRegistrationId id) : base(id) { }

    public DateOnly Date { get; private set; }
    public string? LabCode { get; private set; }
    public string AccNo { get; private set; } = "";
    public string PatientName { get; private set; } = "";
    public string TestCode { get; private set; } = "";
    public int TestType { get; private set; }
    public string? TestName { get; private set; }
    public decimal PatientFee { get; private set; }
    public decimal InsuranceFee { get; private set; }

    /// <summary>Combined fee shown by the page (cash + insurance).</summary>
    public decimal Fee => PatientFee + InsuranceFee;

    public static DetailedRegistration Create(DateOnly date, string? labCode, string? accNo, string? patientName,
        string? testCode, int testType, string? testName, decimal patientFee, decimal insuranceFee) =>
        new(DetailedRegistrationId.New())
        {
            Date = date,
            LabCode = string.IsNullOrWhiteSpace(labCode) ? null : labCode.Trim().ToUpperInvariant(),
            AccNo = accNo?.Trim() ?? "",
            PatientName = patientName?.Trim() ?? "",
            TestCode = testCode?.Trim() ?? "",
            TestType = testType,
            TestName = string.IsNullOrWhiteSpace(testName) ? null : testName.Trim(),
            PatientFee = patientFee < 0 ? 0 : patientFee,
            InsuranceFee = insuranceFee < 0 ? 0 : insuranceFee,
        };
}
