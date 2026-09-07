using FluentAssertions;
using FollowUp.Application.Common.Abstractions;
using FollowUp.Application.Common.Behaviors;
using FollowUp.Application.Common.Exceptions;
using FollowUp.Application.Features.AreaStats;
using FollowUp.Application.Features.LabStats;
using FollowUp.Application.Tests.Common;
using FollowUp.Domain.Identity;

namespace FollowUp.Application.Tests.Features.Stats;

/// <summary>
/// Finding M-7 / STAT-004: the destructive lab/area stats import + Oracle-sync commands were gated by *View*
/// privileges, so a read-only user could overwrite statistics. They now require the AddLabStats/AddAreaStats
/// write privileges; the View* privileges no longer authorize them.
/// </summary>
public class StatsWritePrivilegeTests
{
    private static readonly DateOnly From = new(2026, 8, 1);
    private static readonly DateOnly To = new(2026, 8, 2);
    private static Task<OracleSyncResult> Next() => Task.FromResult(new OracleSyncResult(false, "noop", 0, 0));

    [Fact]
    public async Task View_only_user_cannot_sync_lab_stats()
    {
        var user = new FakeCurrentUser { Privileges = new HashSet<string> { Privileges.ViewLabStats } };
        var behavior = new AuthorizationBehavior<SyncLabStatsCommand, OracleSyncResult>(user);
        var act = () => behavior.Handle(new SyncLabStatsCommand(From, To), Next, CancellationToken.None);
        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task Write_user_can_sync_lab_stats()
    {
        var user = new FakeCurrentUser { Privileges = new HashSet<string> { Privileges.AddLabStats } };
        var behavior = new AuthorizationBehavior<SyncLabStatsCommand, OracleSyncResult>(user);
        var result = await behavior.Handle(new SyncLabStatsCommand(From, To), Next, CancellationToken.None);
        result.Status.Should().Be("noop");
    }

    [Fact]
    public async Task View_only_user_cannot_sync_area_stats()
    {
        var user = new FakeCurrentUser { Privileges = new HashSet<string> { Privileges.ViewAreaStats } };
        var behavior = new AuthorizationBehavior<SyncAreaStatsCommand, OracleSyncResult>(user);
        var act = () => behavior.Handle(new SyncAreaStatsCommand(From, To), Next, CancellationToken.None);
        await act.Should().ThrowAsync<ForbiddenException>();
    }
}
