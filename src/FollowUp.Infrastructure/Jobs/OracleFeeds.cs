namespace FollowUp.Infrastructure.Jobs;

/// <summary>
/// Single source of truth for the allow-listed Oracle feed names (SRS FR-17), so the reader's run-time tamper
/// guard and the sync runner can no longer drift apart (finding STAT-009). Reference/catalogue feeds are pulled
/// by the general hourly sync in dependency order (geography/reference before the records that resolve against
/// them); the statistics feeds are pulled on their own explicit date windows — the nightly job and the page
/// buttons — never by the general sync.
/// </summary>
public static class OracleFeeds
{
    /// <summary>Reference/catalogue feeds the general hourly sync processes, in dependency order.</summary>
    public static readonly string[] GeneralSync =
    {
        "Governorates", "LabCategories", "Branches", "Cities", "Areas", "Reps", "Groups", "Tests", "Labs",
    };

    /// <summary>Statistics feeds pulled on explicit date windows (nightly job / page buttons), not the general sync.</summary>
    public static readonly string[] DateScoped =
    {
        "TestStats", "LabStats", "DetailedStats", "NoLabTests",
    };

    /// <summary>Every allow-listed feed — the reader's run-time tamper guard.</summary>
    public static readonly string[] All = GeneralSync.Concat(DateScoped).ToArray();
}
