using FollowUp.Application.Common.Messaging;
using FollowUp.Application.Features.DailyBoard.Contracts;
using FollowUp.Domain.Laboratories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace FollowUp.Application.Features.DailyBoard;

/// <summary>
/// BR-3 intra-day board scheduling (SRS FR-5). When a lab is onboarded (<see cref="LaboratoryRegistered"/>)
/// or its schedule changes (<see cref="LaboratoryScheduleChanged"/>), today's board is reconciled so the
/// lab's visits appear immediately instead of waiting for the midnight roll-over. Runs from the Outbox
/// dispatcher alongside the notification fan-out; reconciliation is additive and idempotent (labs already
/// scheduled for the day are skipped). Never throws — a scheduling failure must not derail outbox processing
/// or cause a re-published event to duplicate the sibling notifications (the midnight roll-over is the backstop).
/// </summary>
public sealed class BoardSchedulingHandler : INotificationHandler<DomainEventNotification>
{
    private readonly IBoardScheduler _scheduler;
    private readonly ILogger<BoardSchedulingHandler> _logger;

    public BoardSchedulingHandler(IBoardScheduler scheduler, ILogger<BoardSchedulingHandler> logger)
    {
        _scheduler = scheduler;
        _logger = logger;
    }

    public async Task Handle(DomainEventNotification notification, CancellationToken ct)
    {
        try
        {
            switch (notification.DomainEvent)
            {
                // A new lab: add its visits to today's board (additive — a new lab has nothing to prune).
                case LaboratoryRegistered:
                    var added = await _scheduler.ReconcileTodayAsync(ct);
                    _logger.LogInformation(
                        "BR-3 intra-day reconcile after LaboratoryRegistered added {Added} visit(s) to today's board.", added);
                    break;

                // A schedule change: align the lab with its new schedule, pruning stale Pending visits.
                case LaboratoryScheduleChanged scheduleChanged:
                    var result = await _scheduler.ReconcileLabTodayAsync(scheduleChanged.LaboratoryId, ct);
                    _logger.LogInformation(
                        "BR-3 intra-day reconcile after LaboratoryScheduleChanged added {Added} and pruned {Pruned} visit(s) on today's board.",
                        result.Added, result.Pruned);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "BR-3 intra-day reconcile after {Event} failed; today's board will be corrected at the next roll-over.",
                notification.DomainEvent.GetType().Name);
        }
    }
}
