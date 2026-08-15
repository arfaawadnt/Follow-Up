using FollowUp.Application.Common.Abstractions;
using FollowUp.Application.Common.Abstractions.Persistence;
using FollowUp.Application.Common.Exceptions;
using FollowUp.Application.Common.Messaging;
using FollowUp.Application.Common.Security;
using FollowUp.Domain.Identity;
using FollowUp.Domain.Operations;
using FluentValidation;
using MediatR;

namespace FollowUp.Application.Features.LabCheckIn;

// ---- Read side ----

public sealed record ReceivingItemDto(
    Guid VisitId, Guid LaboratoryId, string LabDisplayCode, string LabName, DateOnly VisitDate, int? SampleCount);

/// <summary>Read-side query for items awaiting receipt at the laboratory (transferred, not yet received).</summary>
public interface ILabCheckInQueries
{
    Task<IReadOnlyList<ReceivingItemDto>> GetAwaitingReceiptAsync(OrgScope scope, bool canSeeEncrypted, CancellationToken ct);
}

/// <summary>Lists items awaiting receipt within scope (SRS FR-7).</summary>
public sealed record GetLabCheckInQuery : IQuery<IReadOnlyList<ReceivingItemDto>>, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ConfirmTransfers, Privileges.ManageTransfers };
}

public sealed class GetLabCheckInHandler : IQueryHandler<GetLabCheckInQuery, IReadOnlyList<ReceivingItemDto>>
{
    private readonly ILabCheckInQueries _queries;
    private readonly ICurrentUser _user;

    public GetLabCheckInHandler(ILabCheckInQueries queries, ICurrentUser user) { _queries = queries; _user = user; }

    public Task<IReadOnlyList<ReceivingItemDto>> Handle(GetLabCheckInQuery request, CancellationToken ct) =>
        _queries.GetAwaitingReceiptAsync(_user.Scope, _user.Has(Privileges.ShowEncryptedLabs), ct);
}

// ---- Confirm receipt (Visited -> Received) ----

/// <summary>Marks a transferred visit received at the laboratory (SRS FR-7).</summary>
public sealed record ConfirmReceiptCommand(Guid VisitId) : ICommand, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ConfirmTransfers, Privileges.ManageTransfers };
}

public sealed class ConfirmReceiptHandler : ICommandHandler<ConfirmReceiptCommand>
{
    private readonly IDailyVisitRepository _visits;
    private readonly ILaboratoryRepository _labs;
    private readonly ICurrentUser _user;
    private readonly IClock _clock;

    public ConfirmReceiptHandler(IDailyVisitRepository visits, ILaboratoryRepository labs, ICurrentUser user, IClock clock)
    {
        _visits = visits; _labs = labs; _user = user; _clock = clock;
    }

    public async Task<Unit> Handle(ConfirmReceiptCommand request, CancellationToken ct)
    {
        var visit = await _visits.GetByIdAsync(new DailyVisitId(request.VisitId), ct)
            ?? throw new NotFoundException("Visit", request.VisitId);
        var lab = await _labs.GetByIdAsync(visit.LaboratoryId, ct)
            ?? throw new NotFoundException("Laboratory", visit.LaboratoryId.Value);

        _user.EnsureInScope(lab);

        visit.ReceiveAtLab(_clock.CairoNow);
        lab.DeriveActiveFromActivity(); // BR-5: receipt derives lab status
        return Unit.Value;
    }
}
