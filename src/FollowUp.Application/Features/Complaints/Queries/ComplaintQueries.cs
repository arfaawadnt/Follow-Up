using FollowUp.Application.Common.Abstractions;
using FollowUp.Application.Common.Messaging;
using FollowUp.Application.Common.Models;
using FollowUp.Application.Features.Complaints.Contracts;
using FollowUp.Domain.Identity;

namespace FollowUp.Application.Features.Complaints.Queries;

/// <summary>Lists complaints within scope (SRS FR-11).</summary>
public sealed record GetComplaintsQuery : IQuery<PagedResult<ComplaintListItemDto>>, IAuthorizedRequest
{
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 50;
    public string? Status { get; init; }
    public string? Category { get; init; }
    public Guid? LaboratoryId { get; init; }

    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ViewComplaints, Privileges.ManageComplaints };
}

public sealed class GetComplaintsHandler : IQueryHandler<GetComplaintsQuery, PagedResult<ComplaintListItemDto>>
{
    private readonly IComplaintQueries _queries;
    private readonly ICurrentUser _user;

    public GetComplaintsHandler(IComplaintQueries queries, ICurrentUser user)
    {
        _queries = queries; _user = user;
    }

    public Task<PagedResult<ComplaintListItemDto>> Handle(GetComplaintsQuery request, CancellationToken ct)
    {
        var criteria = new ComplaintSearchCriteria
        {
            Page = request.Page, PageSize = request.PageSize,
            Status = request.Status, Category = request.Category, LaboratoryId = request.LaboratoryId,
        };
        return _queries.SearchAsync(criteria, _user.Scope, _user.Has(Privileges.ShowEncryptedLabs), ct);
    }
}

/// <summary>Status counts for the complaint list's filter pills, computed server-side (SRS FR-11, finding CMP-16).</summary>
public sealed record GetComplaintCountsQuery(Guid? LaboratoryId = null, string? Category = null)
    : IQuery<ComplaintCountsDto>, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ViewComplaints, Privileges.ManageComplaints };
}

public sealed class GetComplaintCountsHandler : IQueryHandler<GetComplaintCountsQuery, ComplaintCountsDto>
{
    private readonly IComplaintQueries _queries;
    private readonly ICurrentUser _user;

    public GetComplaintCountsHandler(IComplaintQueries queries, ICurrentUser user) { _queries = queries; _user = user; }

    public Task<ComplaintCountsDto> Handle(GetComplaintCountsQuery request, CancellationToken ct) =>
        _queries.CountsAsync(_user.Scope, request.Category, request.LaboratoryId, ct);
}

/// <summary>Returns one complaint's detail (SRS FR-11).</summary>
public sealed record GetComplaintByIdQuery(Guid Id) : IQuery<ComplaintDetailDto>, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ViewComplaints, Privileges.ManageComplaints };
}

public sealed class GetComplaintByIdHandler : IQueryHandler<GetComplaintByIdQuery, ComplaintDetailDto>
{
    private readonly IComplaintQueries _queries;
    private readonly ICurrentUser _user;

    public GetComplaintByIdHandler(IComplaintQueries queries, ICurrentUser user)
    {
        _queries = queries; _user = user;
    }

    public async Task<ComplaintDetailDto> Handle(GetComplaintByIdQuery request, CancellationToken ct)
    {
        var dto = await _queries.GetByIdAsync(request.Id, _user.Scope, _user.Has(Privileges.ShowEncryptedLabs), ct)
            ?? throw new Common.Exceptions.NotFoundException("Complaint", request.Id);
        return dto;
    }
}

/// <summary>Per-complaint audit trail (SRS FR-11/FR-20).</summary>
public sealed record GetComplaintAuditQuery(Guid Id) : IQuery<IReadOnlyList<ComplaintAuditRowDto>>, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ViewComplaints, Privileges.ManageComplaints };
}

public sealed class GetComplaintAuditHandler : IQueryHandler<GetComplaintAuditQuery, IReadOnlyList<ComplaintAuditRowDto>>
{
    private readonly IComplaintQueries _queries;
    private readonly ICurrentUser _user;

    public GetComplaintAuditHandler(IComplaintQueries queries, ICurrentUser user)
    {
        _queries = queries; _user = user;
    }

    public Task<IReadOnlyList<ComplaintAuditRowDto>> Handle(GetComplaintAuditQuery request, CancellationToken ct) =>
        _queries.GetAuditAsync(request.Id, _user.Scope, ct);
}
