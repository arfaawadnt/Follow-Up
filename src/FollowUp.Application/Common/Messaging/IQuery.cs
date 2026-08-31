using MediatR;

namespace FollowUp.Application.Common.Messaging;

/// <summary>A read-only query returning a projection (CQRS read side). Must not change state.</summary>
public interface IQuery<out TResponse> : IRequest<TResponse> { }

/// <summary>Handler for an <see cref="IQuery{TResponse}"/>.</summary>
public interface IQueryHandler<in TQuery, TResponse> : IRequestHandler<TQuery, TResponse>
    where TQuery : IQuery<TResponse>
{ }
