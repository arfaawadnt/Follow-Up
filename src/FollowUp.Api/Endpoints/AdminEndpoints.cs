using FollowUp.Application.Features.Audit;
using FollowUp.Application.Features.Setup;
using FollowUp.Application.Features.UserAdmin.Queries;
using FollowUp.Application.Features.UserAdmin.Roles;
using FollowUp.Application.Features.UserAdmin.Users;
using MediatR;

namespace FollowUp.Api.Endpoints;

public static class AdminEndpoints
{
    public static void MapUserAdminEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/users", async (int? page, int? pageSize, string? search, IMediator m, CancellationToken ct) =>
            Results.Ok(await m.Send(new GetUsersQuery(page ?? 1, pageSize ?? 50, search), ct))).WithTags("Users");
        api.MapGet("/users/lookup", async (string? search, IMediator m, CancellationToken ct) =>
            Results.Ok(await m.Send(new LookupUsersQuery(search), ct))).WithTags("Users");
        api.MapPost("/users", async (CreateUserCommand cmd, IMediator m, CancellationToken ct) =>
        { var id = await m.Send(cmd, ct); return Results.Created($"/api/v1/users/{id}", new { id }); }).WithTags("Users");
        api.MapPut("/users/{id:guid}", async (Guid id, UpdateUserCommand cmd, IMediator m, CancellationToken ct) =>
        { await m.Send(cmd with { Id = id }, ct); return Results.NoContent(); }).WithTags("Users");
        api.MapDelete("/users/{id:guid}", async (Guid id, IMediator m, CancellationToken ct) =>
        { await m.Send(new DeleteUserCommand(id), ct); return Results.NoContent(); }).WithTags("Users");
        api.MapPost("/users/{id:guid}/unlock", async (Guid id, IMediator m, CancellationToken ct) =>
        { await m.Send(new UnlockUserCommand(id), ct); return Results.NoContent(); }).WithTags("Users");
        api.MapPost("/users/{id:guid}/role", async (Guid id, ChangeRoleBody b, IMediator m, CancellationToken ct) =>
        { await m.Send(new ChangeUserRoleCommand(id, b.RoleId), ct); return Results.NoContent(); }).WithTags("Users");

        // Roles
        api.MapGet("/setup/roles", async (IMediator m, CancellationToken ct) =>
            Results.Ok(await m.Send(new GetRolesQuery(), ct))).WithTags("Roles");
        api.MapPost("/setup/roles", async (CreateRoleCommand cmd, IMediator m, CancellationToken ct) =>
        { var id = await m.Send(cmd, ct); return Results.Created($"/api/v1/setup/roles/{id}", new { id }); }).WithTags("Roles");
        api.MapPut("/setup/roles/{id:guid}", async (Guid id, UpdateRoleCommand cmd, IMediator m, CancellationToken ct) =>
        { await m.Send(cmd with { Id = id }, ct); return Results.NoContent(); }).WithTags("Roles");
        api.MapDelete("/setup/roles/{id:guid}", async (Guid id, IMediator m, CancellationToken ct) =>
        { await m.Send(new DeleteRoleCommand(id), ct); return Results.NoContent(); }).WithTags("Roles");
    }

    public static void MapSetupEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/refs", async (string? type, IMediator m, CancellationToken ct) =>
            Results.Ok(await m.Send(new GetRefItemsQuery(type), ct))).WithTags("Setup");
        api.MapGet("/setup/refs", async (string? type, IMediator m, CancellationToken ct) =>
            Results.Ok(await m.Send(new GetRefItemsQuery(type), ct))).WithTags("Setup");
        api.MapPost("/setup/refs", async (CreateRefItemCommand cmd, IMediator m, CancellationToken ct) =>
        { var id = await m.Send(cmd, ct); return Results.Created($"/api/v1/setup/refs/{id}", new { id }); }).WithTags("Setup");
        api.MapDelete("/setup/refs/{id:guid}", async (Guid id, IMediator m, CancellationToken ct) =>
        { await m.Send(new DeleteRefItemCommand(id), ct); return Results.NoContent(); }).WithTags("Setup");

        api.MapGet("/setup/cities", async (IMediator m, CancellationToken ct) =>
            Results.Ok(await m.Send(new GetCitiesQuery(), ct))).WithTags("Setup");
        api.MapPost("/setup/cities", async (CreateCityCommand cmd, IMediator m, CancellationToken ct) =>
        { var id = await m.Send(cmd, ct); return Results.Created($"/api/v1/setup/cities/{id}", new { id }); }).WithTags("Setup");
        api.MapDelete("/setup/cities/{id:guid}", async (Guid id, IMediator m, CancellationToken ct) =>
        { await m.Send(new DeleteCityCommand(id), ct); return Results.NoContent(); }).WithTags("Setup");

        api.MapGet("/setup/areas", async (IMediator m, CancellationToken ct) =>
            Results.Ok(await m.Send(new GetAreasQuery(), ct))).WithTags("Setup");
        api.MapPost("/setup/areas", async (CreateAreaCommand cmd, IMediator m, CancellationToken ct) =>
        { var id = await m.Send(cmd, ct); return Results.Created($"/api/v1/setup/areas/{id}", new { id }); }).WithTags("Setup");
        api.MapDelete("/setup/areas/{id:guid}", async (Guid id, IMediator m, CancellationToken ct) =>
        { await m.Send(new DeleteAreaCommand(id), ct); return Results.NoContent(); }).WithTags("Setup");
    }

    public sealed record SettingBody(string? Value, bool IsSecret);
    public sealed record RetentionBody(int Days);
    public sealed record ChangeRoleBody(Guid RoleId);

    public static void MapSettingsAndRetentionEndpoints(this RouteGroupBuilder api)
    {
        // Application settings (FR-2) — secrets masked on read.
        api.MapGet("/settings", async (IMediator m, CancellationToken ct) =>
            Results.Ok(await m.Send(new GetSettingsQuery(), ct))).WithTags("Settings");
        api.MapPut("/settings/{key}", async (string key, SettingBody b, IMediator m, CancellationToken ct) =>
        { await m.Send(new UpsertSettingCommand(key, b.Value, b.IsSecret), ct); return Results.NoContent(); }).WithTags("Settings");

        // Data retention (FR-18).
        api.MapGet("/setup/retention", async (IMediator m, CancellationToken ct) =>
            Results.Ok(await m.Send(new GetRetentionQuery(), ct))).WithTags("Setup");
        api.MapPut("/setup/retention", async (RetentionBody b, IMediator m, CancellationToken ct) =>
        { await m.Send(new SetRetentionCommand(b.Days), ct); return Results.NoContent(); }).WithTags("Setup");
        api.MapPost("/setup/retention/run", async (IMediator m, CancellationToken ct) =>
            Results.Ok(new { purged = await m.Send(new RunRetentionCommand(), ct) })).WithTags("Setup");
    }

    public static void MapAuditEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/audit", async (int? page, int? pageSize, string? entity, string? actor, string? action, IMediator m, CancellationToken ct) =>
            Results.Ok(await m.Send(new GetAuditQuery { Page = page ?? 1, PageSize = pageSize ?? 50, Entity = entity, Actor = actor, Action = action }, ct))).WithTags("Audit");
    }
}
