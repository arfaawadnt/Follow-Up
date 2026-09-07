using FollowUp.Application.Common.Abstractions;
using FollowUp.Application.Common.Messaging;
using FollowUp.Application.Common.Models;
using FollowUp.Application.Features.Laboratories.Contracts;
using FollowUp.Domain.Identity;

namespace FollowUp.Application.Features.Laboratories.GetLaboratories;

/// <summary>Lists laboratories within the caller's scope (SRS FR-3). The labs directory is available to any
/// authenticated staff member (the UI exposes it with no privilege gate); confidentiality is enforced by the
/// OrgScope filter and the encrypted-code masking, not a view privilege. Declared as an authenticated-only
/// <see cref="IAuthorizedRequest"/> so the authorization posture is explicit and pipeline-enforced (finding M-5).</summary>
public sealed record GetLaboratoriesQuery : IQuery<PagedResult<LabListItemDto>>, IAuthorizedRequest
{
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 50;
    public string? Search { get; init; }
    public string? Status { get; init; }
    public string? Segment { get; init; }
    public string? Governorate { get; init; }

    public IReadOnlyCollection<string> RequiredPrivileges { get; } = System.Array.Empty<string>();
}

public sealed class GetLaboratoriesHandler : IQueryHandler<GetLaboratoriesQuery, PagedResult<LabListItemDto>>
{
    private readonly ILaboratoryQueries _queries;
    private readonly ICurrentUser _currentUser;

    public GetLaboratoriesHandler(ILaboratoryQueries queries, ICurrentUser currentUser)
    {
        _queries = queries;
        _currentUser = currentUser;
    }

    public Task<PagedResult<LabListItemDto>> Handle(GetLaboratoriesQuery request, CancellationToken ct)
    {
        var criteria = new LabSearchCriteria
        {
            Page = request.Page,
            PageSize = request.PageSize,
            Search = request.Search,
            Status = request.Status,
            Segment = request.Segment,
            Governorate = request.Governorate,
        };

        var canSeeEncrypted = _currentUser.Has(Privileges.ShowEncryptedLabs);
        var canSeeLocation = _currentUser.Has(Privileges.ViewLabLocation);
        return _queries.SearchAsync(criteria, _currentUser.Scope, canSeeEncrypted, canSeeLocation, ct);
    }
}
