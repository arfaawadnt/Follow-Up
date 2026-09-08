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

        // Items is set eagerly so downstream (logging, the exception handler) can read the id.
        context.Items[Auth.CurrentUser.CorrelationItemKey] = correlationId;
        // The response header is written via OnStarting so it survives an error response: the
        // ExceptionHandlingMiddleware calls Response.Clear() before writing the problem body, which
        // wipes headers set eagerly here — OnStarting re-applies at flush time (finding PLT-013).
        context.Response.OnStarting(static state =>
        {
            var (response, id) = ((HttpResponse, string))state;
            response.Headers[Header] = id;
            return Task.CompletedTask;
        }, (context.Response, correlationId));
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
        // Applied via OnStarting so the headers are present on every response — including error
        // responses, where ExceptionHandlingMiddleware.Response.Clear() would otherwise strip
        // headers set eagerly before _next ran (finding PLT-013).
        context.Response.OnStarting(static state =>
        {
            var headers = ((HttpResponse)state).Headers;
            headers["X-Content-Type-Options"] = "nosniff";
            headers["X-Frame-Options"] = "DENY";
            headers["Referrer-Policy"] = "no-referrer";
            headers["Cross-Origin-Opener-Policy"] = "same-origin";
            headers["Content-Security-Policy"] =
                "default-src 'self'; script-src 'self'; " +
                "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; " +
                "font-src 'self' https://fonts.gstatic.com; " +
                "img-src 'self' data: https://*.tile.openstreetmap.org; " +
                "connect-src 'self' ws: wss:; frame-ancestors 'none'";
            return Task.CompletedTask;
        }, context.Response);
        await _next(context);
    }
}
