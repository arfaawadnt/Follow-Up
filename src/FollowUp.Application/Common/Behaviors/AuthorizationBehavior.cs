using FollowUp.Application.Common.Abstractions;
using FollowUp.Application.Common.Exceptions;
using FollowUp.Application.Common.Messaging;
using MediatR;

namespace FollowUp.Application.Common.Behaviors;

/// <summary>
/// Backend authorization inside the application pipeline (SRS Authorization Layer 2). For any request that
/// declares <see cref="IAuthorizedRequest"/>, requires an authenticated caller holding at least one of the
/// listed privileges (else 403). Record-level org-scope/ownership is enforced inside handlers, which have
/// the loaded aggregate. Privileges come from <see cref="ICurrentUser"/>, re-read from the DB each request.
/// </summary>
public sealed class AuthorizationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly ICurrentUser _currentUser;

    public AuthorizationBehavior(ICurrentUser currentUser) => _currentUser = currentUser;

    public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        if (request is IAuthorizedRequest authorized)
        {
            if (!_currentUser.IsAuthenticated)
                throw new UnauthorizedException();

            var required = authorized.RequiredPrivileges;
            if (required.Count > 0 && !required.Any(_currentUser.Has))
                throw new ForbiddenException();
        }

        return next();
    }
}
