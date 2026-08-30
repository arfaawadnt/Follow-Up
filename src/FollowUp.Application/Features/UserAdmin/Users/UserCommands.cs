using FollowUp.Application.Common.Abstractions;
using FollowUp.Application.Common.Abstractions.Persistence;
using FollowUp.Application.Common.Exceptions;
using FollowUp.Application.Common.Messaging;
using FollowUp.Application.Common.Security;
using FollowUp.Domain.Identity;
using FollowUp.Domain.Representatives;
using FluentValidation;
using MediatR;

namespace FollowUp.Application.Features.UserAdmin.Users;

/// <summary>Shared anti-amplification check when assigning a role to a user (BR-12).</summary>
internal static class RoleGrantSupport
{
    public static async Task<Role> LoadGrantableRoleAsync(Guid roleId, IRoleRepository roles, ICurrentUser caller, CancellationToken ct)
    {
        var role = await roles.GetByIdAsync(new RoleId(roleId), ct)
            ?? throw new NotFoundException("Role", roleId);
        // Assigning a role grants its privileges/scope; the caller must hold at least those.
        caller.EnsurePrivilegesWithinGrant(role.Privileges);
        caller.EnsureScopeWithinGrant(role.Scope);
        return role;
    }
}

// ---- Create user ----

public sealed record CreateUserCommand : ICommand<Guid>, IAuthorizedRequest
{
    public string Username { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
    public Guid RoleId { get; init; }
    public string? DisplayName { get; init; }
    public string? Email { get; init; }
    public string? Phone { get; init; }
    public string Language { get; init; } = "en";
    public Guid? RepresentativeId { get; init; }

    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ManageUsers };
}

public sealed class CreateUserValidator : AbstractValidator<CreateUserCommand>
{
    public CreateUserValidator()
    {
        RuleFor(x => x.Username).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Password).StrongPassword(); // length + complexity + deny-list (IDN-10)
        RuleFor(x => x.RoleId).NotEmpty();
    }
}

public sealed class CreateUserHandler : ICommandHandler<CreateUserCommand, Guid>
{
    private readonly IAppUserRepository _users;
    private readonly IRoleRepository _roles;
    private readonly ICurrentUser _caller;
    private readonly IPasswordHasher _hasher;

    public CreateUserHandler(IAppUserRepository users, IRoleRepository roles, ICurrentUser caller, IPasswordHasher hasher)
    {
        _users = users; _roles = roles; _caller = caller; _hasher = hasher;
    }

    public async Task<Guid> Handle(CreateUserCommand request, CancellationToken ct)
    {
        var role = await RoleGrantSupport.LoadGrantableRoleAsync(request.RoleId, _roles, _caller, ct);

        if (await _users.UsernameExistsAsync(request.Username, ct))
            throw new ConflictException($"Username '{request.Username}' is already taken.");

        RepresentativeId? repId = request.RepresentativeId is { } r ? new RepresentativeId(r) : null;
        if (repId is { } linked && await _users.AnyLinkedToRepAsync(linked, ct))
            throw new ConflictException("That representative already has a login account.");

        var user = AppUser.Create(request.Username, _hasher.Hash(request.Password), role.Id);
        user.SetProfile(request.Email, request.Phone);
        user.SetDisplayName(request.DisplayName);
        user.SetLanguage(request.Language);
        if (repId is { } rid) user.LinkRepresentative(rid);

        _users.Add(user);
        return user.Id.Value;
    }
}

// ---- Update user ----

public sealed record UpdateUserCommand : ICommand, IAuthorizedRequest
{
    public Guid Id { get; init; }
    public Guid RoleId { get; init; }
    public string? DisplayName { get; init; }
    public string? Email { get; init; }
    public string? Phone { get; init; }
    public string Language { get; init; } = "en";
    public Guid? RepresentativeId { get; init; }

    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ManageUsers };
}

public sealed class UpdateUserHandler : ICommandHandler<UpdateUserCommand>
{
    private readonly IAppUserRepository _users;
    private readonly IRoleRepository _roles;
    private readonly ICurrentUser _caller;

    public UpdateUserHandler(IAppUserRepository users, IRoleRepository roles, ICurrentUser caller)
    {
        _users = users; _roles = roles; _caller = caller;
    }

    public async Task<Unit> Handle(UpdateUserCommand request, CancellationToken ct)
    {
        var user = await _users.GetByIdAsync(new AppUserId(request.Id), ct)
            ?? throw new NotFoundException("User", request.Id);

        // Self-role-change is blocked (SRS §2.3).
        if (_caller.UserId == user.Id && new RoleId(request.RoleId) != user.RoleId)
            throw new ForbiddenException("You cannot change your own role.");

        var role = await RoleGrantSupport.LoadGrantableRoleAsync(request.RoleId, _roles, _caller, ct);

        user.ChangeRole(role.Id);
        user.SetProfile(request.Email, request.Phone);
        user.SetDisplayName(request.DisplayName);
        user.SetLanguage(request.Language);
        user.LinkRepresentative(request.RepresentativeId is { } r ? new RepresentativeId(r) : null);
        return Unit.Value;
    }
}

// ---- Change role only (preserves profile: phone, representative link, language) ----

public sealed record ChangeUserRoleCommand(Guid Id, Guid RoleId) : ICommand, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ManageUsers };
}

public sealed class ChangeUserRoleHandler : ICommandHandler<ChangeUserRoleCommand>
{
    private readonly IAppUserRepository _users;
    private readonly IRoleRepository _roles;
    private readonly ICurrentUser _caller;

    public ChangeUserRoleHandler(IAppUserRepository users, IRoleRepository roles, ICurrentUser caller)
    {
        _users = users; _roles = roles; _caller = caller;
    }

    public async Task<Unit> Handle(ChangeUserRoleCommand request, CancellationToken ct)
    {
        var user = await _users.GetByIdAsync(new AppUserId(request.Id), ct)
            ?? throw new NotFoundException("User", request.Id);

        // Self-role-change is blocked (SRS §2.3).
        if (_caller.UserId == user.Id && new RoleId(request.RoleId) != user.RoleId)
            throw new ForbiddenException("You cannot change your own role.");

        var role = await RoleGrantSupport.LoadGrantableRoleAsync(request.RoleId, _roles, _caller, ct);
        user.ChangeRole(role.Id);
        return Unit.Value;
    }
}

// ---- Delete user ----

public sealed record DeleteUserCommand(Guid Id) : ICommand, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ManageUsers };
}

public sealed class DeleteUserHandler : ICommandHandler<DeleteUserCommand>
{
    private readonly IAppUserRepository _users;
    private readonly ICurrentUser _caller;

    public DeleteUserHandler(IAppUserRepository users, ICurrentUser caller) { _users = users; _caller = caller; }

    public async Task<Unit> Handle(DeleteUserCommand request, CancellationToken ct)
    {
        var user = await _users.GetByIdAsync(new AppUserId(request.Id), ct)
            ?? throw new NotFoundException("User", request.Id);

        if (_caller.UserId == user.Id)
            throw new ConflictException("You cannot delete your own account.");
        if (user.IsBuiltIn)
            throw new ConflictException("The built-in administrator account cannot be deleted.");

        _users.Remove(user);
        return Unit.Value;
    }
}

// ---- Unlock user ----

public sealed record UnlockUserCommand(Guid Id) : ICommand, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ManageUsers };
}

public sealed class UnlockUserHandler : ICommandHandler<UnlockUserCommand>
{
    private readonly IAppUserRepository _users;

    public UnlockUserHandler(IAppUserRepository users) => _users = users;

    public async Task<Unit> Handle(UnlockUserCommand request, CancellationToken ct)
    {
        var user = await _users.GetByIdAsync(new AppUserId(request.Id), ct)
            ?? throw new NotFoundException("User", request.Id);
        user.Unlock();
        return Unit.Value;
    }
}

// ---- Self-service: change own password ----

public sealed record ChangeOwnPasswordCommand(string OldPassword, string NewPassword) : ICommand, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = Array.Empty<string>(); // authenticated only
}

public sealed class ChangeOwnPasswordValidator : AbstractValidator<ChangeOwnPasswordCommand>
{
    public ChangeOwnPasswordValidator() => RuleFor(x => x.NewPassword).StrongPassword(); // IDN-10
}

public sealed class ChangeOwnPasswordHandler : ICommandHandler<ChangeOwnPasswordCommand>
{
    private readonly IAppUserRepository _users;
    private readonly IUserSessionRepository _sessions;
    private readonly ICurrentUser _caller;
    private readonly IPasswordHasher _hasher;
    private readonly IClock _clock;

    public ChangeOwnPasswordHandler(IAppUserRepository users, IUserSessionRepository sessions,
        ICurrentUser caller, IPasswordHasher hasher, IClock clock)
    {
        _users = users; _sessions = sessions; _caller = caller; _hasher = hasher; _clock = clock;
    }

    public async Task<Unit> Handle(ChangeOwnPasswordCommand request, CancellationToken ct)
    {
        var user = await _users.GetByIdAsync(_caller.UserId, ct)
            ?? throw new NotFoundException("User", _caller.UserId);

        if (!_hasher.Verify(request.OldPassword, user.Password))
            throw new ForbiddenException("The current password is incorrect.");

        user.SetPassword(_hasher.Hash(request.NewPassword));

        // Evict the user's other sessions so a stolen bearer token cannot outlive the password change
        // (finding IDN-5); keep the caller's current session so they are not logged out mid-change.
        var now = _clock.UtcNow;
        foreach (var session in await _sessions.GetActiveByUserAsync(_caller.UserId, ct))
            if (session.Id != _caller.SessionId)
                session.Revoke(now);

        return Unit.Value;
    }
}

// ---- Self-service: set own language ----

public sealed record SetOwnLanguageCommand(string Language) : ICommand, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = Array.Empty<string>(); // authenticated only
}

public sealed class SetOwnLanguageHandler : ICommandHandler<SetOwnLanguageCommand>
{
    private readonly IAppUserRepository _users;
    private readonly ICurrentUser _caller;

    public SetOwnLanguageHandler(IAppUserRepository users, ICurrentUser caller) { _users = users; _caller = caller; }

    public async Task<Unit> Handle(SetOwnLanguageCommand request, CancellationToken ct)
    {
        var user = await _users.GetByIdAsync(_caller.UserId, ct)
            ?? throw new NotFoundException("User", _caller.UserId);
        user.SetLanguage(request.Language);
        return Unit.Value;
    }
}
