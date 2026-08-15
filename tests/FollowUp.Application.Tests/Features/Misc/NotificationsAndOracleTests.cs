using FluentAssertions;
using FollowUp.Application.Common.Exceptions;
using FollowUp.Application.Features.Integration;
using FollowUp.Application.Features.Notifications;
using FollowUp.Application.Tests.Common;
using FollowUp.Domain.Identity;
using FollowUp.Domain.Notifications;

namespace FollowUp.Application.Tests.Features.Misc;

public class NotificationsTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Owner_can_mark_own_notification_read()
    {
        var me = AppUserId.New();
        var repo = new FakeSystemNotificationRepository();
        var n = SystemNotification.Create(me, "complaint.logged", "New complaint", "CMP-1", Now);
        repo.Store.Add(n);
        var handler = new MarkNotificationReadHandler(repo, new FakeCurrentUser { UserId = me }, new FakeClock(Now));

        await handler.Handle(new MarkNotificationReadCommand(n.Id.Value), CancellationToken.None);

        n.IsRead.Should().BeTrue();
    }

    [Fact]
    public async Task Cannot_mark_someone_elses_notification_read()
    {
        var owner = AppUserId.New();
        var repo = new FakeSystemNotificationRepository();
        var n = SystemNotification.Create(owner, "complaint.logged", "New", "CMP-1", Now);
        repo.Store.Add(n);
        var handler = new MarkNotificationReadHandler(repo, new FakeCurrentUser { UserId = AppUserId.New() }, new FakeClock(Now));

        var act = () => handler.Handle(new MarkNotificationReadCommand(n.Id.Value), CancellationToken.None);
        await act.Should().ThrowAsync<ForbiddenException>();
    }
}

public class OracleIntegrationTests
{
    [Fact]
    public async Task Update_config_sets_enable_and_interval_only()
    {
        var repo = new FakeOracleConfigRepository();
        var handler = new UpdateIntegrationConfigHandler(repo);

        await handler.Handle(new UpdateIntegrationConfigCommand(Enabled: true, IntervalHours: 12), CancellationToken.None);

        repo.Config!.Enabled.Should().BeTrue();
        repo.Config.IntervalHours.Should().Be(12);
    }

    [Fact]
    public async Task Get_config_never_exposes_connection_string()
    {
        var repo = new FakeOracleConfigRepository();
        var cfg = Domain.Integration.OracleConfig.Create(true, 24);
        cfg.ApplyManagedConfig("Host=secret;Password=hunter2",
            new[] { Domain.Integration.AllowListedQuery.Create("Labs", "SELECT code FROM labs") });
        repo.Add(cfg);

        var dto = await new GetIntegrationConfigHandler(repo).Handle(new GetIntegrationConfigQuery(), CancellationToken.None);

        dto.AllowListedQueries.Should().ContainSingle().Which.Should().Be("Labs");
        // The DTO has no connection-string field at all — it cannot leak.
        dto.GetType().GetProperties().Should().NotContain(p => p.Name.Contains("Connection"));
    }
}
