using FluentAssertions;
using FollowUp.Application.Common.Messaging;
using FollowUp.Application.Features.DailyBoard;
using FollowUp.Application.Features.DailyBoard.Contracts;
using FollowUp.Domain.Laboratories;
using FollowUp.Domain.Operations;
using Microsoft.Extensions.Logging.Abstractions;

namespace FollowUp.Application.Tests.Features.DailyBoard;

public class BoardSchedulingHandlerTests
{
    private sealed class RecordingScheduler : IBoardScheduler
    {
        public int Calls { get; private set; }
        public int LabCalls { get; private set; }
        public LaboratoryId? LastLabId { get; private set; }
        public bool Throw { get; init; }
        public int Returns { get; init; }

        public Task<int> ReconcileTodayAsync(CancellationToken ct = default)
        {
            Calls++;
            if (Throw) throw new InvalidOperationException("boom");
            return Task.FromResult(Returns);
        }

        public Task<BoardReconciliation> ReconcileLabTodayAsync(LaboratoryId laboratoryId, CancellationToken ct = default)
        {
            LabCalls++;
            LastLabId = laboratoryId;
            if (Throw) throw new InvalidOperationException("boom");
            return Task.FromResult(new BoardReconciliation(Returns, 0));
        }
    }

    private static BoardSchedulingHandler Handler(IBoardScheduler scheduler) =>
        new(scheduler, NullLogger<BoardSchedulingHandler>.Instance);

    [Fact]
    public async Task Registration_reconciles_the_whole_board_additively()
    {
        var scheduler = new RecordingScheduler { Returns = 2 };
        await Handler(scheduler).Handle(
            new DomainEventNotification(new LaboratoryRegistered(LaboratoryId.New(), "MGL-1")), CancellationToken.None);

        scheduler.Calls.Should().Be(1);
        scheduler.LabCalls.Should().Be(0); // registration never prunes
    }

    [Fact]
    public async Task Schedule_change_reconciles_only_the_changed_lab_with_prune()
    {
        var labId = LaboratoryId.New();
        var scheduler = new RecordingScheduler();
        await Handler(scheduler).Handle(
            new DomainEventNotification(new LaboratoryScheduleChanged(labId)), CancellationToken.None);

        scheduler.LabCalls.Should().Be(1);
        scheduler.LastLabId.Should().Be(labId);
        scheduler.Calls.Should().Be(0); // does not touch other labs' boards
    }

    [Fact]
    public async Task Ignores_unrelated_domain_events()
    {
        var scheduler = new RecordingScheduler();
        await Handler(scheduler).Handle(
            new DomainEventNotification(new VisitMissed(DailyVisitId.New(), LaboratoryId.New())), CancellationToken.None);

        scheduler.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Swallows_scheduler_failures_so_the_outbox_is_not_derailed()
    {
        var scheduler = new RecordingScheduler { Throw = true };
        var act = async () => await Handler(scheduler).Handle(
            new DomainEventNotification(new LaboratoryRegistered(LaboratoryId.New(), "MGL-2")), CancellationToken.None);

        await act.Should().NotThrowAsync();
        scheduler.Calls.Should().Be(1);
    }
}
