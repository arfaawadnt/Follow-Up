using FollowUp.Application.Common.Abstractions.Persistence;
using FollowUp.Application.Features.Laboratories.ChangeLaboratoryStatus;
using FollowUp.Application.Features.Laboratories.CreateLaboratory;
using FollowUp.Application.Features.Laboratories.GetLaboratories;
using FollowUp.Application.Features.Laboratories.GetLaboratoryById;
using FollowUp.Application.Features.Representatives.Contracts;
using FollowUp.Application.Features.Representatives.CreateRepresentative;
using FollowUp.Application.Features.Representatives.GetRepresentatives;
using MediatR;

namespace FollowUp.Api.Endpoints;

public static class LaboratoryEndpoints
{
    public sealed record StatusRequest(string Status);

    public static void MapLaboratoryEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/labs", async (int? page, int? pageSize, string? search, string? status, string? segment,
            string? governorate, IMediator m, CancellationToken ct) =>
            Results.Ok(await m.Send(new GetLaboratoriesQuery
            {
                Page = page ?? 1, PageSize = pageSize ?? 50, Search = search,
                Status = status, Segment = segment, Governorate = governorate,
            }, ct))).WithTags("Laboratories");

        api.MapGet("/labs/{id:guid}", async (Guid id, IMediator m, CancellationToken ct) =>
            Results.Ok(await m.Send(new GetLaboratoryByIdQuery(id), ct))).WithTags("Laboratories");

        api.MapPost("/labs", async (CreateLaboratoryCommand cmd, IMediator m, CancellationToken ct) =>
        {
            var id = await m.Send(cmd, ct);
            return Results.Created($"/api/v1/labs/{id}", new { id });
        }).WithTags("Laboratories");

        api.MapPut("/labs/{id:guid}/status", async (Guid id, StatusRequest req, IMediator m, CancellationToken ct) =>
        {
            await m.Send(new ChangeLaboratoryStatusCommand(id, req.Status), ct);
            return Results.NoContent();
        }).WithTags("Laboratories");

        api.MapGet("/labs/nextcode", async (ILaboratoryRepository repo, CancellationToken ct) =>
            Results.Ok(new { code = await repo.NextCodeAsync(ct) })).WithTags("Laboratories");

        // Representatives
        api.MapGet("/reps", async (int? page, int? pageSize, string? search, string? type, bool? activeOnly,
            IMediator m, CancellationToken ct) =>
            Results.Ok(await m.Send(new GetRepresentativesQuery
            {
                Page = page ?? 1, PageSize = pageSize ?? 50, Search = search, Type = type, ActiveOnly = activeOnly,
            }, ct))).WithTags("Representatives");

        api.MapPost("/reps", async (CreateRepresentativeCommand cmd, IMediator m, CancellationToken ct) =>
        {
            var id = await m.Send(cmd, ct);
            return Results.Created($"/api/v1/reps/{id}", new { id });
        }).WithTags("Representatives");
    }
}
