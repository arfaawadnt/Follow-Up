using FollowUp.Application.Common.Messaging;
using FollowUp.Application.Common.Models;
using FollowUp.Domain.Identity;

namespace FollowUp.Application.Features.UserAdmin.Queries;

public sealed record UserListItemDto(Guid Id, string Username, string RoleName, string? Email, bool IsActive, bool IsLocked);
public sealed record UserLookupDto(Guid Id, string Username);
public sealed record RoleDto(
    Guid Id, string Name, IReadOnlyList<string> Privileges, string DefaultLanguage, string DefaultTheme, bool IsBuiltIn);

public interface IUserAdminQueries
{
    Task<PagedResult<UserListItemDto>> SearchUsersAsync(ListQuery query, CancellationToken ct);
    Task<IReadOnlyList<UserLookupDto>> LookupUsersAsync(string? search, CancellationToken ct);
    Task<IReadOnlyList<RoleDto>> GetRolesAsync(CancellationToken ct);
}

/// <summary>Lists users (SRS FR-2; admin).</summary>
public sealed record GetUsersQuery(int Page = 1, int PageSize = 50, string? Search = null)
    : IQuery<PagedResult<UserListItemDto>>, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ManageUsers };
}

public sealed class GetUsersHandler : IQueryHandler<GetUsersQuery, PagedResult<UserListItemDto>>
{
    private readonly IUserAdminQueries _queries;
    public GetUsersHandler(IUserAdminQueries queries) => _queries = queries;

    public Task<PagedResult<UserListItemDto>> Handle(GetUsersQuery request, CancellationToken ct) =>
        _queries.SearchUsersAsync(new ListQuery { Page = request.Page, PageSize = request.PageSize, Search = request.Search }, ct);
}

/// <summary>Directory lookup returning non-credential attribution info (SRS FR-2; authenticated).</summary>
public sealed record LookupUsersQuery(string? Search = null) : IQuery<IReadOnlyList<UserLookupDto>>, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = Array.Empty<string>();
}

public sealed class LookupUsersHandler : IQueryHandler<LookupUsersQuery, IReadOnlyList<UserLookupDto>>
{
    private readonly IUserAdminQueries _queries;
    public LookupUsersHandler(IUserAdminQueries queries) => _queries = queries;

    public Task<IReadOnlyList<UserLookupDto>> Handle(LookupUsersQuery request, CancellationToken ct) =>
        _queries.LookupUsersAsync(request.Search, ct);
}

/// <summary>Lists roles (SRS FR-2; admin).</summary>
public sealed record GetRolesQuery : IQuery<IReadOnlyList<RoleDto>>, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ManageUsers };
}

public sealed class GetRolesHandler : IQueryHandler<GetRolesQuery, IReadOnlyList<RoleDto>>
{
    private readonly IUserAdminQueries _queries;
    public GetRolesHandler(IUserAdminQueries queries) => _queries = queries;

    public Task<IReadOnlyList<RoleDto>> Handle(GetRolesQuery request, CancellationToken ct) =>
        _queries.GetRolesAsync(ct);
}
