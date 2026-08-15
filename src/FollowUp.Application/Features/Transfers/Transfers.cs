using FollowUp.Application.Common.Abstractions;
using FollowUp.Application.Common.Abstractions.Persistence;
using FollowUp.Application.Common.Exceptions;
using FollowUp.Application.Common.Messaging;
using FollowUp.Application.Common.Security;
using FollowUp.Domain.Identity;
using FollowUp.Domain.Operations;
using FollowUp.Domain.Representatives;
using FluentValidation;
using MediatR;

namespace FollowUp.Application.Features.Transfers;

// ---- Read side ----

public sealed record TransferItemDto(
    Guid VisitId, Guid LaboratoryId, string LabDisplayCode, string LabName, DateOnly VisitDate, int? SampleCount);

/// <summary>Read-side query interface for transferable items (checked-in, not yet transferred).</summary>
public interface ITransferQueries
{
    Task<IReadOnlyList<TransferItemDto>> GetTransferableAsync(OrgScope scope, bool canSeeEncrypted, CancellationToken ct);
}

/// <summary>Lists visits awaiting transfer within scope (SRS FR-6).</summary>
public sealed record GetTransfersQuery : IQuery<IReadOnlyList<TransferItemDto>>, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ViewTransfers, Privileges.ManageTransfers };
}

public sealed class GetTransfersHandler : IQueryHandler<GetTransfersQuery, IReadOnlyList<TransferItemDto>>
{
    private readonly ITransferQueries _queries;
    private readonly ICurrentUser _user;

    public GetTransfersHandler(ITransferQueries queries, ICurrentUser user) { _queries = queries; _user = user; }

    public Task<IReadOnlyList<TransferItemDto>> Handle(GetTransfersQuery request, CancellationToken ct) =>
        _queries.GetTransferableAsync(_user.Scope, _user.Has(Privileges.ShowEncryptedLabs), ct);
}

// ---- Confirm transfer ----

/// <summary>Confirms hand-off of a collected visit to a transfer rep with driver details (SRS FR-6).</summary>
public sealed record ConfirmTransferCommand : ICommand, IAuthorizedRequest
{
    public Guid VisitId { get; init; }
    public Guid TransferRepId { get; init; }
    public string DriverName { get; init; } = string.Empty;
    public string DriverMobile { get; init; } = string.Empty;
    public string? CarPlate { get; init; }

    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ConfirmTransfers, Privileges.ManageTransfers };
}

public sealed class ConfirmTransferValidator : AbstractValidator<ConfirmTransferCommand>
{
    public ConfirmTransferValidator()
    {
        RuleFor(x => x.VisitId).NotEmpty();
        RuleFor(x => x.TransferRepId).NotEmpty();
        RuleFor(x => x.DriverName).NotEmpty();
        RuleFor(x => x.DriverMobile).NotEmpty();
    }
}

public sealed class ConfirmTransferHandler : ICommandHandler<ConfirmTransferCommand>
{
    private readonly IDailyVisitRepository _visits;
    private readonly ILaboratoryRepository _labs;
    private readonly IRepresentativeRepository _reps;
    private readonly ICurrentUser _user;
    private readonly IClock _clock;

    public ConfirmTransferHandler(IDailyVisitRepository visits, ILaboratoryRepository labs,
        IRepresentativeRepository reps, ICurrentUser user, IClock clock)
    {
        _visits = visits; _labs = labs; _reps = reps; _user = user; _clock = clock;
    }

    public async Task<Unit> Handle(ConfirmTransferCommand request, CancellationToken ct)
    {
        var visit = await _visits.GetByIdAsync(new DailyVisitId(request.VisitId), ct)
            ?? throw new NotFoundException("Visit", request.VisitId);
        var lab = await _labs.GetByIdAsync(visit.LaboratoryId, ct)
            ?? throw new NotFoundException("Laboratory", visit.LaboratoryId.Value);

        _user.EnsureInScope(lab);
        _user.EnsureOwnedIfRepLinked(visit.CollectorRepId);

        var transferRepId = new RepresentativeId(request.TransferRepId);
        if (!await _reps.ExistsAsync(transferRepId, ct))
            throw new NotFoundException("Representative", request.TransferRepId);

        visit.ConfirmTransfer(transferRepId,
            new TransferDetails(request.DriverName, request.DriverMobile, request.CarPlate), _clock.CairoNow);
        return Unit.Value;
    }
}
