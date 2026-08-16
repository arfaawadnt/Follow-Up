namespace FollowUp.Application.Common.Abstractions;

/// <summary>
/// Supplies the current request's idempotency key (architect: "commands must be idempotent when they may be
/// retried"). The API reads an <c>Idempotency-Key</c> header; jobs/tests return null (no dedup). When a key is
/// present, a retried command returns the first execution's result instead of running twice.
/// </summary>
public interface IIdempotencyKeyProvider
{
    string? CurrentKey { get; }
}
