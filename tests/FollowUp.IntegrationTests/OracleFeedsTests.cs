using FluentAssertions;
using FollowUp.Infrastructure.Jobs;
using Xunit;

namespace FollowUp.IntegrationTests;

/// <summary>
/// Finding STAT-009: the Oracle allow-list was hand-maintained in both OracleDbReader (tamper guard) and
/// OracleSyncRunner (general-sync feeds) and had drifted. Both now derive from OracleFeeds; these guard the
/// single-source invariants so a future edit can't silently reintroduce the drift.
/// </summary>
public sealed class OracleFeedsTests
{
    [Fact]
    public void All_is_exactly_the_general_plus_date_scoped_feeds_with_no_overlap()
    {
        OracleFeeds.GeneralSync.Should().OnlyHaveUniqueItems();
        OracleFeeds.DateScoped.Should().OnlyHaveUniqueItems();
        OracleFeeds.GeneralSync.Should().NotIntersectWith(OracleFeeds.DateScoped,
            "a feed is either general-sync or date-scoped, never both");
        OracleFeeds.All.Should().BeEquivalentTo(OracleFeeds.GeneralSync.Concat(OracleFeeds.DateScoped));
        OracleFeeds.All.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void The_statistics_feeds_are_never_run_by_the_general_sync()
    {
        // TestStats/LabStats/DetailedStats/NoLabTests are pulled on their own date windows — they must not appear
        // in the general hourly sync set, or it would pull the multi-year statistics history every hour.
        OracleFeeds.GeneralSync.Should().NotContain(new[] { "TestStats", "LabStats", "DetailedStats", "NoLabTests" });
    }
}
