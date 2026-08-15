using MediatR;

namespace FollowUp.Application.Common.Messaging;

/// <summary>A command that changes state and returns a result (CQRS write side).</summary>
public interface ICommand<out TResponse> : IRequest<TResponse> { }

/// <summary>A command that changes state without returning a payload.</summary>
public interface ICommand : IRequest<Unit> { }

/// <summary>Handler for a <see cref="ICommand{TResponse}"/>.</summary>
public interface ICommandHandler<in TCommand, TResponse> : IRequestHandler<TCommand, TResponse>
    where TCommand : ICommand<TResponse> { }

/// <summary>Handler for a parameterless-result <see cref="ICommand"/>.</summary>
public interface ICommandHandler<in TCommand> : IRequestHandler<TCommand, Unit>
    where TCommand : ICommand { }
