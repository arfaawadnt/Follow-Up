using FollowUp.Domain.Common;
using MediatR;

namespace FollowUp.Application.Common.Messaging;

/// <summary>
/// Wraps a domain event as a MediatR notification so it can be published to handlers without the Domain
/// referencing MediatR. The Outbox dispatcher publishes these after commit; notification handlers react.
/// </summary>
public sealed record DomainEventNotification(IDomainEvent DomainEvent) : INotification;
