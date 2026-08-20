using FollowUp.Application.Features.Insights;
using MediatR;

namespace FollowUp.Api.Endpoints;

public static class InsightsEndpoints
{
    public static void MapInsightsEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/dashboard", async (IMediator m, CancellationToken ct) =>
            Results.Ok(await m.Send(new GetDashboardQuery(), ct))).WithTags("Insights");

        api.MapGet("/reports/overview", async (IMediator m, CancellationToken ct) =>
            Results.Ok(await m.Send(new GetOverviewReportQuery(), ct))).WithTags("Insights");

        api.MapGet("/reports/performance", async (IMediator m, CancellationToken ct) =>
            Results.Ok(await m.Send(new GetPerformanceReportQuery(), ct))).WithTags("Insights");

        api.MapGet("/reports/labhistory/{id:guid}", async (Guid id, IMediator m, CancellationToken ct) =>
            Results.Ok(await m.Send(new GetLabHistoryReportQuery(id), ct))).WithTags("Insights");

        api.MapGet("/reports/rep-intervals", async (DateOnly? start, DateOnly? end, IMediator m, CancellationToken ct) =>
            Results.Ok(await m.Send(new GetRepIntervalsReportQuery(start, end), ct))).WithTags("Insights");
    }
}
