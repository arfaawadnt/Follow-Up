using FollowUp.Application.Common.Abstractions;
using FollowUp.Application.Common.Exceptions;
using FollowUp.Application.Common.Messaging;
using FollowUp.Application.Features.Laboratories.Contracts;
using FollowUp.Domain.Identity;

namespace FollowUp.Application.Features.Laboratories.GetLaboratoryById;

/// <summary>Returns one laboratory's detail if it is within the caller's scope (SRS FR-3).</summary>
public sealed record GetLaboratoryByIdQuery(Guid Id) : IQuery<LabDetailDto>;

public sealed class GetLaboratoryByIdHandler : IQueryHandler<GetLaboratoryByIdQuery, LabDetailDto>
{
    private readonly ILaboratoryQueries _queries;
    private readonly ICurrentUser _currentUser;

    public GetLaboratoryByIdHandler(ILaboratoryQueries queries, ICurrentUser currentUser)
    {
        _queries = queries;
        _currentUser = currentUser;
    }

    public async Task<LabDetailDto> Handle(GetLaboratoryByIdQuery request, CancellationToken ct)
    {
        var canSeeEncrypted = _currentUser.Has(Privileges.ShowEncryptedLabs);
        var canSeeLocation = _currentUser.Has(Privileges.ViewLabLocation);
        var dto = await _queries.GetByIdAsync(request.Id, canSeeEncrypted, canSeeLocation, ct)
            ?? throw new NotFoundException("Laboratory", request.Id);

        // Record-level scope check on the read (SCOPE-READ fix — reads are scoped like writes).
        if (!_currentUser.Scope.Allows(dto.Branch, dto.Governorate, dto.City, dto.Area, dto.Category, dto.Segment))
            throw new NotFoundException("Laboratory", request.Id); // hide existence outside scope

        return dto;
    }
}
