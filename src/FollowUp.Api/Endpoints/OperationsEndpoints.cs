using FollowUp.Application.Features.DailyBoard.Commands;
using FollowUp.Application.Features.DailyBoard.Queries;
using FollowUp.Application.Features.LabCheckIn;
using FollowUp.Application.Features.Marketing;
using FollowUp.Application.Features.Outsource;
using FollowUp.Application.Features.SampleTracking;
using FollowUp.Application.Features.Transfers;
using MediatR;

namespace FollowUp.Api.Endpoints;

public static class OperationsEndpoints
{
    public sealed record CheckInBody(int SampleCount);
    public sealed record VerifyBody(bool Verified);
    public sealed record OutsourceStatusBody(string Status);
    public sealed record AdvanceStepBody(string Step);

    public static void MapOperationsEndpoints(this RouteGroupBuilder api)
    {
        // Daily board (FR-5)
        api.MapGet("/daily", async (DateOnly? start, DateOnly? end, DateOnly? date, string? rep, string? status, IMediator m, CancellationToken ct) =>
        {
            Guid? repId = Guid.TryParse(rep, out var g) ? g : null;
            return Results.Ok(await m.Send(new GetDailyBoardQuery(start ?? date, end ?? date, repId, status), ct));
        }).WithTags("DailyBoard");
        api.MapPost("/daily/{id:guid}/checkin", async (Guid id, CheckInBody b, IMediator m, CancellationToken ct) =>
        { await m.Send(new CheckInVisitCommand(id, b.SampleCount), ct); return Results.NoContent(); }).WithTags("DailyBoard");
        api.MapPost("/daily/{id:guid}/miss", async (Guid id, IMediator m, CancellationToken ct) =>
        { await m.Send(new MissVisitCommand(id), ct); return Results.NoContent(); }).WithTags("DailyBoard");
        api.MapPost("/daily/{id:guid}/undo", async (Guid id, IMediator m, CancellationToken ct) =>
        { await m.Send(new UndoVisitCommand(id), ct); return Results.NoContent(); }).WithTags("DailyBoard");
        api.MapPost("/daily/{id:guid}/verify", async (Guid id, VerifyBody b, IMediator m, CancellationToken ct) =>
        { await m.Send(new VerifyVisitCommand(id, b.Verified), ct); return Results.NoContent(); }).WithTags("DailyBoard");

        // Transfers (FR-6)
        api.MapGet("/transfers", async (DateOnly? start, DateOnly? end, IMediator m, CancellationToken ct) =>
            Results.Ok(await m.Send(new GetTransfersQuery(start, end), ct))).WithTags("Transfers");
        api.MapPost("/transfers/confirm", async (ConfirmTransferCommand cmd, IMediator m, CancellationToken ct) =>
        { await m.Send(cmd, ct); return Results.NoContent(); }).WithTags("Transfers");
        api.MapPost("/transfers/confirm-batch", async (ConfirmTransfersBatchCommand cmd, IMediator m, CancellationToken ct) =>
            Results.Ok(new { confirmed = await m.Send(cmd, ct) })).WithTags("Transfers");

        // Lab check-in (FR-7)
        api.MapGet("/labcheckin", async (DateOnly? start, DateOnly? end, IMediator m, CancellationToken ct) =>
            Results.Ok(await m.Send(new GetLabCheckInQuery(start, end), ct))).WithTags("LabCheckIn");
        api.MapPost("/labcheckin/confirm", async (ConfirmReceiptBody b, IMediator m, CancellationToken ct) =>
        { await m.Send(new ConfirmReceiptCommand(b.VisitId), ct); return Results.NoContent(); }).WithTags("LabCheckIn");
        api.MapPost("/labcheckin/confirm-batch", async (ConfirmReceiptsBatchCommand cmd, IMediator m, CancellationToken ct) =>
            Results.Ok(new { received = await m.Send(cmd, ct) })).WithTags("LabCheckIn");

        // Outsource (FR-9)
        api.MapGet("/outsource-samples", async (DateOnly? start, DateOnly? end, DateOnly? date, IMediator m, CancellationToken ct) =>
            Results.Ok(await m.Send(new GetOutsourceSamplesQuery(start ?? date, end ?? date), ct))).WithTags("Outsource");
        api.MapPost("/outsource-samples", async (CreateOutsourceSampleCommand cmd, IMediator m, CancellationToken ct) =>
        { var id = await m.Send(cmd, ct); return Results.Created($"/api/v1/outsource-samples/{id}", new { id }); }).WithTags("Outsource");
        api.MapPost("/outsource-samples/{id:guid}/status", async (Guid id, OutsourceStatusBody b, IMediator m, CancellationToken ct) =>
        { await m.Send(new AdvanceOutsourceStatusCommand(id, b.Status), ct); return Results.NoContent(); }).WithTags("Outsource");
        api.MapDelete("/outsource-samples/{id:guid}", async (Guid id, IMediator m, CancellationToken ct) =>
        { await m.Send(new DeleteOutsourceSampleCommand(id), ct); return Results.NoContent(); }).WithTags("Outsource");

        // Sample tracking (FR-8)
        api.MapGet("/sample-tracking", async (DateOnly? start, DateOnly? end, DateOnly? date, IMediator m, CancellationToken ct) =>
            Results.Ok(await m.Send(new GetSampleTrackingQuery(start ?? date, end ?? date), ct))).WithTags("SampleTracking");
        api.MapPost("/sample-tracking", async (RecordSampleDataEntryCommand cmd, IMediator m, CancellationToken ct) =>
        { var id = await m.Send(cmd, ct); return Results.Ok(new { id }); }).WithTags("SampleTracking");
        api.MapPost("/sample-tracking/batch", async (BatchRecordSampleDataEntryCommand cmd, IMediator m, CancellationToken ct) =>
            Results.Ok(new { processed = await m.Send(cmd, ct) })).WithTags("SampleTracking");
        api.MapPost("/sample-tracking/{id:guid}/advance", async (Guid id, AdvanceStepBody b, IMediator m, CancellationToken ct) =>
        { await m.Send(new AdvanceSampleTrackingCommand(id, b.Step), ct); return Results.NoContent(); }).WithTags("SampleTracking");
        api.MapGet("/sample-tracking/report", async (DateOnly from, DateOnly to, IMediator m, CancellationToken ct) =>
            Results.Ok(await m.Send(new GetSampleLifecycleReportQuery(from, to), ct))).WithTags("SampleTracking");
        api.MapGet("/sample-tracking/lifecycle", async (DateOnly from, DateOnly to, IMediator m, CancellationToken ct) =>
            Results.Ok(await m.Send(new GetSampleLifecycleQuery(from, to), ct))).WithTags("SampleTracking");
        api.MapPost("/sample-tracking/assignments", async (SaveSampleAssignmentsCommand cmd, IMediator m, CancellationToken ct) =>
            Results.Ok(new { saved = await m.Send(cmd, ct) })).WithTags("SampleTracking");

        // Marketing (FR-10)
        api.MapGet("/marketing", async (int? page, int? pageSize, string? status, Guid? laboratoryId, IMediator m, CancellationToken ct) =>
            Results.Ok(await m.Send(new GetMarketingVisitsQuery { Page = page ?? 1, PageSize = pageSize ?? 50, Status = status, LaboratoryId = laboratoryId }, ct))).WithTags("Marketing");
        api.MapPost("/marketing", async (ScheduleMarketingVisitCommand cmd, IMediator m, CancellationToken ct) =>
        { var id = await m.Send(cmd, ct); return Results.Created($"/api/v1/marketing/{id}", new { id }); }).WithTags("Marketing");
        api.MapPost("/marketing/{id:guid}/complete", async (Guid id, CompleteMarketingBody b, IMediator m, CancellationToken ct) =>
        { await m.Send(new CompleteMarketingVisitCommand(id, b.Outcome), ct); return Results.NoContent(); }).WithTags("Marketing");
        api.MapPost("/marketing/{id:guid}/cancel", async (Guid id, CancelMarketingBody b, IMediator m, CancellationToken ct) =>
        { await m.Send(new CancelMarketingVisitCommand(id, b.Reason), ct); return Results.NoContent(); }).WithTags("Marketing");
    }

    public sealed record ConfirmReceiptBody(Guid VisitId);
    public sealed record CompleteMarketingBody(string Outcome);
    public sealed record CancelMarketingBody(string? Reason);
}
