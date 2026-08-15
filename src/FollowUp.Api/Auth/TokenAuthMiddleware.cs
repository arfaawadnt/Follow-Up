using FollowUp.Application.Common.Abstractions;
using FollowUp.Application.Common.Abstractions.Persistence;
using FollowUp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FollowUp.Api.Auth;

/// <summary>
/// Authenticates a request from its bearer token (SRS FR-1). Validates the HMAC token, confirms the session
/// exists, is not revoked and not expired, then re-reads the user's role → privileges + org scope from the
/// database (never trusted from the token — NFR-SEC-2) and stashes them on the request. Also refreshes the
/// session's last-seen. Invalid/absent tokens simply leave the request unauthenticated.
/// </summary>
public sealed class TokenAuthMiddleware
{
    private readonly RequestDelegate _next;
    public TokenAuthMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        var token = ExtractToken(context);
        if (!string.IsNullOrEmpty(token))
            await TryAuthenticateAsync(context, token);

        await _next(context);
    }

    private static string? ExtractToken(HttpContext context)
    {
        var header = context.Request.Headers.Authorization.ToString();
        if (header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return header["Bearer ".Length..].Trim();
        // SignalR/EventSource can't set headers — accept the token from the query string for the hub only.
        if (context.Request.Path.StartsWithSegments("/hubs"))
            return context.Request.Query["access_token"].ToString() is { Length: > 0 } q ? q : null;
        return null;
    }

    private static async Task TryAuthenticateAsync(HttpContext context, string token)
    {
        var services = context.RequestServices;
        var tokens = services.GetRequiredService<ITokenService>();
        var clock = services.GetRequiredService<IClock>();

        var sessionId = tokens.ReadSessionId(token);
        if (sessionId is not { } sid) return;

        var sessions = services.GetRequiredService<IUserSessionRepository>();
        var session = await sessions.GetByIdAsync(sid, context.RequestAborted);
        if (session is null || !session.IsActive(clock.UtcNow)) return;
        if (session.TokenHash != tokens.HashToken(token)) return; // defense in depth

        var user = await services.GetRequiredService<IAppUserRepository>().GetByIdAsync(session.UserId, context.RequestAborted);
        if (user is null || !user.IsActive) return;

        var role = await services.GetRequiredService<IRoleRepository>().GetByIdAsync(user.RoleId, context.RequestAborted);
        if (role is null) return;

        context.Items[CurrentUser.ItemKey] = new CurrentUserState(
            user.Id, user.Username, role.Id, session.Id,
            role.EffectivePrivileges, role.Scope, user.RepresentativeId);

        // Refresh last-seen without change-tracking/audit (raw UPDATE).
        var db = services.GetRequiredService<FollowUpDbContext>();
        await db.Sessions.Where(s => s.Id == session.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.LastSeenAt, clock.UtcNow), context.RequestAborted);
    }
}
