using FollowUp.Application.Common.Abstractions.Persistence;
using FollowUp.Application.Common.Messaging;
using FollowUp.Domain.Laboratories;
using MediatR;
using Microsoft.Extensions.Logging;
using DomainSampleTracking = FollowUp.Domain.Operations.SampleTracking;

namespace FollowUp.Application.Features.SampleTracking;

/// <summary>
/// Keeps the FR-8 area/day tracking rows truthful when a laboratory moves between areas: every date on
/// which the lab has received samples is recomputed for both the old and the new area, so receipts
/// confirmed before the lab had an area (or under a wrong one) surface without manual backfill. Runs from
/// the Outbox dispatcher on <see cref="LaboratoryAreaChanged"/>. Recomputed totals make redelivery
/// idempotent, so failures propagate — the outbox's bounded per-message retry redelivers and the
/// recompute self-heals instead of stamping the message processed with stale totals.
/// </summary>
public sealed class AreaChangeTrackingHandler : INotificationHandler<DomainEventNotification>
{
    private readonly ISampleTrackingRepository _tracking;
    private readonly ISampleTrackingQueries _queries;
    private readonly ILogger<AreaChangeTrackingHandler> _logger;

    public AreaChangeTrackingHandler(ISampleTrackingRepository tracking, ISampleTrackingQueries queries,
        ILogger<AreaChangeTrackingHandler> logger)
    {
        _tracking = tracking; _queries = queries; _logger = logger;
    }

    public async Task Handle(DomainEventNotification notification, CancellationToken ct)
    {
        if (notification.DomainEvent is not LaboratoryAreaChanged changed) return;

        try
        {
            var dates = await _queries.GetReceivedVisitDatesAsync(changed.LaboratoryId, ct);
            if (dates.Count == 0) return;

            var refreshed = 0;
            foreach (var date in dates)
            {
                if (changed.OldArea is not null && await RefreshAsync(changed.OldArea, date, ct)) refreshed++;
                if (changed.NewArea is not null && await RefreshAsync(changed.NewArea, date, ct)) refreshed++;
            }
            _logger.LogInformation("Area-change tracking refreshed {Rows} area/day rows for lab {LabId} ({Old} -> {New}).",
                refreshed, changed.LaboratoryId.Value, changed.OldArea ?? "(none)", changed.NewArea ?? "(none)");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Area-change tracking refresh failed for lab {LabId}; the outbox will retry.", changed.LaboratoryId.Value);
            throw;
        }
    }

    /// <summary>Recomputes one area/day total. Rows nobody worked that land on zero are dropped;
    /// worked rows keep their step assignments and simply show the recomputed count.</summary>
    private async Task<bool> RefreshAsync(string area, DateOnly date, CancellationToken ct)
    {
        var total = await _queries.SumReceivedSamplesAsync(area, date, ct);
        var row = await _tracking.GetByAreaDateAsync(area, date, ct);
        if (row is null)
        {
            if (total == 0) return false;
            row = DomainSampleTracking.Open(area, date);
            _tracking.Add(row);
        }
        else if (total == 0 && row.IsUntouched)
        {
            _tracking.Remove(row);
            return true;
        }
        row.SetCount(total);
        return true;
    }
}
