using FollowUp.Application.Common.Abstractions;
using FollowUp.Application.Common.Abstractions.Persistence;
using FollowUp.Application.Common.Exceptions;
using FollowUp.Application.Common.Messaging;
using FollowUp.Domain.Common;
using FollowUp.Domain.Identity;
using FollowUp.Domain.Reference;
using FollowUp.Domain.Representatives;
using FluentValidation;
using MediatR;

namespace FollowUp.Application.Features.Setup;

// ---- Read side ----

public sealed record RefItemDto(Guid Id, string Type, string Code, string NameEn, string? NameAr, string? RealName, int SortOrder, string Source,
    decimal? TargetIncomeFrom = null, decimal? TargetIncomeTo = null);
public sealed record CityDto(Guid Id, string Name, string Governorate, string? RealName, string Source);
public sealed record AreaDto(Guid Id, string Name, Guid CityId, bool TransportationRequired, IReadOnlyList<Guid> TransferReps, string? RealName, string Source,
    Guid? AreaManagerId = null, Guid? AreaResponsibleId = null);

public interface ISetupQueries
{
    Task<IReadOnlyList<RefItemDto>> GetRefItemsAsync(string? type, CancellationToken ct);
    Task<IReadOnlyList<CityDto>> GetCitiesAsync(CancellationToken ct);
    Task<IReadOnlyList<AreaDto>> GetAreasAsync(CancellationToken ct);
}

/// <summary>Lists reference items, optionally filtered by type (SRS FR-18; any authenticated user — dropdowns).</summary>
public sealed record GetRefItemsQuery(string? Type = null) : IQuery<IReadOnlyList<RefItemDto>>, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = Array.Empty<string>();
}

public sealed class GetRefItemsHandler : IQueryHandler<GetRefItemsQuery, IReadOnlyList<RefItemDto>>
{
    private readonly ISetupQueries _queries;
    public GetRefItemsHandler(ISetupQueries queries) => _queries = queries;
    public Task<IReadOnlyList<RefItemDto>> Handle(GetRefItemsQuery request, CancellationToken ct) =>
        _queries.GetRefItemsAsync(request.Type, ct);
}

public sealed record GetCitiesQuery : IQuery<IReadOnlyList<CityDto>>, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = Array.Empty<string>();
}

public sealed class GetCitiesHandler : IQueryHandler<GetCitiesQuery, IReadOnlyList<CityDto>>
{
    private readonly ISetupQueries _queries;
    public GetCitiesHandler(ISetupQueries queries) => _queries = queries;
    public Task<IReadOnlyList<CityDto>> Handle(GetCitiesQuery request, CancellationToken ct) => _queries.GetCitiesAsync(ct);
}

public sealed record GetAreasQuery : IQuery<IReadOnlyList<AreaDto>>, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = Array.Empty<string>();
}

public sealed class GetAreasHandler : IQueryHandler<GetAreasQuery, IReadOnlyList<AreaDto>>
{
    private readonly ISetupQueries _queries;
    public GetAreasHandler(ISetupQueries queries) => _queries = queries;
    public Task<IReadOnlyList<AreaDto>> Handle(GetAreasQuery request, CancellationToken ct) => _queries.GetAreasAsync(ct);
}

// ---- Ref item write ----

public sealed record CreateRefItemCommand(string Type, string Code, string NameEn, string? NameAr, int SortOrder = 0, string? RealName = null,
    decimal? TargetIncomeFrom = null, decimal? TargetIncomeTo = null)
    : ICommand<Guid>, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.SetupRefs };
}

public sealed class CreateRefItemValidator : AbstractValidator<CreateRefItemCommand>
{
    public CreateRefItemValidator()
    {
        RuleFor(x => x.Type).NotEmpty();
        RuleFor(x => x.Code).NotEmpty();
        RuleFor(x => x.NameEn).NotEmpty();
        RuleFor(x => x.TargetIncomeFrom).GreaterThanOrEqualTo(0).When(x => x.TargetIncomeFrom is not null);
        RuleFor(x => x.TargetIncomeTo).GreaterThanOrEqualTo(0).When(x => x.TargetIncomeTo is not null);
        RuleFor(x => x.TargetIncomeTo).GreaterThanOrEqualTo(x => x.TargetIncomeFrom!.Value)
            .When(x => x.TargetIncomeFrom is not null && x.TargetIncomeTo is not null)
            .WithMessage("Target income 'to' must be greater than or equal to 'from'.");
    }
}

public sealed class CreateRefItemHandler : ICommandHandler<CreateRefItemCommand, Guid>
{
    private readonly IRefItemRepository _repository;
    public CreateRefItemHandler(IRefItemRepository repository) => _repository = repository;

    public async Task<Guid> Handle(CreateRefItemCommand request, CancellationToken ct)
    {
        var type = Enumeration.FromName<RefType>(request.Type);
        if (await _repository.ExistsAsync(type, request.Code, ct))
            throw new ConflictException($"A {type.Name} reference with code '{request.Code}' already exists.");

        var item = RefItem.Create(type, request.Code, request.NameEn, request.NameAr, request.SortOrder);
        item.SetRealName(request.RealName);
        if (request.TargetIncomeFrom is not null || request.TargetIncomeTo is not null)
            item.SetTargetIncome(request.TargetIncomeFrom, request.TargetIncomeTo);
        _repository.Add(item);
        return item.Id.Value;
    }
}

public sealed record DeleteRefItemCommand(Guid Id) : ICommand, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.SetupRefs };
}

public sealed class DeleteRefItemHandler : ICommandHandler<DeleteRefItemCommand>
{
    private readonly IRefItemRepository _repository;
    public DeleteRefItemHandler(IRefItemRepository repository) => _repository = repository;

    public async Task<Unit> Handle(DeleteRefItemCommand request, CancellationToken ct)
    {
        var item = await _repository.GetByIdAsync(new RefItemId(request.Id), ct)
            ?? throw new NotFoundException("Reference item", request.Id);
        _repository.Remove(item);
        return Unit.Value;
    }
}

public sealed record UpdateRefItemCommand(Guid Id, string Name, string? RealName = null,
    decimal? TargetIncomeFrom = null, decimal? TargetIncomeTo = null) : ICommand, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.SetupRefs };
}

public sealed class UpdateRefItemValidator : AbstractValidator<UpdateRefItemCommand>
{
    public UpdateRefItemValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.TargetIncomeFrom).GreaterThanOrEqualTo(0).When(x => x.TargetIncomeFrom is not null);
        RuleFor(x => x.TargetIncomeTo).GreaterThanOrEqualTo(0).When(x => x.TargetIncomeTo is not null);
        RuleFor(x => x.TargetIncomeTo).GreaterThanOrEqualTo(x => x.TargetIncomeFrom!.Value)
            .When(x => x.TargetIncomeFrom is not null && x.TargetIncomeTo is not null)
            .WithMessage("Target income 'to' must be greater than or equal to 'from'.");
    }
}

public sealed class UpdateRefItemHandler : ICommandHandler<UpdateRefItemCommand>
{
    private readonly IRefItemRepository _repository;
    public UpdateRefItemHandler(IRefItemRepository repository) => _repository = repository;

    public async Task<Unit> Handle(UpdateRefItemCommand request, CancellationToken ct)
    {
        var item = await _repository.GetByIdAsync(new RefItemId(request.Id), ct)
            ?? throw new NotFoundException("Reference item", request.Id);
        item.Rename(request.Name, null); // single-name model: NameEn only
        item.SetRealName(request.RealName);
        // Segment income band (null bounds are valid: a null 'to' is the unbounded top tier, e.g. VIP).
        item.SetTargetIncome(request.TargetIncomeFrom, request.TargetIncomeTo);
        return Unit.Value;
    }
}

// ---- City write ----

public sealed record CreateCityCommand(string Name, string Governorate, string? RealName = null) : ICommand<Guid>, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.SetupCities };
}

public sealed class CreateCityHandler : ICommandHandler<CreateCityCommand, Guid>
{
    private readonly ICityRepository _repository;
    public CreateCityHandler(ICityRepository repository) => _repository = repository;

    public Task<Guid> Handle(CreateCityCommand request, CancellationToken ct)
    {
        var city = City.Create(request.Name, request.Governorate);
        city.SetRealName(request.RealName);
        _repository.Add(city);
        return Task.FromResult(city.Id.Value);
    }
}

public sealed record DeleteCityCommand(Guid Id) : ICommand, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.SetupCities };
}

public sealed class DeleteCityHandler : ICommandHandler<DeleteCityCommand>
{
    private readonly ICityRepository _repository;
    public DeleteCityHandler(ICityRepository repository) => _repository = repository;

    public async Task<Unit> Handle(DeleteCityCommand request, CancellationToken ct)
    {
        var city = await _repository.GetByIdAsync(new CityId(request.Id), ct)
            ?? throw new NotFoundException("City", request.Id);
        _repository.Remove(city);
        return Unit.Value;
    }
}

public sealed record UpdateCityCommand(Guid Id, string Name, string Governorate, string? RealName = null) : ICommand, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.SetupCities };
}

public sealed class UpdateCityValidator : AbstractValidator<UpdateCityCommand>
{
    public UpdateCityValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Governorate).NotEmpty();
    }
}

public sealed class UpdateCityHandler : ICommandHandler<UpdateCityCommand>
{
    private readonly ICityRepository _repository;
    public UpdateCityHandler(ICityRepository repository) => _repository = repository;

    public async Task<Unit> Handle(UpdateCityCommand request, CancellationToken ct)
    {
        var city = await _repository.GetByIdAsync(new CityId(request.Id), ct)
            ?? throw new NotFoundException("City", request.Id);
        city.Rename(request.Name);
        city.SetGovernorate(request.Governorate);
        city.SetRealName(request.RealName);
        return Unit.Value;
    }
}

// ---- Area write ----

/// <summary>
/// Resolves an optional area-role assignment: the representative must exist and be of the expected type
/// (<c>AreaManager</c> for the manager, <c>AreaResponsible</c> for the responsible) — the UI pickers are bound to
/// those types and the server enforces the same rule. Returns null when no assignment was supplied.
/// </summary>
internal static class AreaRoleSupport
{
    public static async Task<RepresentativeId?> ResolveAsync(Guid? repId, Domain.Representatives.RepresentativeType expected,
        string field, IRepresentativeRepository reps, ICurrentUser user, CancellationToken ct)
    {
        if (repId is not { } id) return null;
        var rep = await reps.GetByIdAsync(new RepresentativeId(id), ct)
            ?? throw new NotFoundException("Representative", id);
        // Record-level scope (ADR-0002): a caller may only assign a representative within their own org-scope.
        FollowUp.Application.Common.Security.ScopeGuard.EnsureInScope(user, rep);
        if (rep.Type != expected)
            throw new Common.Exceptions.ValidationException(new Dictionary<string, string[]>
            {
                [field] = new[] { $"The selected representative must be of type {expected.Name}." },
            });
        return rep.Id;
    }
}

public sealed record CreateAreaCommand(string Name, Guid CityId, bool TransportationRequired, IReadOnlyList<Guid> TransferReps, string? RealName = null,
    Guid? AreaManagerId = null, Guid? AreaResponsibleId = null)
    : ICommand<Guid>, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.SetupAreas };
}

public sealed class CreateAreaHandler : ICommandHandler<CreateAreaCommand, Guid>
{
    private readonly IAreaRepository _repository;
    private readonly IRepresentativeRepository _reps;
    private readonly ICurrentUser _user;
    public CreateAreaHandler(IAreaRepository repository, IRepresentativeRepository reps, ICurrentUser user)
    { _repository = repository; _reps = reps; _user = user; }

    public async Task<Guid> Handle(CreateAreaCommand request, CancellationToken ct)
    {
        var area = Area.Create(request.Name, new CityId(request.CityId), request.TransportationRequired);
        area.SetTransferReps(request.TransferReps.Select(r => new RepresentativeId(r)));
        area.SetRealName(request.RealName);
        area.AssignManager(await AreaRoleSupport.ResolveAsync(request.AreaManagerId,
            Domain.Representatives.RepresentativeType.AreaManager, nameof(request.AreaManagerId), _reps, _user, ct));
        area.AssignResponsible(await AreaRoleSupport.ResolveAsync(request.AreaResponsibleId,
            Domain.Representatives.RepresentativeType.AreaResponsible, nameof(request.AreaResponsibleId), _reps, _user, ct));
        _repository.Add(area);
        return area.Id.Value;
    }
}

public sealed record DeleteAreaCommand(Guid Id) : ICommand, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.SetupAreas };
}

public sealed class DeleteAreaHandler : ICommandHandler<DeleteAreaCommand>
{
    private readonly IAreaRepository _repository;
    public DeleteAreaHandler(IAreaRepository repository) => _repository = repository;

    public async Task<Unit> Handle(DeleteAreaCommand request, CancellationToken ct)
    {
        var area = await _repository.GetByIdAsync(new AreaId(request.Id), ct)
            ?? throw new NotFoundException("Area", request.Id);
        _repository.Remove(area);
        return Unit.Value;
    }
}

public sealed record UpdateAreaCommand(Guid Id, string Name, Guid CityId, bool TransportationRequired, string? RealName = null,
    Guid? AreaManagerId = null, Guid? AreaResponsibleId = null)
    : ICommand, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.SetupAreas };
}

public sealed class UpdateAreaValidator : AbstractValidator<UpdateAreaCommand>
{
    public UpdateAreaValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.CityId).NotEmpty();
    }
}

public sealed class UpdateAreaHandler : ICommandHandler<UpdateAreaCommand>
{
    private readonly IAreaRepository _repository;
    private readonly IRepresentativeRepository _reps;
    private readonly ICurrentUser _user;
    public UpdateAreaHandler(IAreaRepository repository, IRepresentativeRepository reps, ICurrentUser user)
    { _repository = repository; _reps = reps; _user = user; }

    public async Task<Unit> Handle(UpdateAreaCommand request, CancellationToken ct)
    {
        var area = await _repository.GetByIdAsync(new AreaId(request.Id), ct)
            ?? throw new NotFoundException("Area", request.Id);
        area.Rename(request.Name);
        area.SetCity(new CityId(request.CityId));
        area.SetTransportation(request.TransportationRequired);
        area.SetRealName(request.RealName);
        area.AssignManager(await AreaRoleSupport.ResolveAsync(request.AreaManagerId,
            Domain.Representatives.RepresentativeType.AreaManager, nameof(request.AreaManagerId), _reps, _user, ct));
        area.AssignResponsible(await AreaRoleSupport.ResolveAsync(request.AreaResponsibleId,
            Domain.Representatives.RepresentativeType.AreaResponsible, nameof(request.AreaResponsibleId), _reps, _user, ct));
        return Unit.Value;
    }
}
