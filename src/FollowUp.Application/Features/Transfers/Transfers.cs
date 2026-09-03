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

// Mirrors the reference platform's transfer row: collected visits in a date range, transferred or pending.
public sealed record TransferItemDto(
    Guid VisitId, Guid LaboratoryId, string LabDisplayCode, string LabName,
    string? Branch, string? Governorate, string? City, string? Area,
    DateOnly VisitDate, string VisitTime, string? CollectorName, int? Samples,
    bool TransferDone, string? DriverName, string? DriverMobile, string? CarPlate,
    Guid? TransferRepId, string? TransferRepName, string? TransferTime);

/// <summary>Read-side query interface for collected visits (transferred or awaiting transfer) in a range.</summary>
public interface ITransferQueries
{
    Task<IReadOnlyList<TransferItemDto>> GetTransferableAsync(DateOnly start, DateOnly end, OrgScope scope, bool canSeeEncrypted, CancellationToken ct);
}

/// <summary>Lists collected visits (transferred or awaiting transfer) in a date range within scope (SRS FR-6).</summary>
public sealed record GetTransfersQuery(DateOnly? Start = null, DateOnly? End = null)
    : IQuery<IReadOnlyList<TransferItemDto>>, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ViewTransfers, Privileges.ManageTransfers };
}

public sealed class GetTransfersHandler : IQueryHandler<GetTransfersQuery, IReadOnlyList<TransferItemDto>>
{
    private readonly ITransferQueries _queries;
    private readonly ICurrentUser _user;
    private readonly IClock _clock;

    public GetTransfersHandler(ITransferQueries queries, ICurrentUser user, IClock clock) { _queries = queries; _user = user; _clock = clock; }

    public Task<IReadOnlyList<TransferItemDto>> Handle(GetTransfersQuery request, CancellationToken ct)
    {
        var start = request.Start ?? _clock.CairoToday;
        var end = request.End ?? start;
        return _queries.GetTransferableAsync(start, end, _user.Scope, _user.Has(Privileges.ShowEncryptedLabs), ct);
    }
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
            new TransferDetails(request.DriverName, request.DriverMobile, request.CarPlate), _clock.UtcNow);
        return Unit.Value;
    }
}

// ---- Confirm transfers in batch (reference: "Save Transfer Confirmations") ----

public sealed record TransferConfirmationLine(Guid VisitId, Guid TransferRepId,
    string DriverName, string DriverMobile, string? CarPlate, DateTimeOffset? TransferredAt = null);

/// <summary>Confirms several transfer hand-offs in one transaction (SRS FR-6). Returns the confirmed count.</summary>
public sealed record ConfirmTransfersBatchCommand(IReadOnlyList<TransferConfirmationLine> Lines)
    : ICommand<int>, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ConfirmTransfers, Privileges.ManageTransfers };
}

public sealed class ConfirmTransfersBatchValidator : AbstractValidator<ConfirmTransfersBatchCommand>
{
    public ConfirmTransfersBatchValidator()
    {
        RuleFor(x => x.Lines).NotEmpty();
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(x => x.VisitId).NotEmpty();
            line.RuleFor(x => x.TransferRepId).NotEmpty();
            line.RuleFor(x => x.DriverName).NotEmpty();
            line.RuleFor(x => x.DriverMobile).NotEmpty();
        });
    }
}

public sealed class ConfirmTransfersBatchHandler : ICommandHandler<ConfirmTransfersBatchCommand, int>
{
    private readonly IDailyVisitRepository _visits;
    private readonly ILaboratoryRepository _labs;
    private readonly IRepresentativeRepository _reps;
    private readonly ICurrentUser _user;
    private readonly IClock _clock;

    public ConfirmTransfersBatchHandler(IDailyVisitRepository visits, ILaboratoryRepository labs,
        IRepresentativeRepository reps, ICurrentUser user, IClock clock)
    {
        _visits = visits; _labs = labs; _reps = reps; _user = user; _clock = clock;
    }

    public async Task<int> Handle(ConfirmTransfersBatchCommand request, CancellationToken ct)
    {
        var confirmed = 0;
        foreach (var line in request.Lines)
        {
            var visit = await _visits.GetByIdAsync(new DailyVisitId(line.VisitId), ct)
                ?? throw new NotFoundException("Visit", line.VisitId);
            var lab = await _labs.GetByIdAsync(visit.LaboratoryId, ct)
                ?? throw new NotFoundException("Laboratory", visit.LaboratoryId.Value);

            _user.EnsureInScope(lab);
            _user.EnsureOwnedIfRepLinked(visit.CollectorRepId);

            var transferRepId = new RepresentativeId(line.TransferRepId);
            if (!await _reps.ExistsAsync(transferRepId, ct))
                throw new NotFoundException("Representative", line.TransferRepId);

            visit.ConfirmTransfer(transferRepId,
                new TransferDetails(line.DriverName, line.DriverMobile, line.CarPlate), line.TransferredAt ?? _clock.UtcNow);
            confirmed++;
        }
        return confirmed;
    }
}
