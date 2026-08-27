using FollowUp.Application.Common.Abstractions.Persistence;
using FollowUp.Application.Common.Messaging;
using FollowUp.Domain.Operations;
using MediatR;
using Microsoft.Extensions.Logging;
using DomainSampleTracking = FollowUp.Domain.Operations.SampleTracking;

namespace FollowUp.Application.Features.SampleTracking;

/// <summary>
/// Reference parity for FR-7/FR-8: when a visit's samples are received at the laboratory, the area/day
/// sample-tracking row is created (or refreshed) automatically with the derived received-samples total —
/// staff then only assign the data-entry/review/sort users. Runs from the Outbox dispatcher on
/// <see cref="VisitReceived"/>. Recomputes the total (idempotent under outbox redelivery), so failures
/// propagate — the outbox's bounded per-message retry redelivers instead of losing the refresh.
/// </summary>
public sealed class SampleReceiptTrackingHandler : INotificationHandler<DomainEventNotification>
{
    private readonly IDailyVisitRepository _visits;
    private readonly ILaboratoryRepository _labs;
    private readonly ISampleTrackingRepository _tracking;
    private readonly ISampleTrackingQueries _queries;
    private readonly ILogger<SampleReceiptTrackingHandler> _logger;

    public SampleReceiptTrackingHandler(IDailyVisitRepository visits, ILaboratoryRepository labs,
        ISampleTrackingRepository tracking, ISampleTrackingQueries queries, ILogger<SampleReceiptTrackingHandler> logger)
    {
        _visits = visits; _labs = labs; _tracking = tracking; _queries = queries; _logger = logger;
    }

    public async Task Handle(DomainEventNotification notification, CancellationToken ct)
    {
        if (notification.DomainEvent is not VisitReceived received) return;

        try
        {
            var visit = await _visits.GetByIdAsync(received.VisitId, ct);
            if (visit is null) return; // archived before dispatch — the day's roll-over totals stand
            var lab = await _labs.GetByIdAsync(received.LaboratoryId, ct);
            if (lab?.Area is null) return; // no area — nothing to track regionally

            var total = await _queries.SumReceivedSamplesAsync(lab.Area, visit.VisitDate, ct);

            var row = await _tracking.GetByAreaDateAsync(lab.Area, visit.VisitDate, ct);
            if (row is null)
            {
                row = DomainSampleTracking.Open(lab.Area, visit.VisitDate);
                _tracking.Add(row);
            }
            row.SetCount(total);
            _logger.LogInformation("Receipt tracking refreshed area {Area} on {Date}: {Total} received samples.",
                lab.Area, visit.VisitDate, total);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Receipt tracking refresh failed for visit {VisitId}; the outbox will retry.", received.VisitId.Value);
            throw;
        }
    }
}
