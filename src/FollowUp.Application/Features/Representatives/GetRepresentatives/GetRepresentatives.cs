using FollowUp.Application.Common.Abstractions;
using FollowUp.Application.Common.Messaging;
using FollowUp.Application.Common.Models;
using FollowUp.Application.Features.Representatives.Contracts;
using FollowUp.Domain.Identity;

namespace FollowUp.Application.Features.Representatives.GetRepresentatives;

/// <summary>Lists representatives within the caller's scope (SRS FR-4; requires ViewReps/ManageReps).</summary>
public sealed record GetRepresentativesQuery : IQuery<PagedResult<RepListItemDto>>, IAuthorizedRequest
{
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 50;
    public string? Search { get; init; }
    public string? Type { get; init; }
    public bool? ActiveOnly { get; init; }

    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ViewReps, Privileges.ManageReps };
}

public sealed class GetRepresentativesHandler : IQueryHandler<GetRepresentativesQuery, PagedResult<RepListItemDto>>
{
    private readonly IRepresentativeQueries _queries;
    private readonly ICurrentUser _currentUser;

    public GetRepresentativesHandler(IRepresentativeQueries queries, ICurrentUser currentUser)
    {
        _queries = queries;
        _currentUser = currentUser;
    }

    public Task<PagedResult<RepListItemDto>> Handle(GetRepresentativesQuery request, CancellationToken ct)
    {
        var criteria = new RepSearchCriteria
        {
            Page = request.Page,
            PageSize = request.PageSize,
            Search = request.Search,
            Type = request.Type,
            ActiveOnly = request.ActiveOnly,
        };
        return _queries.SearchAsync(criteria, _currentUser.Scope, ct);
    }
}
