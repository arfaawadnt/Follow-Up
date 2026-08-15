using System.Diagnostics;
using FollowUp.Application.Common.Abstractions;
using MediatR;
using Microsoft.Extensions.Logging;

namespace FollowUp.Application.Common.Behaviors;

/// <summary>
/// Structured request logging + performance timing (SRS NFR-OBS-1/2; architect observability). Enriches
/// each use-case with correlation id, user, operation name and duration/outcome. Request bodies are never
/// logged (they may carry sensitive data). Tracing spans are added by OpenTelemetry auto-instrumentation of
/// MediatR in Infrastructure; this behavior emits the correlated structured log line.
/// </summary>
public sealed class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private const int SlowMilliseconds = 1000; // NFR-PERF-1: warn beyond ~1s

    private readonly ILogger<LoggingBehavior<TRequest, TResponse>> _logger;
    private readonly ICurrentUser _currentUser;

    public LoggingBehavior(ILogger<LoggingBehavior<TRequest, TResponse>> logger, ICurrentUser currentUser)
    {
        _logger = logger;
        _currentUser = currentUser;
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        var operation = typeof(TRequest).Name;
        var user = _currentUser.IsAuthenticated ? _currentUser.Username : "anonymous";
        var sw = Stopwatch.StartNew();
        try
        {
            var response = await next();
            sw.Stop();
            if (sw.ElapsedMilliseconds >= SlowMilliseconds)
                _logger.LogWarning("Use-case {Operation} by {User} completed SLOW in {Elapsed}ms (corr {CorrelationId})",
                    operation, user, sw.ElapsedMilliseconds, _currentUser.CorrelationId);
            else
                _logger.LogInformation("Use-case {Operation} by {User} completed in {Elapsed}ms (corr {CorrelationId})",
                    operation, user, sw.ElapsedMilliseconds, _currentUser.CorrelationId);
            return response;
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogWarning(ex, "Use-case {Operation} by {User} failed after {Elapsed}ms (corr {CorrelationId})",
                operation, user, sw.ElapsedMilliseconds, _currentUser.CorrelationId);
            throw;
        }
    }
}
