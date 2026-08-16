using FollowUp.Application.Features.Auth;
using FollowUp.Application.Features.UserAdmin.Users;
using MediatR;

namespace FollowUp.Api.Endpoints;

public static class AuthEndpoints
{
    public sealed record LoginRequest(string Username, string Password);
    public sealed record ChangePasswordRequest(string OldPassword, string NewPassword);
    public sealed record LanguageRequest(string Language);

    public static void MapAuthEndpoints(this RouteGroupBuilder api)
    {
        api.MapPost("/auth/login", async (LoginRequest req, HttpContext http, IMediator m, CancellationToken ct) =>
        {
            var ip = http.Connection.RemoteIpAddress?.ToString();
            var ua = http.Request.Headers.UserAgent.ToString();
            var result = await m.Send(new LoginCommand(req.Username, req.Password, ip, ua), ct);
            return Results.Ok(result);
        }).WithTags("Auth").AllowAnonymous().RequireRateLimiting("login");

        api.MapPost("/auth/logout", async (IMediator m, CancellationToken ct) =>
        {
            await m.Send(new LogoutCommand(), ct);
            return Results.NoContent();
        }).WithTags("Auth");

        api.MapGet("/sessions", async (IMediator m, CancellationToken ct) =>
            Results.Ok(await m.Send(new GetMySessionsQuery(), ct))).WithTags("Auth");

        api.MapPost("/user/change-password", async (ChangePasswordRequest req, IMediator m, CancellationToken ct) =>
        {
            await m.Send(new ChangeOwnPasswordCommand(req.OldPassword, req.NewPassword), ct);
            return Results.NoContent();
        }).WithTags("Auth");

        api.MapPut("/user/language", async (LanguageRequest req, IMediator m, CancellationToken ct) =>
        {
            await m.Send(new SetOwnLanguageCommand(req.Language), ct);
            return Results.NoContent();
        }).WithTags("Auth");
    }
}
