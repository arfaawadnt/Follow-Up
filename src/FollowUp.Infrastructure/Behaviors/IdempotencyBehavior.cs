using System.Text.Json;
using FollowUp.Application.Common.Abstractions;
using FollowUp.Application.Common.Messaging;
using FollowUp.Infrastructure.Persistence;
using FollowUp.Infrastructure.Persistence.Idempotency;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FollowUp.Infrastructure.Behaviors;

/// <summary>
/// Makes retried commands idempotent (architect data-consistency rule). When the request carries an
/// <c>Idempotency-Key</c>, a first execution records the key + serialized result; a retry with the same key
/// short-circuits and returns the recorded result instead of running the handler again. Runs innermost, inside
/// the transaction, so the key and the command's effect commit together. Queries and keyless commands pass through.
/// </summary>
public sealed class IdempotencyBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly FollowUpDbContext _db;
    private readonly IIdempotencyKeyProvider _keys;
    private readonly ILogger<IdempotencyBehavior<TRequest, TResponse>> _logger;

    public IdempotencyBehavior(FollowUpDbContext db, IIdempotencyKeyProvider keys,
        ILogger<IdempotencyBehavior<TRequest, TResponse>> logger)
    {
        _db = db;
        _keys = keys;
        _logger = logger;
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        var key = _keys.CurrentKey;
        // Excluded commands (e.g. login) never cache a response — that would persist a bearer token in the
        // idempotency store and could replay a stale one (finding IDN-2).
        if (request is not IBaseCommand || request is IExcludeFromIdempotency || string.IsNullOrWhiteSpace(key))
            return await next();

        var existing = await _db.IdempotencyRecords.AsNoTracking().FirstOrDefaultAsync(r => r.Key == key, ct);
        if (existing is not null)
        {
            _logger.LogInformation("Idempotent replay for key {Key} ({Request})", key, typeof(TRequest).Name);
            return existing.ResponseJson is null ? default! : JsonSerializer.Deserialize<TResponse>(existing.ResponseJson)!;
        }

        var response = await next();

        _db.IdempotencyRecords.Add(new IdempotencyRecord
        {
            Key = key!,
            RequestType = typeof(TRequest).Name,
            ResponseJson = response is null or Unit ? null : JsonSerializer.Serialize(response),
            CreatedAt = DateTimeOffset.UtcNow,
        });
        return response;
    }
}
