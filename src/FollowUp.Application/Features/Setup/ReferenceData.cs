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

public sealed record RefItemDto(Guid Id, string Type, string Code, string NameEn, string? NameAr, int SortOrder);
public sealed record CityDto(Guid Id, string Name, string Governorate);
public sealed record AreaDto(Guid Id, string Name, Guid CityId, bool TransportationRequired, IReadOnlyList<Guid> TransferReps);

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

public sealed record CreateRefItemCommand(string Type, string Code, string NameEn, string? NameAr, int SortOrder = 0)
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

public sealed record UpdateRefItemCommand(Guid Id, string Name) : ICommand, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.SetupRefs };
}

public sealed class UpdateRefItemValidator : AbstractValidator<UpdateRefItemCommand>
{
    public UpdateRefItemValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
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
        return Unit.Value;
    }
}

// ---- City write ----

public sealed record CreateCityCommand(string Name, string Governorate) : ICommand<Guid>, IAuthorizedRequest
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

public sealed record UpdateCityCommand(Guid Id, string Name, string Governorate) : ICommand, IAuthorizedRequest
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
        return Unit.Value;
    }
}

// ---- Area write ----

public sealed record CreateAreaCommand(string Name, Guid CityId, bool TransportationRequired, IReadOnlyList<Guid> TransferReps)
    : ICommand<Guid>, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.SetupAreas };
}

public sealed class CreateAreaHandler : ICommandHandler<CreateAreaCommand, Guid>
{
    private readonly IAreaRepository _repository;
    public CreateAreaHandler(IAreaRepository repository) => _repository = repository;

    public Task<Guid> Handle(CreateAreaCommand request, CancellationToken ct)
    {
        var area = Area.Create(request.Name, new CityId(request.CityId), request.TransportationRequired);
        area.SetTransferReps(request.TransferReps.Select(r => new RepresentativeId(r)));
        _repository.Add(area);
        return Task.FromResult(area.Id.Value);
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

public sealed record UpdateAreaCommand(Guid Id, string Name, Guid CityId, bool TransportationRequired)
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
    public UpdateAreaHandler(IAreaRepository repository) => _repository = repository;

    public async Task<Unit> Handle(UpdateAreaCommand request, CancellationToken ct)
    {
        var area = await _repository.GetByIdAsync(new AreaId(request.Id), ct)
            ?? throw new NotFoundException("Area", request.Id);
        area.Rename(request.Name);
        area.SetCity(new CityId(request.CityId));
        area.SetTransportation(request.TransportationRequired);
        return Unit.Value;
    }
}
