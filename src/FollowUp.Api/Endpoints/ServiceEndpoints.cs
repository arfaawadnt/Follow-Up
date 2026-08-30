using FollowUp.Application.Features.Complaints.Commands;
using FollowUp.Application.Features.Complaints.Queries;
using FollowUp.Application.Features.Notifications;
using FollowUp.Application.Features.Signatures;
using MediatR;

namespace FollowUp.Api.Endpoints;

public static class ServiceEndpoints
{
    public sealed record LogComplaintBody(Guid LaboratoryId, string Category, string ViaChannel, string? AssignedTeam,
        string Details, Guid? RepresentativeId, DateTimeOffset? ReceivedAt);
    public sealed record AdvanceStageBody(string Stage, string? Notes, bool? IsValid, string? OutcomeType, string? Summary);
    public sealed record ResolveBody(string? ResolutionSummary);
    public sealed record SignActionBody(string Meaning, string? Reason, string Password); // module+recordId are in the route (SIG-12)
    public sealed record SignatureCreatedDto(Guid Id);
    public sealed record PreferenceBody(string EventKey, bool System, bool Mail, bool WhatsApp);

    public static void MapComplaintEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/complaints", async (int? page, int? pageSize, string? status, string? category, Guid? laboratoryId, IMediator m, CancellationToken ct) =>
            Results.Ok(await m.Send(new GetComplaintsQuery { Page = page ?? 1, PageSize = pageSize ?? 50, Status = status, Category = category, LaboratoryId = laboratoryId }, ct))).WithTags("Complaints");
        api.MapGet("/complaints/{id:guid}", async (Guid id, IMediator m, CancellationToken ct) =>
            Results.Ok(await m.Send(new GetComplaintByIdQuery(id), ct))).WithTags("Complaints");
        api.MapGet("/complaints/{id:guid}/audit", async (Guid id, IMediator m, CancellationToken ct) =>
            Results.Ok(await m.Send(new GetComplaintAuditQuery(id), ct))).WithTags("Complaints");
        api.MapPost("/complaints", async (LogComplaintBody b, IMediator m, CancellationToken ct) =>
        {
            var r = await m.Send(new LogComplaintCommand
            {
                LaboratoryId = b.LaboratoryId, Category = b.Category, ViaChannel = b.ViaChannel,
                AssignedTeam = b.AssignedTeam, Details = b.Details, RepresentativeId = b.RepresentativeId, ReceivedAt = b.ReceivedAt,
            }, ct);
            return Results.Created($"/api/v1/complaints/{r.Id}", r); // resource URI carries the new id (CMP-13)
        }).WithTags("Complaints");
        api.MapPost("/complaints/{id:guid}/start", async (Guid id, IMediator m, CancellationToken ct) =>
        { await m.Send(new StartComplaintCommand(id), ct); return Results.NoContent(); }).WithTags("Complaints");
        api.MapPost("/complaints/{id:guid}/resolve", async (Guid id, ResolveBody? b, IMediator m, CancellationToken ct) =>
        { await m.Send(new ResolveComplaintCommand(id, b?.ResolutionSummary), ct); return Results.NoContent(); }).WithTags("Complaints");
        api.MapPost("/complaints/{id:guid}/reopen", async (Guid id, IMediator m, CancellationToken ct) =>
        { await m.Send(new ReopenComplaintCommand(id), ct); return Results.NoContent(); }).WithTags("Complaints");
        // Retired (CMP-5): /stage duplicated /advance, which survives. Return 410 Gone so callers get a clear
        // signal to migrate rather than a silent behaviour change.
        api.MapPost("/complaints/{id:guid}/stage", (Guid id) =>
            Results.Problem(statusCode: 410, title: "Endpoint retired",
                detail: "POST /complaints/{id}/stage is retired. Use POST /complaints/{id}/advance."))
            .WithTags("Complaints").AllowAnonymous();
        api.MapPost("/complaints/{id:guid}/advance", async (Guid id, AdvanceStageBody b, IMediator m, CancellationToken ct) =>
        { await m.Send(new AdvanceComplaintStageCommand(id, b.Stage, b.Notes, b.IsValid, b.OutcomeType, b.Summary), ct); return Results.NoContent(); }).WithTags("Complaints");
    }

    public static void MapSignatureEndpoints(this RouteGroupBuilder api)
    {
        api.MapPost("/esign/{module}/{recordId}/sign", async (string module, string recordId, SignActionBody b, IMediator m, CancellationToken ct) =>
        {
            var id = await m.Send(new SignRecordCommand(module, recordId, b.Meaning, b.Reason, b.Password), ct);
            return Results.Ok(new SignatureCreatedDto(id));
        }).WithTags("Signatures").RequireRateLimiting("esign");
        api.MapGet("/esign/{module}/{recordId}", async (string module, string recordId, IMediator m, CancellationToken ct) =>
            Results.Ok(await m.Send(new VerifySignatureQuery(module, recordId), ct))).WithTags("Signatures");
    }

    public static void MapNotificationEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/notifications", async (bool? unreadOnly, IMediator m, CancellationToken ct) =>
            Results.Ok(await m.Send(new GetNotificationsQuery(unreadOnly ?? false), ct))).WithTags("Notifications");
        api.MapPost("/notifications/{id:guid}/read", async (Guid id, IMediator m, CancellationToken ct) =>
        { await m.Send(new MarkNotificationReadCommand(id), ct); return Results.NoContent(); }).WithTags("Notifications");
        api.MapPost("/notifications/read-all", async (IMediator m, CancellationToken ct) =>
        { await m.Send(new MarkAllNotificationsReadCommand(), ct); return Results.NoContent(); }).WithTags("Notifications");
        api.MapGet("/notifications/preferences", async (IMediator m, CancellationToken ct) =>
            Results.Ok(await m.Send(new GetNotificationPreferencesQuery(), ct))).WithTags("Notifications");
        api.MapPut("/notifications/preferences", async (PreferenceBody b, IMediator m, CancellationToken ct) =>
        { await m.Send(new UpdateNotificationPreferenceCommand(b.EventKey, b.System, b.Mail, b.WhatsApp), ct); return Results.NoContent(); }).WithTags("Notifications");
        api.MapGet("/notifications/gateways", async (IMediator m, CancellationToken ct) =>
            Results.Ok(await m.Send(new GetNotificationGatewaysQuery(), ct))).WithTags("Notifications");
        api.MapGet("/notifications/logs", async (IMediator m, CancellationToken ct) =>
            Results.Ok(await m.Send(new GetDeliveryLogsQuery(), ct))).WithTags("Notifications");
        api.MapPost("/notifications/logs/{id:guid}/retry", async (Guid id, IMediator m, CancellationToken ct) =>
        { await m.Send(new RetryDeliveryCommand(id), ct); return Results.NoContent(); }).WithTags("Notifications");
    }
}
