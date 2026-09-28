using FollowUp.Domain.Common;

namespace FollowUp.Domain.Statistics;

/// <summary>
/// How long after its registration a test line was added (2026-09-28): <see cref="None"/> when the test was created
/// with (or before) the registration or either timestamp is unknown; <see cref="Within3Hours"/> when it was added
/// later but within 3 hours; <see cref="Over3Hours"/> when it was added more than 3 hours after registration.
/// </summary>
public enum TestAdditionStatus { None, Within3Hours, Over3Hours }

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
    /// <summary>The registration's own branch code (from <c>reg.branch_code</c>), resolved to a name via the
    /// Branches reference at read time. Distinct from the lab's serving branch.</summary>
    public string? RegBranchCode { get; private set; }
    public string AccNo { get; private set; } = "";
    public string PatientName { get; private set; } = "";
    public string TestCode { get; private set; } = "";
    public int TestType { get; private set; }
    public string? TestName { get; private set; }
    public decimal PatientFee { get; private set; }
    public decimal InsuranceFee { get; private set; }
    /// <summary>Plain-text sample/test statuses from <c>reg_lines</c> — stored for a separate page.</summary>
    public string? SampleStatus { get; private set; }
    public string? TestStatus { get; private set; }
    /// <summary>When the registration (Acc No) was created in LDM — Oracle <c>reg.created_date</c>, LDM local time.</summary>
    public DateTime? RegCreatedAt { get; private set; }
    /// <summary>When this test line was added to the registration — Oracle <c>reg_selected_services.created_date</c>, LDM local time.</summary>
    public DateTime? TestCreatedAt { get; private set; }

    /// <summary>Combined fee shown by the page (cash + insurance).</summary>
    public decimal Fee => PatientFee + InsuranceFee;

    /// <summary>Tests added after the registration within this window are "late but acceptable" (flagged blue); later ones are flagged red.</summary>
    public static readonly TimeSpan LateAdditionWindow = TimeSpan.FromHours(3);

    /// <summary>How long after the registration this test was added; null when either timestamp is unknown.</summary>
    public TimeSpan? TestAdditionDelay => RegCreatedAt is { } r && TestCreatedAt is { } t ? t - r : null;

    /// <summary>Classification of <see cref="TestAdditionDelay"/> (see <see cref="TestAdditionStatus"/>).</summary>
    public TestAdditionStatus TestAddition => Classify(TestAdditionDelay);

    public static TestAdditionStatus Classify(TimeSpan? delay) =>
        delay is not { } d || d <= TimeSpan.Zero ? TestAdditionStatus.None
        : d <= LateAdditionWindow ? TestAdditionStatus.Within3Hours
        : TestAdditionStatus.Over3Hours;

    public static DetailedRegistration Create(DateOnly date, string? labCode, string? regBranchCode, string? accNo,
        string? patientName, string? testCode, int testType, string? testName, decimal patientFee, decimal insuranceFee,
        string? sampleStatus, string? testStatus, DateTime? regCreatedAt = null, DateTime? testCreatedAt = null) =>
        new(DetailedRegistrationId.New())
        {
            Date = date,
            LabCode = string.IsNullOrWhiteSpace(labCode) ? null : labCode.Trim().ToUpperInvariant(),
            RegBranchCode = string.IsNullOrWhiteSpace(regBranchCode) ? null : regBranchCode.Trim(),
            AccNo = accNo?.Trim() ?? "",
            PatientName = patientName?.Trim() ?? "",
            TestCode = testCode?.Trim() ?? "",
            TestType = testType,
            TestName = string.IsNullOrWhiteSpace(testName) ? null : testName.Trim(),
            PatientFee = patientFee < 0 ? 0 : patientFee,
            InsuranceFee = insuranceFee < 0 ? 0 : insuranceFee,
            SampleStatus = string.IsNullOrWhiteSpace(sampleStatus) ? null : sampleStatus.Trim(),
            TestStatus = string.IsNullOrWhiteSpace(testStatus) ? null : testStatus.Trim(),
            // Oracle DATE values are LDM local wall-clock time with no zone: keep them unspecified (stored as timestamp without time zone).
            RegCreatedAt = regCreatedAt is { } rc ? DateTime.SpecifyKind(rc, DateTimeKind.Unspecified) : null,
            TestCreatedAt = testCreatedAt is { } tc ? DateTime.SpecifyKind(tc, DateTimeKind.Unspecified) : null,
        };
}
