using FollowUp.Application.Common.Abstractions;

namespace FollowUp.Api.Auth;

/// <summary>Reads the client's <c>Idempotency-Key</c> request header (architect idempotency).</summary>
public sealed class HttpIdempotencyKeyProvider : IIdempotencyKeyProvider
{
    private readonly IHttpContextAccessor _accessor;
    public HttpIdempotencyKeyProvider(IHttpContextAccessor accessor) => _accessor = accessor;

    public string? CurrentKey
    {
        get
        {
            var value = _accessor.HttpContext?.Request.Headers["Idempotency-Key"].ToString();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
    }
}
