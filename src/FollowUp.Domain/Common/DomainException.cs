namespace FollowUp.Domain.Common;

/// <summary>
/// Thrown when a domain invariant or rule is violated (e.g. an illegal state transition).
/// The Api layer maps this to an RFC 7807 problem response — a caller error (4xx), never a 500.
/// </summary>
public class DomainException : Exception
{
    public DomainException(string message) : base(message) { }

    public DomainException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// Specialised <see cref="DomainException"/> for an attempt to move an aggregate through a
/// transition its state machine forbids. Mapped to HTTP 409 Conflict at the edge (per BR-11).
/// </summary>
public sealed class IllegalStateTransitionException : DomainException
{
    public IllegalStateTransitionException(string entity, string from, string to)
        : base($"Illegal {entity} transition: '{from}' → '{to}'.")
    {
        Entity = entity;
        From = from;
        To = to;
    }

    public string Entity { get; }
    public string From { get; }
    public string To { get; }
}
