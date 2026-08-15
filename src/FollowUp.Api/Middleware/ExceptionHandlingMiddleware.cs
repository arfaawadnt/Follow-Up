using FollowUp.Application.Common.Exceptions;
using FollowUp.Domain.Common;
using Microsoft.AspNetCore.Mvc;

namespace FollowUp.Api.Middleware;

/// <summary>
/// Translates every exception into a single RFC 7807 Problem Details shape (SRS NFR-UX-4). Caller errors are
/// 4xx (never surfaced as 500); server faults are logged with the correlation id and returned as an opaque
/// 500 — no stack traces, SQL, or internal names leak (architect API rules).
/// </summary>
public sealed class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await WriteProblemAsync(context, ex);
        }
    }

    private async Task WriteProblemAsync(HttpContext context, Exception ex)
    {
        var correlationId = context.Items[Auth.CurrentUser.CorrelationItemKey] as string;
        var (status, title, detail, errors) = Map(ex);

        if (status >= 500)
            _logger.LogError(ex, "Unhandled error (corr {CorrelationId})", correlationId);
        else
            _logger.LogWarning("Request error {Status}: {Title} (corr {CorrelationId})", status, title, correlationId);

        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = detail,
            Type = $"https://httpstatuses.io/{status}",
        };
        problem.Extensions["correlationId"] = correlationId;
        if (errors is not null) problem.Extensions["errors"] = errors;

        context.Response.Clear();
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(problem, context.RequestAborted);
    }

    private static (int Status, string Title, string? Detail, IReadOnlyDictionary<string, string[]>? Errors) Map(Exception ex) => ex switch
    {
        ValidationException v => (StatusCodes.Status400BadRequest, "Validation failed", v.Message, v.Errors),
        UnauthorizedException => (StatusCodes.Status401Unauthorized, "Unauthorized", ex.Message, null),
        ForbiddenException => (StatusCodes.Status403Forbidden, "Forbidden", ex.Message, null),
        NotFoundException => (StatusCodes.Status404NotFound, "Not found", ex.Message, null),
        ConflictException => (StatusCodes.Status409Conflict, "Conflict", ex.Message, null),
        IllegalStateTransitionException => (StatusCodes.Status409Conflict, "Illegal state transition", ex.Message, null),
        DomainException => (StatusCodes.Status400BadRequest, "Invalid operation", ex.Message, null),
        _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred", null, null),
    };
}
