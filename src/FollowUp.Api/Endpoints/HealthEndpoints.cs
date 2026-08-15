using FollowUp.Application.Features.Platform;
using FollowUp.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FollowUp.Api.Endpoints;

/// <summary>Anonymous platform/health endpoints (SRS FR-21). Unversioned infrastructure contracts.</summary>
public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/healthz/live", () => Results.Ok(new { status = "live" }))
            .WithTags("Platform").AllowAnonymous();

        app.MapGet("/healthz/ready", async (FollowUpDbContext db, CancellationToken ct) =>
        {
            try
            {
                await db.Database.ExecuteSqlRawAsync("SELECT 1", ct);
                return Results.Ok(new { status = "ready" });
            }
            catch
            {
                return Results.Json(new { status = "not-ready" }, statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        }).WithTags("Platform").AllowAnonymous();

        app.MapGet("/healthz/version", () => Results.Ok(new
        {
            version = typeof(HealthEndpoints).Assembly.GetName().Version?.ToString() ?? "0.0.0",
            service = "FollowUp",
        })).WithTags("Platform").AllowAnonymous();

        return app;
    }

    public static void MapMapsEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/maps/resolve-redirect", async (string url, IMediator m, CancellationToken ct) =>
            Results.Ok(new { target = await m.Send(new ResolveMapLinkQuery(url), ct) }))
            .WithTags("Platform");
    }
}
