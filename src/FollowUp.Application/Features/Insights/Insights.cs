using FollowUp.Application.Common.Abstractions;
using FollowUp.Application.Common.Messaging;
using FollowUp.Domain.Identity;

namespace FollowUp.Application.Features.Insights;

// ---- DTOs ----

public sealed record DashboardDto(
    int ActiveLabs, int OpenComplaints, int SamplesToday, int MissedToday,
    IReadOnlyList<ScheduleItemDto> TodaySchedule,
    IReadOnlyList<UnresolvedComplaintDto> UnresolvedComplaints,
    IReadOnlyList<RepProgressDto> RepProgress,
    IReadOnlyList<BirthdayDto> Birthdays);

public sealed record ScheduleItemDto(Guid VisitId, string LabDisplayCode, string LabName, string Status, string Time);
public sealed record UnresolvedComplaintDto(Guid Id, string Reference, string LabDisplayCode, string Status);
public sealed record RepProgressDto(Guid RepId, string RepName, decimal AchievementPercent, bool OnTrack);
public sealed record BirthdayDto(string ContactName, string LabDisplayCode, string? Phone);

public sealed record NetworkOverviewDto(int TotalLabs, int ActiveLabs, int SamplesThisMonth, decimal IncomeThisMonth);
public sealed record RepPerformanceRowDto(Guid RepId, string RepName, decimal AchievementPercent, decimal Pace, bool OnTrack, decimal Salary);
public sealed record LabHistoryDto(string LabDisplayCode, string Name, IReadOnlyList<LabHistoryPointDto> Points);
public sealed record LabHistoryPointDto(DateOnly Date, int Samples, string Status);
public sealed record RepIntervalDto(Guid RepId, string RepName, double AverageCycleHours);

/// <summary>
/// Read-side query interface for insights (ADR-0005). Attainment/pace are engine-computed (BR-6/BR-8:
/// rolling 90-day window, on-track ≥ 85%); every surface is scoped to the caller's allowed lab ids.
/// </summary>
public interface IInsightsQueries
{
    Task<DashboardDto> GetDashboardAsync(OrgScope scope, bool canSeeEncrypted, DateOnly today, CancellationToken ct);
    Task<NetworkOverviewDto> GetOverviewAsync(OrgScope scope, CancellationToken ct);
    Task<IReadOnlyList<RepPerformanceRowDto>> GetPerformanceAsync(OrgScope scope, CancellationToken ct);
    Task<LabHistoryDto?> GetLabHistoryAsync(Guid labId, bool canSeeEncrypted, CancellationToken ct);
    Task<IReadOnlyList<RepIntervalDto>> GetRepIntervalsAsync(OrgScope scope, CancellationToken ct);
}

// ---- Dashboard ----

public sealed record GetDashboardQuery : IQuery<DashboardDto>, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ViewDashboard };
}

public sealed class GetDashboardHandler : IQueryHandler<GetDashboardQuery, DashboardDto>
{
    private readonly IInsightsQueries _queries;
    private readonly ICurrentUser _user;
    private readonly IClock _clock;
    public GetDashboardHandler(IInsightsQueries queries, ICurrentUser user, IClock clock)
    { _queries = queries; _user = user; _clock = clock; }

    public Task<DashboardDto> Handle(GetDashboardQuery r, CancellationToken ct) =>
        _queries.GetDashboardAsync(_user.Scope, _user.Has(Privileges.ShowEncryptedLabs), _clock.CairoToday, ct);
}

// ---- Reports ----

public sealed record GetOverviewReportQuery : IQuery<NetworkOverviewDto>, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ViewReports };
}
public sealed class GetOverviewReportHandler : IQueryHandler<GetOverviewReportQuery, NetworkOverviewDto>
{
    private readonly IInsightsQueries _q; private readonly ICurrentUser _u;
    public GetOverviewReportHandler(IInsightsQueries q, ICurrentUser u) { _q = q; _u = u; }
    public Task<NetworkOverviewDto> Handle(GetOverviewReportQuery r, CancellationToken ct) => _q.GetOverviewAsync(_u.Scope, ct);
}

public sealed record GetPerformanceReportQuery : IQuery<IReadOnlyList<RepPerformanceRowDto>>, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ViewReports };
}
public sealed class GetPerformanceReportHandler : IQueryHandler<GetPerformanceReportQuery, IReadOnlyList<RepPerformanceRowDto>>
{
    private readonly IInsightsQueries _q; private readonly ICurrentUser _u;
    public GetPerformanceReportHandler(IInsightsQueries q, ICurrentUser u) { _q = q; _u = u; }
    public Task<IReadOnlyList<RepPerformanceRowDto>> Handle(GetPerformanceReportQuery r, CancellationToken ct) => _q.GetPerformanceAsync(_u.Scope, ct);
}

public sealed record GetLabHistoryReportQuery(Guid LabId) : IQuery<LabHistoryDto>, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ViewReports };
}
public sealed class GetLabHistoryReportHandler : IQueryHandler<GetLabHistoryReportQuery, LabHistoryDto>
{
    private readonly IInsightsQueries _q; private readonly ICurrentUser _u;
    public GetLabHistoryReportHandler(IInsightsQueries q, ICurrentUser u) { _q = q; _u = u; }
    public async Task<LabHistoryDto> Handle(GetLabHistoryReportQuery r, CancellationToken ct) =>
        await _q.GetLabHistoryAsync(r.LabId, _u.Has(Privileges.ShowEncryptedLabs), ct)
        ?? throw new Common.Exceptions.NotFoundException("Laboratory", r.LabId);
}

public sealed record GetRepIntervalsReportQuery : IQuery<IReadOnlyList<RepIntervalDto>>, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ViewReports };
}
public sealed class GetRepIntervalsReportHandler : IQueryHandler<GetRepIntervalsReportQuery, IReadOnlyList<RepIntervalDto>>
{
    private readonly IInsightsQueries _q; private readonly ICurrentUser _u;
    public GetRepIntervalsReportHandler(IInsightsQueries q, ICurrentUser u) { _q = q; _u = u; }
    public Task<IReadOnlyList<RepIntervalDto>> Handle(GetRepIntervalsReportQuery r, CancellationToken ct) => _q.GetRepIntervalsAsync(_u.Scope, ct);
}
