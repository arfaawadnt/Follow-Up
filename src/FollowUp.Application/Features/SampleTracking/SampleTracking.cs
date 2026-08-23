using FollowUp.Application.Common.Abstractions;
using FollowUp.Application.Common.Abstractions.Persistence;
using FollowUp.Application.Common.Exceptions;
using FollowUp.Application.Common.Messaging;
using FollowUp.Application.Common.Security;
using FollowUp.Domain.Identity;
using FollowUp.Domain.Operations;
using FluentValidation;
using MediatR;
using DomainSampleTracking = FollowUp.Domain.Operations.SampleTracking;

namespace FollowUp.Application.Features.SampleTracking;

// ---- Read side ----

public sealed record SampleTrackingDto(
    Guid Id, string Area, DateOnly Date, int Count,
    string? DataEntryBy, DateTimeOffset? DataEntryAt,
    string? ReviewBy, DateTimeOffset? ReviewAt,
    string? SortBy, DateTimeOffset? SortAt,
    bool IsComplete);

public sealed record SampleLifecycleReportRowDto(string Area, DateOnly Date, int Count, string Stage);

public interface ISampleTrackingQueries
{
    Task<IReadOnlyList<SampleTrackingDto>> ListAsync(DateOnly start, DateOnly end, OrgScope scope, CancellationToken ct);
    Task<IReadOnlyList<SampleLifecycleReportRowDto>> ReportAsync(DateOnly from, DateOnly to, OrgScope scope, CancellationToken ct);
}

/// <summary>Lists sample-tracking rows for a date range within scope (SRS FR-8).</summary>
public sealed record GetSampleTrackingQuery(DateOnly? Start = null, DateOnly? End = null) : IQuery<IReadOnlyList<SampleTrackingDto>>, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.SampleTracking };
}

public sealed class GetSampleTrackingHandler : IQueryHandler<GetSampleTrackingQuery, IReadOnlyList<SampleTrackingDto>>
{
    private readonly ISampleTrackingQueries _queries;
    private readonly ICurrentUser _user;
    private readonly IClock _clock;

    public GetSampleTrackingHandler(ISampleTrackingQueries queries, ICurrentUser user, IClock clock) { _queries = queries; _user = user; _clock = clock; }

    public Task<IReadOnlyList<SampleTrackingDto>> Handle(GetSampleTrackingQuery request, CancellationToken ct)
    {
        var start = request.Start ?? _clock.CairoToday;
        var end = request.End ?? start;
        return _queries.ListAsync(start, end, _user.Scope, ct);
    }
}

/// <summary>Sample lifecycle report over a date range (SRS FR-8).</summary>
public sealed record GetSampleLifecycleReportQuery(DateOnly From, DateOnly To)
    : IQuery<IReadOnlyList<SampleLifecycleReportRowDto>>, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.SampleTracking };
}

public sealed class GetSampleLifecycleReportHandler
    : IQueryHandler<GetSampleLifecycleReportQuery, IReadOnlyList<SampleLifecycleReportRowDto>>
{
    private readonly ISampleTrackingQueries _queries;
    private readonly ICurrentUser _user;

    public GetSampleLifecycleReportHandler(ISampleTrackingQueries queries, ICurrentUser user)
    {
        _queries = queries; _user = user;
    }

    public Task<IReadOnlyList<SampleLifecycleReportRowDto>> Handle(GetSampleLifecycleReportQuery request, CancellationToken ct) =>
        _queries.ReportAsync(request.From, request.To, _user.Scope, ct);
}

// ---- Record data entry (single/upsert) ----

public sealed record RecordSampleDataEntryCommand(string Area, DateOnly Date, int Count) : ICommand<Guid>, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.SampleTracking };
}

public sealed class RecordSampleDataEntryValidator : AbstractValidator<RecordSampleDataEntryCommand>
{
    public RecordSampleDataEntryValidator()
    {
        RuleFor(x => x.Area).NotEmpty();
        RuleFor(x => x.Count).GreaterThanOrEqualTo(0);
    }
}

public sealed class RecordSampleDataEntryHandler : ICommandHandler<RecordSampleDataEntryCommand, Guid>
{
    private readonly ISampleTrackingRepository _repository;
    private readonly ICurrentUser _user;
    private readonly IClock _clock;

    public RecordSampleDataEntryHandler(ISampleTrackingRepository repository, ICurrentUser user, IClock clock)
    {
        _repository = repository; _user = user; _clock = clock;
    }

    public async Task<Guid> Handle(RecordSampleDataEntryCommand request, CancellationToken ct)
    {
        _user.EnsureAreaInScope(request.Area);

        var tracking = await _repository.GetByAreaDateAsync(request.Area, request.Date, ct);
        if (tracking is null)
        {
            tracking = DomainSampleTracking.Open(request.Area, request.Date);
            _repository.Add(tracking);
        }
        tracking.RecordDataEntry(request.Count, _user.Username, _clock.UtcNow);
        return tracking.Id.Value;
    }
}

// ---- Batch data entry ----

public sealed record BatchRecordSampleDataEntryCommand(IReadOnlyList<SampleEntryLine> Lines) : ICommand<int>, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.SampleTracking };
}

public sealed record SampleEntryLine(string Area, DateOnly Date, int Count);

public sealed class BatchRecordSampleDataEntryHandler : ICommandHandler<BatchRecordSampleDataEntryCommand, int>
{
    private readonly ISampleTrackingRepository _repository;
    private readonly ICurrentUser _user;
    private readonly IClock _clock;

    public BatchRecordSampleDataEntryHandler(ISampleTrackingRepository repository, ICurrentUser user, IClock clock)
    {
        _repository = repository; _user = user; _clock = clock;
    }

    public async Task<int> Handle(BatchRecordSampleDataEntryCommand request, CancellationToken ct)
    {
        foreach (var line in request.Lines)
        {
            _user.EnsureAreaInScope(line.Area);
            var tracking = await _repository.GetByAreaDateAsync(line.Area, line.Date, ct);
            if (tracking is null)
            {
                tracking = DomainSampleTracking.Open(line.Area, line.Date);
                _repository.Add(tracking);
            }
            tracking.RecordDataEntry(line.Count, _user.Username, _clock.UtcNow);
        }
        return request.Lines.Count;
    }
}

// ---- Advance step (Review / Sort) ----

public sealed record AdvanceSampleTrackingCommand(Guid Id, string Step) : ICommand, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.SampleTracking };
}

public sealed class AdvanceSampleTrackingHandler : ICommandHandler<AdvanceSampleTrackingCommand>
{
    private readonly ISampleTrackingRepository _repository;
    private readonly ICurrentUser _user;
    private readonly IClock _clock;

    public AdvanceSampleTrackingHandler(ISampleTrackingRepository repository, ICurrentUser user, IClock clock)
    {
        _repository = repository; _user = user; _clock = clock;
    }

    public async Task<Unit> Handle(AdvanceSampleTrackingCommand request, CancellationToken ct)
    {
        var tracking = await _repository.GetByIdAsync(new SampleTrackingId(request.Id), ct)
            ?? throw new NotFoundException("Sample tracking", request.Id);
        _user.EnsureAreaInScope(tracking.Area);

        switch (request.Step)
        {
            case "Review": tracking.RecordReview(_user.Username, _clock.UtcNow); break;
            case "Sort": tracking.RecordSort(_user.Username, _clock.UtcNow); break;
            default: throw new Common.Exceptions.ValidationException(
                new Dictionary<string, string[]> { ["Step"] = new[] { "Step must be Review or Sort." } });
        }
        return Unit.Value;
    }
}
