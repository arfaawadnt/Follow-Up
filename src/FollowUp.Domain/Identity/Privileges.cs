namespace FollowUp.Domain.Identity;

/// <summary>
/// Canonical catalogue of the ~45 fine-grained privileges (SRS §2.1) plus the central expansion rules
/// (SRS §2.1: a coarse <c>Manage*</c> implies View/Add/Update/Delete; documented cross-grants apply).
/// Expansion is computed here once so UI and backend can never disagree.
/// </summary>
public static class Privileges
{
    // Dashboard / reports
    public const string ViewDashboard = nameof(ViewDashboard);
    public const string ViewReports = nameof(ViewReports);

    // Labs
    public const string ManageLabs = nameof(ManageLabs);
    public const string AddLabs = nameof(AddLabs);
    public const string UpdateLabs = nameof(UpdateLabs);
    public const string ViewLabLocation = nameof(ViewLabLocation);
    public const string ShowEncryptedLabs = nameof(ShowEncryptedLabs);

    // Reps
    public const string ManageReps = nameof(ManageReps);
    public const string ViewReps = nameof(ViewReps);
    public const string AddReps = nameof(AddReps);
    public const string UpdateReps = nameof(UpdateReps);

    // Daily follow-up
    public const string ViewDailyFollowup = nameof(ViewDailyFollowup);
    public const string AddDailyFollowup = nameof(AddDailyFollowup);
    public const string UpdateDailyFollowup = nameof(UpdateDailyFollowup);
    public const string VerifyDailyFollowup = nameof(VerifyDailyFollowup);

    // Transfers
    public const string ViewTransfers = nameof(ViewTransfers);
    public const string ConfirmTransfers = nameof(ConfirmTransfers);
    public const string ManageTransfers = nameof(ManageTransfers);

    // Ops
    public const string SampleTracking = nameof(SampleTracking);
    public const string OutsourceSamples = nameof(OutsourceSamples);

    // Marketing
    public const string ViewMarketing = nameof(ViewMarketing);
    public const string AddMarketing = nameof(AddMarketing);
    public const string UpdateMarketing = nameof(UpdateMarketing);

    // Complaints
    public const string ViewComplaints = nameof(ViewComplaints);
    public const string AddComplaints = nameof(AddComplaints);
    public const string UpdateComplaints = nameof(UpdateComplaints);
    public const string ResolveComplaints = nameof(ResolveComplaints);
    public const string ManageComplaints = nameof(ManageComplaints);

    // Loyalty & commissions
    public const string ManageLoyalty = nameof(ManageLoyalty);
    public const string ManageCommissions = nameof(ManageCommissions);

    // Stats & catalogue
    public const string ViewLabStats = nameof(ViewLabStats);
    public const string ViewTeststats = nameof(ViewTeststats);
    public const string ViewAreaStats = nameof(ViewAreaStats);
    public const string AddGroups = nameof(AddGroups);
    public const string UpdateGroups = nameof(UpdateGroups);
    public const string DeleteGroups = nameof(DeleteGroups);
    public const string AddTestsetup = nameof(AddTestsetup);
    public const string UpdateTestsetup = nameof(UpdateTestsetup);
    public const string DeleteTestsetup = nameof(DeleteTestsetup);
    public const string AddTeststats = nameof(AddTeststats);

    // Administration
    public const string ManageUsers = nameof(ManageUsers);
    public const string OracleIntegration = nameof(OracleIntegration);
    public const string ManageEmailReports = nameof(ManageEmailReports);
    public const string SetupRefs = nameof(SetupRefs);
    public const string SetupCities = nameof(SetupCities);
    public const string SetupAreas = nameof(SetupAreas);

    /// <summary>Every privilege name (the ~45 leaves plus the coarse Manage* grants).</summary>
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ViewDashboard, ViewReports,
        ManageLabs, AddLabs, UpdateLabs, ViewLabLocation, ShowEncryptedLabs,
        ManageReps, ViewReps, AddReps, UpdateReps,
        ViewDailyFollowup, AddDailyFollowup, UpdateDailyFollowup, VerifyDailyFollowup,
        ViewTransfers, ConfirmTransfers, ManageTransfers,
        SampleTracking, OutsourceSamples,
        ViewMarketing, AddMarketing, UpdateMarketing,
        ViewComplaints, AddComplaints, UpdateComplaints, ResolveComplaints, ManageComplaints,
        ManageLoyalty, ManageCommissions,
        ViewLabStats, ViewTeststats, ViewAreaStats, AddGroups, UpdateGroups, DeleteGroups,
        AddTestsetup, UpdateTestsetup, DeleteTestsetup, AddTeststats,
        ManageUsers, OracleIntegration, ManageEmailReports, SetupRefs, SetupCities, SetupAreas,
    };

    // Coarse → fine-grained expansions, plus documented cross-grants.
    private static readonly Dictionary<string, string[]> Expansions = new(StringComparer.OrdinalIgnoreCase)
    {
        [ManageLabs] = new[] { AddLabs, UpdateLabs, ViewLabLocation },
        [ManageReps] = new[] { ViewReps, AddReps, UpdateReps },
        [ManageComplaints] = new[] { ViewComplaints, AddComplaints, UpdateComplaints, ResolveComplaints },
        [ManageTransfers] = new[] { ViewTransfers, ConfirmTransfers },
        [ViewReports] = new[] { ViewLabStats, ViewTeststats, ViewAreaStats },
    };

    /// <summary>
    /// Expands a raw grant set into the full effective set (coarse Manage* implies its leaves and
    /// documented cross-grants). Fixed-point so chained expansions resolve.
    /// </summary>
    public static IReadOnlySet<string> Expand(IEnumerable<string> granted)
    {
        var effective = new HashSet<string>(granted, StringComparer.OrdinalIgnoreCase);
        bool changed;
        do
        {
            changed = false;
            foreach (var privilege in effective.ToArray())
            {
                if (!Expansions.TryGetValue(privilege, out var implied)) continue;
                foreach (var leaf in implied)
                    changed |= effective.Add(leaf);
            }
        } while (changed);
        return effective;
    }
}
