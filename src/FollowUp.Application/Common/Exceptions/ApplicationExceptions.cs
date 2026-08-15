namespace FollowUp.Application.Common.Exceptions;

/// <summary>Requested resource does not exist (→ HTTP 404).</summary>
public sealed class NotFoundException : Exception
{
    public NotFoundException(string resource, object key)
        : base($"{resource} '{key}' was not found.") { }

    public NotFoundException(string message) : base(message) { }
}

/// <summary>Caller is authenticated but lacks the privilege or scope for this action (→ HTTP 403).</summary>
public sealed class ForbiddenException : Exception
{
    public ForbiddenException(string message = "You do not have permission to perform this action.")
        : base(message) { }
}

/// <summary>Caller is not authenticated (→ HTTP 401).</summary>
public sealed class UnauthorizedException : Exception
{
    public UnauthorizedException(string message = "Authentication is required.") : base(message) { }
}

/// <summary>A concurrency/state conflict, e.g. a stale optimistic-concurrency token (→ HTTP 409).</summary>
public sealed class ConflictException : Exception
{
    public ConflictException(string message) : base(message) { }
}

/// <summary>Input validation failed (→ HTTP 400). Carries per-field errors for the Problem Details payload.</summary>
public sealed class ValidationException : Exception
{
    public ValidationException(IReadOnlyDictionary<string, string[]> errors)
        : base("One or more validation errors occurred.")
        => Errors = errors;

    public IReadOnlyDictionary<string, string[]> Errors { get; }
}
