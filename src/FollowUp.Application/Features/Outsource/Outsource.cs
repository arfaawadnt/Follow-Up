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
    Guid Id, Guid LaboratoryId, string LabDisplayCode, string LabName, DateOnly VisitDate, string? DestinationLab, int Quantity, string Status, string? Notes,
    IReadOnlyList<OutsourceTestDto>? Tests = null);

/// <summary>One outsourced test line (read side). NetRevenue = TestFees − OutsourceFees (server-computed).</summary>
public sealed record OutsourceTestDto(Guid Id, string TestCode, string TestName, string SampleVolume,
    decimal TestFees, decimal OutsourceFees, decimal NetRevenue);

/// <summary>Test line as submitted by the client on create/update (net revenue is recomputed server-side).</summary>
public sealed record OutsourceTestInput(string TestCode, string TestName, string SampleVolume, decimal TestFees, decimal OutsourceFees);

/// <summary>A flattened row for the Outsource Tracking report (grouped client-side by date → lab → test).</summary>
public sealed record OutsourceTrackingRowDto(
    DateOnly VisitDate, Guid LaboratoryId, string LabDisplayCode, string LabName,
    string? Branch, string? Governorate, string? City, string? Area,
    string TestCode, string TestName, string SampleVolume,
    decimal TestFees, decimal OutsourceFees, decimal NetRevenue);

/// <summary>A test catalogue entry for the outsource test picker.</summary>
public sealed record TestLookupDto(string Code, string Name, int TestType);

public interface IOutsourceQueries
{
    Task<IReadOnlyList<OutsourceSampleDto>> ListAsync(DateOnly start, DateOnly end, OrgScope scope, bool canSeeEncrypted, CancellationToken ct);
    Task<IReadOnlyList<OutsourceTrackingRowDto>> TrackingAsync(DateOnly start, DateOnly end, OrgScope scope, bool canSeeEncrypted, CancellationToken ct);
}

public sealed class OutsourceTestInputValidator : AbstractValidator<OutsourceTestInput>
{
    public OutsourceTestInputValidator()
    {
        RuleFor(x => x.TestCode).NotEmpty();
        RuleFor(x => x.TestName).NotEmpty();
        RuleFor(x => x.SampleVolume).Must(v => OutsourceTest.Volumes.Contains(v))
            .WithMessage("Sample volume must be Small, Medium or Large.");
        RuleFor(x => x.TestFees).GreaterThanOrEqualTo(0);
        RuleFor(x => x.OutsourceFees).GreaterThanOrEqualTo(0);
    }
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

// ---- Outsource Tracking report (test lines flattened, grouped client-side by date → lab → test) ----

public sealed record GetOutsourceTrackingQuery(DateOnly From, DateOnly To)
    : IQuery<IReadOnlyList<OutsourceTrackingRowDto>>, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.OutsourceSamples };
}

public sealed class GetOutsourceTrackingValidator : AbstractValidator<GetOutsourceTrackingQuery>
{
    public GetOutsourceTrackingValidator() =>
        RuleFor(x => x.From).LessThanOrEqualTo(x => x.To).WithMessage("The start date must be on or before the end date.");
}

public sealed class GetOutsourceTrackingHandler : IQueryHandler<GetOutsourceTrackingQuery, IReadOnlyList<OutsourceTrackingRowDto>>
{
    private readonly IOutsourceQueries _queries;
    private readonly ICurrentUser _user;
    public GetOutsourceTrackingHandler(IOutsourceQueries queries, ICurrentUser user) { _queries = queries; _user = user; }
    public Task<IReadOnlyList<OutsourceTrackingRowDto>> Handle(GetOutsourceTrackingQuery request, CancellationToken ct) =>
        _queries.TrackingAsync(request.From, request.To, _user.Scope, _user.Has(Privileges.ShowEncryptedLabs), ct);
}

// ---- Test lookup (for the outsource test picker) ----

public sealed record GetTestLookupQuery : IQuery<IReadOnlyList<TestLookupDto>>, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.OutsourceSamples, Privileges.ViewTeststats };
}

public sealed class GetTestLookupHandler : IQueryHandler<GetTestLookupQuery, IReadOnlyList<TestLookupDto>>
{
    private readonly FollowUp.Application.Features.TestCatalogue.ITestCatalogueQueries _catalogue;
    public GetTestLookupHandler(FollowUp.Application.Features.TestCatalogue.ITestCatalogueQueries catalogue) => _catalogue = catalogue;
    public async Task<IReadOnlyList<TestLookupDto>> Handle(GetTestLookupQuery request, CancellationToken ct)
    {
        var setups = await _catalogue.GetSetupsAsync(ct);
        return setups.Select(s => new TestLookupDto(s.Code, s.NameEn, s.TestType)).ToList();
    }
}

// ---- Create (unique per visit-date + lab) ----

internal static class OutsourceTestMapping
{
    public static IEnumerable<OutsourceTest> ToDomain(IEnumerable<OutsourceTestInput> inputs) =>
        inputs.Select(t => OutsourceTest.Create(t.TestCode, t.TestName, t.SampleVolume, t.TestFees, t.OutsourceFees));
}

public sealed record CreateOutsourceSampleCommand : ICommand<Guid>, IAuthorizedRequest
{
    public Guid LaboratoryId { get; init; }
    public DateOnly VisitDate { get; init; }
    public string DestinationLab { get; init; } = string.Empty;
    public int Quantity { get; init; }
    public string? Notes { get; init; }
    public IReadOnlyList<OutsourceTestInput> Tests { get; init; } = Array.Empty<OutsourceTestInput>();

    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.OutsourceSamples };
}

public sealed class CreateOutsourceSampleValidator : AbstractValidator<CreateOutsourceSampleCommand>
{
    public CreateOutsourceSampleValidator()
    {
        RuleFor(x => x.LaboratoryId).NotEmpty();
        RuleFor(x => x.DestinationLab).NotEmpty();
        RuleFor(x => x.Quantity).GreaterThan(0);
        RuleForEach(x => x.Tests).SetValidator(new OutsourceTestInputValidator());
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
        if (request.Tests.Count > 0)
            sample.SetTests(OutsourceTestMapping.ToDomain(request.Tests));
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
    private readonly ILaboratoryRepository _labs;
    private readonly ICurrentUser _user;
    private readonly IClock _clock;

    public AdvanceOutsourceStatusHandler(IOutsourceSampleRepository repository, ILaboratoryRepository labs,
        ICurrentUser user, IClock clock)
    {
        _repository = repository; _labs = labs; _user = user; _clock = clock;
    }

    public async Task<Unit> Handle(AdvanceOutsourceStatusCommand request, CancellationToken ct)
    {
        var sample = await _repository.GetByIdAsync(new OutsourceSampleId(request.Id), ct)
            ?? throw new NotFoundException("Outsource sample", request.Id);
        // Record-scope check: the privilege alone does not authorize acting on another org-scope's sample
        // (finding B-2) — mirror the Create/Update handlers.
        var lab = await _labs.GetByIdAsync(sample.LaboratoryId, ct)
            ?? throw new NotFoundException("Laboratory", sample.LaboratoryId.Value);
        _user.EnsureInScope(lab);
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
    private readonly ILaboratoryRepository _labs;
    private readonly ICurrentUser _user;

    public DeleteOutsourceSampleHandler(IOutsourceSampleRepository repository, ILaboratoryRepository labs, ICurrentUser user)
    {
        _repository = repository; _labs = labs; _user = user;
    }

    public async Task<Unit> Handle(DeleteOutsourceSampleCommand request, CancellationToken ct)
    {
        var sample = await _repository.GetByIdAsync(new OutsourceSampleId(request.Id), ct)
            ?? throw new NotFoundException("Outsource sample", request.Id);
        // Record-scope check before a destructive delete: the privilege alone does not authorize deleting
        // another org-scope's sample + its fee lines (finding B-2) — mirror the Create/Update handlers.
        var lab = await _labs.GetByIdAsync(sample.LaboratoryId, ct)
            ?? throw new NotFoundException("Laboratory", sample.LaboratoryId.Value);
        _user.EnsureInScope(lab);
        _repository.Remove(sample);
        return Unit.Value;
    }
}

// ---- Inline row update (reference parity: samples / destination / notes editable in the grid) ----

public sealed record UpdateOutsourceSampleCommand(Guid Id, int Quantity, string? DestinationLab, string? Notes)
    : ICommand, IAuthorizedRequest
{
    /// <summary>Null = leave the existing test lines untouched (inline row edit); a list replaces them (tests dialog).</summary>
    public IReadOnlyList<OutsourceTestInput>? Tests { get; init; }

    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.OutsourceSamples };
}

public sealed class UpdateOutsourceSampleValidator : AbstractValidator<UpdateOutsourceSampleCommand>
{
    public UpdateOutsourceSampleValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Quantity).GreaterThan(0);
        RuleForEach(x => x.Tests).SetValidator(new OutsourceTestInputValidator()).When(x => x.Tests is not null);
    }
}

public sealed class UpdateOutsourceSampleHandler : ICommandHandler<UpdateOutsourceSampleCommand>
{
    private readonly IOutsourceSampleRepository _repository;
    private readonly ILaboratoryRepository _labs;
    private readonly ICurrentUser _user;

    public UpdateOutsourceSampleHandler(IOutsourceSampleRepository repository, ILaboratoryRepository labs, ICurrentUser user)
    {
        _repository = repository; _labs = labs; _user = user;
    }

    public async Task<Unit> Handle(UpdateOutsourceSampleCommand request, CancellationToken ct)
    {
        var sample = await _repository.GetByIdAsync(new OutsourceSampleId(request.Id), ct)
            ?? throw new NotFoundException("Outsource sample", request.Id);
        var lab = await _labs.GetByIdAsync(sample.LaboratoryId, ct)
            ?? throw new NotFoundException("Laboratory", sample.LaboratoryId.Value);
        _user.EnsureInScope(lab);

        sample.Update(request.Quantity, request.DestinationLab, request.Notes);
        if (request.Tests is not null)
            sample.SetTests(OutsourceTestMapping.ToDomain(request.Tests));
        return Unit.Value;
    }
}
