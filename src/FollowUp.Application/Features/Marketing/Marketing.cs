using FollowUp.Application.Common.Abstractions;
using FollowUp.Application.Common.Abstractions.Persistence;
using FollowUp.Application.Common.Exceptions;
using FollowUp.Application.Common.Messaging;
using FollowUp.Application.Common.Models;
using FollowUp.Application.Common.Security;
using FollowUp.Domain.Common;
using FollowUp.Domain.Identity;
using FollowUp.Domain.Laboratories;
using FollowUp.Domain.Marketing;
using FollowUp.Domain.Representatives;
using FluentValidation;
using MediatR;

namespace FollowUp.Application.Features.Marketing;

// ---- Read side ----

public sealed record MarketingVisitDto(
    Guid Id, string Reference, Guid LaboratoryId, string LabDisplayCode, string Lab, string? Area, string? Governorate,
    Guid RepresentativeId, string? Rep, string Purpose, DateOnly ScheduledDate, string? ScheduledTime,
    string? Plan, string Status, string? Outcome);

/// <summary>Read-side query interface; listings surface Scheduled visits first (BR-10).</summary>
public interface IMarketingQueries
{
    Task<PagedResult<MarketingVisitDto>> SearchAsync(MarketingSearchCriteria criteria, OrgScope scope,
        bool canSeeEncrypted, CancellationToken ct);
}

public sealed record MarketingSearchCriteria : ListQuery
{
    public string? Status { get; init; }
    public Guid? LaboratoryId { get; init; }
}

public sealed record GetMarketingVisitsQuery : IQuery<PagedResult<MarketingVisitDto>>, IAuthorizedRequest
{
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 50;
    public string? Search { get; init; }
    public string? Status { get; init; }
    public Guid? LaboratoryId { get; init; }

    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ViewMarketing };
}

public sealed class GetMarketingVisitsHandler : IQueryHandler<GetMarketingVisitsQuery, PagedResult<MarketingVisitDto>>
{
    private readonly IMarketingQueries _queries;
    private readonly ICurrentUser _user;

    public GetMarketingVisitsHandler(IMarketingQueries queries, ICurrentUser user) { _queries = queries; _user = user; }

    public Task<PagedResult<MarketingVisitDto>> Handle(GetMarketingVisitsQuery request, CancellationToken ct)
    {
        var criteria = new MarketingSearchCriteria
        {
            Page = request.Page, PageSize = request.PageSize, Search = request.Search,
            Status = request.Status, LaboratoryId = request.LaboratoryId,
        };
        return _queries.SearchAsync(criteria, _user.Scope, _user.Has(Privileges.ShowEncryptedLabs), ct);
    }
}

// ---- Schedule (create) ----

public sealed record ScheduleMarketingVisitCommand : ICommand<Guid>, IAuthorizedRequest
{
    public Guid LaboratoryId { get; init; }
    public Guid RepresentativeId { get; init; }
    public string Purpose { get; init; } = "Routine";
    public DateOnly ScheduledDate { get; init; }
    public TimeOnly? ScheduledTime { get; init; }
    public string? Plan { get; init; }

    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.AddMarketing };
}

public sealed class ScheduleMarketingVisitValidator : AbstractValidator<ScheduleMarketingVisitCommand>
{
    public ScheduleMarketingVisitValidator()
    {
        RuleFor(x => x.LaboratoryId).NotEmpty();
        RuleFor(x => x.RepresentativeId).NotEmpty();
        RuleFor(x => x.Purpose).NotEmpty();
    }
}

public sealed class ScheduleMarketingVisitHandler : ICommandHandler<ScheduleMarketingVisitCommand, Guid>
{
    private readonly IMarketingVisitRepository _repository;
    private readonly ILaboratoryRepository _labs;
    private readonly IRepresentativeRepository _reps;
    private readonly ICurrentUser _user;

    public ScheduleMarketingVisitHandler(IMarketingVisitRepository repository, ILaboratoryRepository labs,
        IRepresentativeRepository reps, ICurrentUser user)
    {
        _repository = repository; _labs = labs; _reps = reps; _user = user;
    }

    public async Task<Guid> Handle(ScheduleMarketingVisitCommand request, CancellationToken ct)
    {
        var lab = await _labs.GetByIdAsync(new LaboratoryId(request.LaboratoryId), ct)
            ?? throw new NotFoundException("Laboratory", request.LaboratoryId);
        _user.EnsureInScope(lab);

        var repId = new RepresentativeId(request.RepresentativeId);
        if (!await _reps.ExistsAsync(repId, ct))
            throw new NotFoundException("Representative", request.RepresentativeId);

        var number = await _repository.NextNumberAsync(ct);
        var visit = MarketingVisit.Schedule(number, lab.Id, repId,
            Enumeration.FromName<MarketingPurpose>(request.Purpose), request.ScheduledDate,
            request.ScheduledTime, request.Plan);
        _repository.Add(visit); // raises MarketingVisitScheduled -> notification (Outbox, Phase 3)
        return visit.Id.Value;
    }
}

// ---- Complete ----

public sealed record CompleteMarketingVisitCommand(Guid Id, string Outcome) : ICommand, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.UpdateMarketing };
}

public sealed class CompleteMarketingVisitValidator : AbstractValidator<CompleteMarketingVisitCommand>
{
    public CompleteMarketingVisitValidator() => RuleFor(x => x.Outcome).NotEmpty();
}

public sealed class CompleteMarketingVisitHandler : ICommandHandler<CompleteMarketingVisitCommand>
{
    private readonly IMarketingVisitRepository _repository;
    private readonly IClock _clock;

    public CompleteMarketingVisitHandler(IMarketingVisitRepository repository, IClock clock)
    {
        _repository = repository; _clock = clock;
    }

    public async Task<Unit> Handle(CompleteMarketingVisitCommand request, CancellationToken ct)
    {
        var visit = await _repository.GetByIdAsync(new MarketingVisitId(request.Id), ct)
            ?? throw new NotFoundException("Marketing visit", request.Id);
        visit.Complete(request.Outcome, _clock.UtcNow);
        return Unit.Value;
    }
}

// ---- Cancel ----

public sealed record CancelMarketingVisitCommand(Guid Id, string? Reason) : ICommand, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.UpdateMarketing };
}

public sealed class CancelMarketingVisitHandler : ICommandHandler<CancelMarketingVisitCommand>
{
    private readonly IMarketingVisitRepository _repository;

    public CancelMarketingVisitHandler(IMarketingVisitRepository repository) => _repository = repository;

    public async Task<Unit> Handle(CancelMarketingVisitCommand request, CancellationToken ct)
    {
        var visit = await _repository.GetByIdAsync(new MarketingVisitId(request.Id), ct)
            ?? throw new NotFoundException("Marketing visit", request.Id);
        visit.Cancel(request.Reason);
        return Unit.Value;
    }
}
