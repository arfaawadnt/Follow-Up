using FollowUp.Application.Common.Messaging;
using FollowUp.Application.Common.Models;
using FollowUp.Domain.Identity;

namespace FollowUp.Application.Features.Audit;

public sealed record AuditRowDto(
    Guid Id, DateTimeOffset OccurredAt, string Actor, string Entity, string EntityId, string Action,
    string? Before, string? After, string? CorrelationId);

/// <summary>Read-side query interface for the audit trail (SRS FR-20).</summary>
public interface IAuditQueries
{
    Task<PagedResult<AuditRowDto>> SearchAsync(AuditSearchCriteria criteria, CancellationToken ct);
}

public sealed record AuditSearchCriteria : ListQuery
{
    public string? Entity { get; init; }
    public string? Actor { get; init; }
    public string? Action { get; init; }
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
}

/// <summary>Queries the immutable audit trail (SRS FR-20; admin only).</summary>
public sealed record GetAuditQuery : IQuery<PagedResult<AuditRowDto>>, IAuthorizedRequest
{
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 50;
    public string? Entity { get; init; }
    public string? Actor { get; init; }
    public string? Action { get; init; }
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }

    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ManageUsers };
}

public sealed class GetAuditHandler : IQueryHandler<GetAuditQuery, PagedResult<AuditRowDto>>
{
    private readonly IAuditQueries _queries;

    public GetAuditHandler(IAuditQueries queries) => _queries = queries;

    public Task<PagedResult<AuditRowDto>> Handle(GetAuditQuery request, CancellationToken ct)
    {
        var criteria = new AuditSearchCriteria
        {
            Page = request.Page, PageSize = request.PageSize, Entity = request.Entity,
            Actor = request.Actor, Action = request.Action, From = request.From, To = request.To,
        };
        return _queries.SearchAsync(criteria, ct);
    }
}
