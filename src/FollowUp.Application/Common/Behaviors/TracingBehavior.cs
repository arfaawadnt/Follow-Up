using System.Diagnostics;
using MediatR;

namespace FollowUp.Application.Common.Behaviors;

/// <summary>
/// Emits one OpenTelemetry span per MediatR request under the "FollowUp" source (finding M-19), so every
/// command/query is traced end-to-end and correlates with the AspNetCore, HTTP-client and Npgsql spans. The
/// outermost behavior, so the span covers authorization, validation and the handler.
/// </summary>
public sealed class TracingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        using var activity = AppTelemetry.Source.StartActivity(typeof(TRequest).Name, ActivityKind.Internal);
        activity?.SetTag("followup.request", typeof(TRequest).Name);
        try
        {
            return await next();
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }
}
