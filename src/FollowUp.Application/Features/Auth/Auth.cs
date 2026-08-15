using FollowUp.Application.Common.Abstractions;
using FollowUp.Application.Common.Abstractions.Persistence;
using FollowUp.Application.Common.Exceptions;
using FollowUp.Application.Common.Messaging;
using FollowUp.Domain.Identity;
using FluentValidation;
using MediatR;

namespace FollowUp.Application.Features.Auth;

// ---- Login (anonymous) ----

public sealed record LoginCommand(string Username, string Password, string? Ip, string? UserAgent) : ICommand<LoginResult>;

public sealed record LoginResult(
    string Token,
    DateTimeOffset ExpiresAt,
    string Username,
    string RoleName,
    IReadOnlyList<string> Privileges,
    ScopeView Scope);

public sealed record ScopeView(
    IReadOnlyList<string> Branches, IReadOnlyList<string> Governorates, IReadOnlyList<string> Cities,
    IReadOnlyList<string> Areas, IReadOnlyList<string> Categories, IReadOnlyList<string> Segments)
{
    public static ScopeView From(OrgScope s) => new(
        s.Branches.ToArray(), s.Governorates.ToArray(), s.Cities.ToArray(),
        s.Areas.ToArray(), s.Categories.ToArray(), s.Segments.ToArray());
}

public sealed class LoginValidator : AbstractValidator<LoginCommand>
{
    public LoginValidator()
    {
        RuleFor(x => x.Username).NotEmpty();
        RuleFor(x => x.Password).NotEmpty();
    }
}

public sealed class LoginHandler : ICommandHandler<LoginCommand, LoginResult>
{
    private readonly IAppUserRepository _users;
    private readonly IRoleRepository _roles;
    private readonly IUserSessionRepository _sessions;
    private readonly IPasswordHasher _hasher;
    private readonly ITokenService _tokens;
    private readonly IAuthPolicy _policy;
    private readonly IClock _clock;

    public LoginHandler(IAppUserRepository users, IRoleRepository roles, IUserSessionRepository sessions,
        IPasswordHasher hasher, ITokenService tokens, IAuthPolicy policy, IClock clock)
    {
        _users = users; _roles = roles; _sessions = sessions;
        _hasher = hasher; _tokens = tokens; _policy = policy; _clock = clock;
    }

    public async Task<LoginResult> Handle(LoginCommand request, CancellationToken ct)
    {
        var now = _clock.UtcNow;
        var user = await _users.GetByUsernameAsync(request.Username, ct);

        // Uniform failure for unknown user vs bad password (don't disclose which).
        if (user is null || !user.IsActive)
            throw new UnauthorizedException("Invalid username or password.");

        if (user.IsLockedOut(now))
            throw new UnauthorizedException("The account is temporarily locked. Try again later.");

        if (!_hasher.Verify(request.Password, user.Password))
        {
            user.RegisterFailedLogin(_policy.MaxFailedAttempts, _policy.LockoutWindow, now);
            throw new UnauthorizedException("Invalid username or password.");
        }

        user.RegisterSuccessfulLogin();

        var role = await _roles.GetByIdAsync(user.RoleId, ct)
            ?? throw new UnauthorizedException("The account has no valid role.");

        // Generate the session id first so it can be embedded in the signed token; store only the token hash.
        var sessionId = UserSessionId.New();
        var issued = _tokens.Issue(user.Id, sessionId, now);
        var session = UserSession.Issue(sessionId, user.Id, issued.TokenHash, now, issued.ExpiresAt,
            request.Ip, request.UserAgent);
        _sessions.Add(session);

        return new LoginResult(issued.Token, issued.ExpiresAt, user.Username, role.Name,
            role.EffectivePrivileges.OrderBy(p => p).ToArray(), ScopeView.From(role.Scope));
    }
}

// ---- Logout ----

public sealed record LogoutCommand : ICommand, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = Array.Empty<string>(); // authenticated only
}

public sealed class LogoutHandler : ICommandHandler<LogoutCommand>
{
    private readonly IUserSessionRepository _sessions;
    private readonly ICurrentUser _caller;
    private readonly IClock _clock;

    public LogoutHandler(IUserSessionRepository sessions, ICurrentUser caller, IClock clock)
    {
        _sessions = sessions; _caller = caller; _clock = clock;
    }

    public async Task<Unit> Handle(LogoutCommand request, CancellationToken ct)
    {
        if (_caller.SessionId is not { } sessionId)
            return Unit.Value; // nothing to revoke

        var session = await _sessions.GetByIdAsync(sessionId, ct);
        session?.Revoke(_clock.UtcNow);
        return Unit.Value;
    }
}

// ---- Sessions (self) ----

public sealed record SessionDto(Guid Id, DateTimeOffset IssuedAt, DateTimeOffset LastSeenAt,
    DateTimeOffset ExpiresAt, bool Revoked, string? Ip);

public interface ISessionQueries
{
    Task<IReadOnlyList<SessionDto>> GetForUserAsync(AppUserId userId, CancellationToken ct);
}

public sealed record GetMySessionsQuery : IQuery<IReadOnlyList<SessionDto>>, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = Array.Empty<string>();
}

public sealed class GetMySessionsHandler : IQueryHandler<GetMySessionsQuery, IReadOnlyList<SessionDto>>
{
    private readonly ISessionQueries _queries;
    private readonly ICurrentUser _caller;

    public GetMySessionsHandler(ISessionQueries queries, ICurrentUser caller) { _queries = queries; _caller = caller; }

    public Task<IReadOnlyList<SessionDto>> Handle(GetMySessionsQuery request, CancellationToken ct) =>
        _queries.GetForUserAsync(_caller.UserId, ct);
}
