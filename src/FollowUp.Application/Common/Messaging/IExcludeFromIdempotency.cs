namespace FollowUp.Application.Common.Messaging;

/// <summary>
/// Marks a command that must never participate in idempotency caching — its response is not stored and never
/// replayed. Used for anonymous/credential-bearing commands such as login: caching would persist the issued
/// bearer token in plaintext in the idempotency store and could replay a stale token (finding IDN-2). Every
/// login must re-authenticate and mint a fresh session.
/// </summary>
public interface IExcludeFromIdempotency { }
