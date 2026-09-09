using FluentAssertions;
using FollowUp.Domain.Integration;
using Xunit;

namespace FollowUp.Domain.Tests.Integration;

public class OracleConfigTests
{
    [Fact]
    public void RecordStatsSyncResult_records_status_without_advancing_the_general_due_gate()
    {
        // Finding STAT-011: the date-scoped statistics sync must record its outcome (LastStatsStatus/At) but must
        // NOT touch LastSyncAt — that gates the general hourly sync's IsDue, and a nightly stats pull shifting it
        // would make the hourly sync skip a cycle.
        var cfg = OracleConfig.Create(enabled: true, intervalHours: 24);
        var t0 = new DateTimeOffset(2026, 9, 9, 3, 0, 0, TimeSpan.Zero);
        cfg.RecordSyncResult("general:ok", t0);
        var generalSyncAt = cfg.LastSyncAt;

        var t1 = t0.AddHours(2);
        cfg.RecordStatsSyncResult("teststats:ok:5", t1);

        cfg.LastStatsStatus.Should().Be("teststats:ok:5");
        cfg.LastStatsSyncAt.Should().Be(t1);
        cfg.LastSyncAt.Should().Be(generalSyncAt, "the general sync's due-gate timestamp must be untouched");
        cfg.LastStatus.Should().Be("general:ok", "the general sync's status must be untouched");
        cfg.IsDue(t1).Should().BeFalse("the general sync ran 2h ago and its 24h interval has not elapsed");
    }
}
