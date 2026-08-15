namespace FollowUp.Api.Middleware;

/// <summary>Assigns/propagates a correlation id for every request (SRS NFR-OBS-1).</summary>
public sealed class CorrelationMiddleware
{
    private const string Header = "X-Correlation-ID";
    private readonly RequestDelegate _next;
    public CorrelationMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers.TryGetValue(Header, out var provided) && !string.IsNullOrWhiteSpace(provided)
            ? provided.ToString()
            : Guid.NewGuid().ToString("N");

        context.Items[Auth.CurrentUser.CorrelationItemKey] = correlationId;
        context.Response.Headers[Header] = correlationId;
        await _next(context);
    }
}

/// <summary>Applies baseline security response headers and a script-src 'self' CSP (SRS NFR-SEC-5).</summary>
public sealed class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;
    public SecurityHeadersMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;
        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "DENY";
        headers["Referrer-Policy"] = "no-referrer";
        headers["Cross-Origin-Opener-Policy"] = "same-origin";
        headers["Content-Security-Policy"] =
            "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; " +
            "img-src 'self' data: https://*.tile.openstreetmap.org; connect-src 'self'; frame-ancestors 'none'";
        await _next(context);
    }
}
