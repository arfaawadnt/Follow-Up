using FollowUp.Application.Common.Abstractions;
using FollowUp.Application.Common.Messaging;
using FollowUp.Domain.Identity;

namespace FollowUp.Application.Features.Insights;

// ---- DTOs ----

// Dashboard read model mirrors the reference platform's `/api/dashboard` payload so the SPA maps 1:1.
public sealed record DashboardDto(
    DashboardKpisDto Kpis,
    DashboardBirthdayDto? Bday,
    IReadOnlyList<DashScheduleDto> Schedule,
    IReadOnlyList<DashComplaintDto> Complaints,
    IReadOnlyList<DashRepProgDto> RepProg,
    IReadOnlyList<DashTopLabDto> TopLabs,
    IReadOnlyList<int> Trend,
    IReadOnlyList<DashSegMixDto> SegMix,
    IReadOnlyList<DashGovRowDto> GovRows);

public sealed record DashboardKpisDto(
    int ActiveLabs, int TotalLabs, int Done, int TotalVisits, int Pending, int Missed,
    int SamplesToday, int OpenComplaints, int InProgress, int Resolved,
    long Mtd, long Target, string MonthName);

public sealed record DashboardBirthdayDto(string Text);
public sealed record DashScheduleDto(Guid Id, string Time, string Lab, string? Area, string Rep, string Status, int? Samples, bool TransferDone);
public sealed record DashComplaintDto(string Id, string Lab, string Description, string Category, int Age);
public sealed record DashRepProgDto(string Name, string Detail, int Pct);
public sealed record DashTopLabDto(string Name, string? Area, int V);
public sealed record DashSegMixDto(string Seg, int C);
public sealed record DashGovRowDto(string G, int V);

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
