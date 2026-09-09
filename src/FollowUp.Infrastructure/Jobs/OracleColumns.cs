namespace FollowUp.Infrastructure.Jobs;

/// <summary>
/// The column aliases returned by the allow-listed Oracle feeds (see <see cref="OracleDefaultQueries"/>), named
/// once so the sync mapping reads them by symbol instead of scattered string literals (finding STAT-008). Each
/// value is the exact upper-cased alias Oracle returns for the corresponding <c>AS</c> clause in the feed SQL.
/// </summary>
internal static class OracleColumns
{
    // Reference / geography / workforce master feeds.
    public const string GovernorateCode = "GOVERNCATE_CODE";
    public const string GovernorateName = "GOVERNCATE_NAME";
    public const string CityCode = "CITY_CODE";
    public const string CityName = "CITY_NAME";
    public const string AreaCode = "AREA_CODE";
    public const string AreaName = "AREA_NAME";
    public const string CategoryId = "CATEGORY_ID";
    public const string CategoryName = "CATEGORY_NAME";
    public const string BranchCode = "BRANCH_CODE";
    public const string BranchName = "BRANCH_NAME";
    public const string RepCode = "REP_CODE";
    public const string RepName = "REP_NAME";

    // Test-catalogue feeds (Groups, Tests).
    public const string GroupCode = "GROUP_CODE";
    public const string GroupName = "GROUP_NAME";
    public const string Cost = "COST";

    // Lab master feed (geography columns are the resolved NAMES on this feed, not codes).
    public const string LabCode = "LAB_CODE";
    public const string LabName = "LAB_NAME";
    public const string Governorate = "GOVERNCATE";
    public const string City = "CITY";
    public const string Area = "AREA";
    public const string Address = "ADDRESS";
    public const string CollectorRepCode = "COLLECTOR_REP_CODE";

    // Statistics feeds (TestStats / LabStats).
    public const string TheDate = "THE_DATE";
    public const string TestCode = "TEST_CODE";
    public const string TestType = "TEST_TYPE";
    public const string TestName = "TEST_NAME";
    public const string Branch = "BRANCH";
    public const string TestCount = "TEST_COUNT";
    public const string TestIncome = "TEST_INCOME";
    public const string RegCount = "REG_COUNT";
    public const string Income = "INCOME";

    // Detailed-statistics feed (transaction-level registration lines).
    public const string RegDate = "REG_DT";
    public const string RegBranchCode = "REG_BRANCH_CODE";
    public const string AccessionNo = "ACC_NO";
    public const string PatientName = "PATIENT_NAME";
    public const string PatientFee = "PATIENT_FEE";
    public const string InsuranceFee = "INSURANCE_FEE";
    public const string SampleStatus = "SAMPLE_STATUS";
    public const string TestStatus = "TEST_STATUS";
}
