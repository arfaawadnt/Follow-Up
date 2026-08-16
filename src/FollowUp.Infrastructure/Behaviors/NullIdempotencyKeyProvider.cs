using FollowUp.Application.Common.Abstractions;

namespace FollowUp.Infrastructure.Behaviors;

/// <summary>Default provider (jobs/tests): no idempotency key, so no dedup. The API overrides with a header-backed one.</summary>
public sealed class NullIdempotencyKeyProvider : IIdempotencyKeyProvider
{
    public string? CurrentKey => null;
}
