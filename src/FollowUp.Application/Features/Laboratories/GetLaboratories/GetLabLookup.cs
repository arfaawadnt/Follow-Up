using FollowUp.Application.Common.Abstractions;
using FollowUp.Application.Common.Messaging;
using FollowUp.Application.Features.Laboratories.Contracts;
using FollowUp.Domain.Identity;

namespace FollowUp.Application.Features.Laboratories.GetLaboratories;

/// <summary>
/// The lab picker's data: every laboratory within the caller's scope, unpaged, as id + display code + name. The paged
/// directory (<see cref="GetLaboratoriesQuery"/>) is for browsing; pickers that asked it for "500 rows" silently lost
/// the rest of the ~13k labs. Same authorization posture as the directory — authenticated-only, confidentiality by
/// OrgScope filtering and encrypted-code masking (finding M-5).
/// </summary>
public sealed record GetLabLookupQuery : IQuery<IReadOnlyList<LabLookupDto>>, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = System.Array.Empty<string>();
}

public sealed class GetLabLookupHandler : IQueryHandler<GetLabLookupQuery, IReadOnlyList<LabLookupDto>>
{
    private readonly ILaboratoryQueries _queries;
    private readonly ICurrentUser _currentUser;

    public GetLabLookupHandler(ILaboratoryQueries queries, ICurrentUser currentUser)
    {
        _queries = queries;
        _currentUser = currentUser;
    }

    public Task<IReadOnlyList<LabLookupDto>> Handle(GetLabLookupQuery request, CancellationToken ct) =>
        _queries.LookupAsync(_currentUser.Scope, _currentUser.Has(Privileges.ShowEncryptedLabs), ct);
}
