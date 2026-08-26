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

// Mirrors the reference platform's lab check-in row: transferred visits (awaiting receipt or received).
public sealed record ReceivingItemDto(
    Guid VisitId, Guid LaboratoryId, string LabDisplayCode, string LabCode, string LabName,
    string? Branch, string? Governorate, string? City, string? Area,
    DateOnly VisitDate, string VisitTime, string? CollectorName, int? Samples, string Status,
    string? TransferRepName, string? TransferTime, string? ReceivedTime);

/// <summary>Read-side query for transferred items (awaiting receipt or received) in a date range.</summary>
public interface ILabCheckInQueries
{
    Task<IReadOnlyList<ReceivingItemDto>> GetAwaitingReceiptAsync(DateOnly start, DateOnly end, OrgScope scope, bool canSeeEncrypted, CancellationToken ct);
}

/// <summary>Lists transferred items (awaiting receipt or received) in a date range within scope (SRS FR-7).</summary>
public sealed record GetLabCheckInQuery(DateOnly? Start = null, DateOnly? End = null)
    : IQuery<IReadOnlyList<ReceivingItemDto>>, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ConfirmTransfers, Privileges.ManageTransfers };
}

public sealed class GetLabCheckInHandler : IQueryHandler<GetLabCheckInQuery, IReadOnlyList<ReceivingItemDto>>
{
    private readonly ILabCheckInQueries _queries;
    private readonly ICurrentUser _user;
    private readonly IClock _clock;

    public GetLabCheckInHandler(ILabCheckInQueries queries, ICurrentUser user, IClock clock) { _queries = queries; _user = user; _clock = clock; }

    public Task<IReadOnlyList<ReceivingItemDto>> Handle(GetLabCheckInQuery request, CancellationToken ct)
    {
        var start = request.Start ?? _clock.CairoToday;
        var end = request.End ?? start;
        return _queries.GetAwaitingReceiptAsync(start, end, _user.Scope, _user.Has(Privileges.ShowEncryptedLabs), ct);
    }
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

        visit.ReceiveAtLab(_clock.UtcNow);
        lab.DeriveActiveFromActivity(); // BR-5: receipt derives lab status
        return Unit.Value;
    }
}

// ---- Confirm receipts in batch (reference: "Confirm Selected Receipts") ----

/// <summary>Marks several transferred visits received in one transaction (SRS FR-7). Returns the confirmed count.</summary>
public sealed record ConfirmReceiptsBatchCommand(IReadOnlyList<Guid> VisitIds) : ICommand<int>, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ConfirmTransfers, Privileges.ManageTransfers };
}

public sealed class ConfirmReceiptsBatchValidator : AbstractValidator<ConfirmReceiptsBatchCommand>
{
    public ConfirmReceiptsBatchValidator()
    {
        RuleFor(x => x.VisitIds).NotEmpty();
        RuleForEach(x => x.VisitIds).NotEmpty();
    }
}

public sealed class ConfirmReceiptsBatchHandler : ICommandHandler<ConfirmReceiptsBatchCommand, int>
{
    private readonly IDailyVisitRepository _visits;
    private readonly ILaboratoryRepository _labs;
    private readonly ICurrentUser _user;
    private readonly IClock _clock;

    public ConfirmReceiptsBatchHandler(IDailyVisitRepository visits, ILaboratoryRepository labs, ICurrentUser user, IClock clock)
    {
        _visits = visits; _labs = labs; _user = user; _clock = clock;
    }

    public async Task<int> Handle(ConfirmReceiptsBatchCommand request, CancellationToken ct)
    {
        var received = 0;
        foreach (var visitId in request.VisitIds)
        {
            var visit = await _visits.GetByIdAsync(new DailyVisitId(visitId), ct)
                ?? throw new NotFoundException("Visit", visitId);
            var lab = await _labs.GetByIdAsync(visit.LaboratoryId, ct)
                ?? throw new NotFoundException("Laboratory", visit.LaboratoryId.Value);

            _user.EnsureInScope(lab);

            visit.ReceiveAtLab(_clock.UtcNow);
            lab.DeriveActiveFromActivity(); // BR-5
            received++;
        }
        return received;
    }
}
