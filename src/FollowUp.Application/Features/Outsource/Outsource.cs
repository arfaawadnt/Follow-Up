using FollowUp.Application.Common.Abstractions;
using FollowUp.Application.Common.Abstractions.Persistence;
using FollowUp.Application.Common.Exceptions;
using FollowUp.Application.Common.Messaging;
using FollowUp.Application.Common.Security;
using FollowUp.Domain.Common;
using FollowUp.Domain.Identity;
using FollowUp.Domain.Laboratories;
using FollowUp.Domain.Operations;
using FluentValidation;
using MediatR;

namespace FollowUp.Application.Features.Outsource;

// ---- Read side ----

public sealed record OutsourceSampleDto(
    Guid Id, Guid LaboratoryId, string LabDisplayCode, string LabName, DateOnly VisitDate, string DestinationLab, int Quantity, string Status, string? Notes);

public interface IOutsourceQueries
{
    Task<IReadOnlyList<OutsourceSampleDto>> ListAsync(DateOnly start, DateOnly end, OrgScope scope, bool canSeeEncrypted, CancellationToken ct);
}

public sealed record GetOutsourceSamplesQuery(DateOnly? Start = null, DateOnly? End = null)
    : IQuery<IReadOnlyList<OutsourceSampleDto>>, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.OutsourceSamples };
}

public sealed class GetOutsourceSamplesHandler : IQueryHandler<GetOutsourceSamplesQuery, IReadOnlyList<OutsourceSampleDto>>
{
    private readonly IOutsourceQueries _queries;
    private readonly ICurrentUser _user;
    private readonly IClock _clock;

    public GetOutsourceSamplesHandler(IOutsourceQueries queries, ICurrentUser user, IClock clock) { _queries = queries; _user = user; _clock = clock; }

    public Task<IReadOnlyList<OutsourceSampleDto>> Handle(GetOutsourceSamplesQuery request, CancellationToken ct)
    {
        var start = request.Start ?? _clock.CairoToday;
        var end = request.End ?? start;
        return _queries.ListAsync(start, end, _user.Scope, _user.Has(Privileges.ShowEncryptedLabs), ct);
    }
}

// ---- Create (unique per visit-date + lab) ----

public sealed record CreateOutsourceSampleCommand : ICommand<Guid>, IAuthorizedRequest
{
    public Guid LaboratoryId { get; init; }
    public DateOnly VisitDate { get; init; }
    public string DestinationLab { get; init; } = string.Empty;
    public int Quantity { get; init; }
    public string? Notes { get; init; }

    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.OutsourceSamples };
}

public sealed class CreateOutsourceSampleValidator : AbstractValidator<CreateOutsourceSampleCommand>
{
    public CreateOutsourceSampleValidator()
    {
        RuleFor(x => x.LaboratoryId).NotEmpty();
        RuleFor(x => x.DestinationLab).NotEmpty();
        RuleFor(x => x.Quantity).GreaterThan(0);
    }
}

public sealed class CreateOutsourceSampleHandler : ICommandHandler<CreateOutsourceSampleCommand, Guid>
{
    private readonly IOutsourceSampleRepository _repository;
    private readonly ILaboratoryRepository _labs;
    private readonly ICurrentUser _user;

    public CreateOutsourceSampleHandler(IOutsourceSampleRepository repository, ILaboratoryRepository labs, ICurrentUser user)
    {
        _repository = repository; _labs = labs; _user = user;
    }

    public async Task<Guid> Handle(CreateOutsourceSampleCommand request, CancellationToken ct)
    {
        var labId = new LaboratoryId(request.LaboratoryId);
        var lab = await _labs.GetByIdAsync(labId, ct) ?? throw new NotFoundException("Laboratory", request.LaboratoryId);
        _user.EnsureInScope(lab);

        if (await _repository.ExistsForAsync(labId, request.VisitDate, ct))
            throw new ConflictException("An outsource record already exists for this lab and visit date.");

        var sample = OutsourceSample.Create(labId, request.VisitDate, request.DestinationLab, request.Quantity, request.Notes);
        _repository.Add(sample);
        return sample.Id.Value;
    }
}

// ---- Advance status ----

public sealed record AdvanceOutsourceStatusCommand(Guid Id, string Status) : ICommand, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.OutsourceSamples };
}

public sealed class AdvanceOutsourceStatusHandler : ICommandHandler<AdvanceOutsourceStatusCommand>
{
    private readonly IOutsourceSampleRepository _repository;
    private readonly IClock _clock;

    public AdvanceOutsourceStatusHandler(IOutsourceSampleRepository repository, IClock clock)
    {
        _repository = repository; _clock = clock;
    }

    public async Task<Unit> Handle(AdvanceOutsourceStatusCommand request, CancellationToken ct)
    {
        var sample = await _repository.GetByIdAsync(new OutsourceSampleId(request.Id), ct)
            ?? throw new NotFoundException("Outsource sample", request.Id);
        sample.AdvanceTo(Enumeration.FromName<OutsourceStatus>(request.Status), _clock.UtcNow);
        return Unit.Value;
    }
}

// ---- Delete ----

public sealed record DeleteOutsourceSampleCommand(Guid Id) : ICommand, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.OutsourceSamples };
}

public sealed class DeleteOutsourceSampleHandler : ICommandHandler<DeleteOutsourceSampleCommand>
{
    private readonly IOutsourceSampleRepository _repository;

    public DeleteOutsourceSampleHandler(IOutsourceSampleRepository repository) => _repository = repository;

    public async Task<Unit> Handle(DeleteOutsourceSampleCommand request, CancellationToken ct)
    {
        var sample = await _repository.GetByIdAsync(new OutsourceSampleId(request.Id), ct)
            ?? throw new NotFoundException("Outsource sample", request.Id);
        _repository.Remove(sample);
        return Unit.Value;
    }
}
