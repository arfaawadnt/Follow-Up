using FollowUp.Application.Common.Messaging;
using FollowUp.Application.Features.Laboratories.Contracts;
using FollowUp.Domain.Identity;

namespace FollowUp.Application.Features.Laboratories.NextLabCode;

/// <summary>Suggests the next available lab code for the create-lab form (SRS FR-3). Requires AddLabs; reads via
/// the projection, so no repository is injected into the API endpoint (finding M-20).</summary>
public sealed record GetNextLabCodeQuery : IQuery<string>, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.AddLabs };
}

public sealed class GetNextLabCodeHandler : IQueryHandler<GetNextLabCodeQuery, string>
{
    private readonly ILaboratoryQueries _queries;
    public GetNextLabCodeHandler(ILaboratoryQueries queries) => _queries = queries;
    public Task<string> Handle(GetNextLabCodeQuery request, CancellationToken ct) => _queries.NextCodeAsync(ct);
}
