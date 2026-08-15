using FollowUp.Application.Common.Abstractions;
using FollowUp.Application.Common.Abstractions.Persistence;
using FollowUp.Application.Common.Exceptions;
using FollowUp.Application.Common.Messaging;
using FollowUp.Application.Common.Security;
using FollowUp.Domain.Identity;
using FluentValidation;
using MediatR;

namespace FollowUp.Application.Features.UserAdmin.Roles;

/// <summary>The six org-scope arrays carried on a role write.</summary>
public sealed record ScopeInput(
    IReadOnlyList<string> Branches, IReadOnlyList<string> Governorates, IReadOnlyList<string> Cities,
    IReadOnlyList<string> Areas, IReadOnlyList<string> Categories, IReadOnlyList<string> Segments)
{
    public OrgScope ToOrgScope() => OrgScope.Create(Branches, Governorates, Cities, Areas, Categories, Segments);
    public static ScopeInput Empty => new(
        Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(),
        Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());
}

// ---- Create role ----

public sealed record CreateRoleCommand : ICommand<Guid>, IAuthorizedRequest
{
    public string Name { get; init; } = string.Empty;
    public IReadOnlyList<string> Privileges { get; init; } = Array.Empty<string>();
    public string DefaultLanguage { get; init; } = "en";
    public string DefaultTheme { get; init; } = "light";
    public ScopeInput Scope { get; init; } = ScopeInput.Empty;

    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Domain.Identity.Privileges.ManageUsers };
}

public sealed class CreateRoleValidator : AbstractValidator<CreateRoleCommand>
{
    public CreateRoleValidator() => RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
}

public sealed class CreateRoleHandler : ICommandHandler<CreateRoleCommand, Guid>
{
    private readonly IRoleRepository _roles;
    private readonly ICurrentUser _caller;

    public CreateRoleHandler(IRoleRepository roles, ICurrentUser caller) { _roles = roles; _caller = caller; }

    public async Task<Guid> Handle(CreateRoleCommand request, CancellationToken ct)
    {
        // Anti-amplification (BR-12): cannot grant privileges/scope broader than the caller's own.
        _caller.EnsurePrivilegesWithinGrant(request.Privileges);
        var scope = request.Scope.ToOrgScope();
        _caller.EnsureScopeWithinGrant(scope);

        if (await _roles.GetByNameAsync(request.Name, ct) is not null)
            throw new ConflictException($"A role named '{request.Name}' already exists.");

        var role = Role.Create(request.Name, request.Privileges, request.DefaultLanguage, request.DefaultTheme, scope);
        _roles.Add(role);
        return role.Id.Value;
    }
}

// ---- Update role ----

public sealed record UpdateRoleCommand : ICommand, IAuthorizedRequest
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public IReadOnlyList<string> Privileges { get; init; } = Array.Empty<string>();
    public string DefaultLanguage { get; init; } = "en";
    public string DefaultTheme { get; init; } = "light";
    public ScopeInput Scope { get; init; } = ScopeInput.Empty;

    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Domain.Identity.Privileges.ManageUsers };
}

public sealed class UpdateRoleHandler : ICommandHandler<UpdateRoleCommand>
{
    private readonly IRoleRepository _roles;
    private readonly ICurrentUser _caller;

    public UpdateRoleHandler(IRoleRepository roles, ICurrentUser caller) { _roles = roles; _caller = caller; }

    public async Task<Unit> Handle(UpdateRoleCommand request, CancellationToken ct)
    {
        var role = await _roles.GetByIdAsync(new RoleId(request.Id), ct)
            ?? throw new NotFoundException("Role", request.Id);

        // Block self-privilege-escalation: a caller cannot change the role they themselves hold.
        if (_caller.RoleId == role.Id)
            throw new ForbiddenException("You cannot modify your own role.");

        _caller.EnsurePrivilegesWithinGrant(request.Privileges);
        var scope = request.Scope.ToOrgScope();
        _caller.EnsureScopeWithinGrant(scope);

        role.Rename(request.Name);
        role.SetPrivileges(request.Privileges);
        role.SetDefaults(request.DefaultLanguage, request.DefaultTheme);
        role.SetScope(scope);
        return Unit.Value;
    }
}

// ---- Delete role ----

public sealed record DeleteRoleCommand(Guid Id) : ICommand, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Domain.Identity.Privileges.ManageUsers };
}

public sealed class DeleteRoleHandler : ICommandHandler<DeleteRoleCommand>
{
    private readonly IRoleRepository _roles;

    public DeleteRoleHandler(IRoleRepository roles) => _roles = roles;

    public async Task<Unit> Handle(DeleteRoleCommand request, CancellationToken ct)
    {
        var role = await _roles.GetByIdAsync(new RoleId(request.Id), ct)
            ?? throw new NotFoundException("Role", request.Id);

        if (role.IsBuiltIn)
            throw new ConflictException("Built-in roles cannot be deleted.");
        if (await _roles.IsInUseAsync(role.Id, ct))
            throw new ConflictException("This role is assigned to one or more users and cannot be deleted.");

        _roles.Remove(role);
        return Unit.Value;
    }
}
