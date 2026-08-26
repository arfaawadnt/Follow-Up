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

// Reports — mirror the reference /api/reports/* payloads.
public sealed record ChartPointDto(string M, int V);
public sealed record CatCountDto(string C, int N);

public sealed record NetworkOverviewDto(
    int SamplesMtd, int CompletionPct, string CompletionDetail, int AvgPerLab, int ActiveLabs,
    int ResolutionPct, string ResolutionDetail, int NewLabsYtd,
    IReadOnlyList<ChartPointDto> Trend, IReadOnlyList<CatCountDto> Cats,
    IReadOnlyList<DashGovRowDto> GovRows, IReadOnlyList<DashSegMixDto> SegMix);

public sealed record RepPerformanceRowDto(
    Guid RepId, string Name, string Type, string GoalType, string? Metric, string GoalDuration,
    decimal Target, decimal Achieved, decimal Pct, string PaceLabel, bool OnTrack, decimal Salary);

public sealed record LabHistoryVisitDto(DateOnly Date, string Time, string? Collector, string Status, int? Samples);
public sealed record LabHistoryComplaintDto(string Reference, string Description, DateOnly Date, string Status);

public sealed record LabHistoryDto(
    string LabDisplayCode, string EncAlias, string Name, string Segment, string Status,
    string? Branch, string? Payer, string? ContractType, string? LicenseNo, DateOnly? LicenseDate,
    string? PreferredChannel, IReadOnlyList<string> VisitTimes, IReadOnlyList<string> WorkDays,
    IReadOnlyList<string> Collectors, string? Marketing, DateOnly Joined, string? Address,
    IReadOnlyList<string> Contacts,
    int AvgMonth, int Mtd, int Completion14Pct, int Missed14, int Complaints,
    IReadOnlyList<ChartPointDto> Months,
    IReadOnlyList<LabHistoryVisitDto> Visits,
    IReadOnlyList<LabHistoryComplaintDto> ComplaintRows);

// Per-visit interval breakdown (minutes) mirroring the reference rep-intervals report.
public sealed record RepIntervalRowDto(
    string CollectorName, string LabName, string LabCode, string? Branch, string? Governorate, string? City, string? Area,
    DateOnly VisitDate, string VisitTime, int? Samples,
    double? PlannedToCollect, double? CollectToTransfer, double? TransferToCheckin, double? TotalCycle,
    string? CheckinTime, string? TransferTime, string? MarkedAt);

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
    Task<IReadOnlyList<RepIntervalRowDto>> GetRepIntervalsAsync(DateOnly start, DateOnly end, OrgScope scope, bool canSeeEncrypted, CancellationToken ct);
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

public sealed record GetRepIntervalsReportQuery(DateOnly? Start = null, DateOnly? End = null) : IQuery<IReadOnlyList<RepIntervalRowDto>>, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ViewReports };
}
public sealed class GetRepIntervalsReportHandler : IQueryHandler<GetRepIntervalsReportQuery, IReadOnlyList<RepIntervalRowDto>>
{
    private readonly IInsightsQueries _q; private readonly ICurrentUser _u; private readonly IClock _clock;
    public GetRepIntervalsReportHandler(IInsightsQueries q, ICurrentUser u, IClock clock) { _q = q; _u = u; _clock = clock; }
    public Task<IReadOnlyList<RepIntervalRowDto>> Handle(GetRepIntervalsReportQuery r, CancellationToken ct)
    {
        var start = r.Start ?? _clock.CairoToday.AddDays(-7);
        var end = r.End ?? _clock.CairoToday;
        return _q.GetRepIntervalsAsync(start, end, _u.Scope, _u.Has(Privileges.ShowEncryptedLabs), ct);
    }
}
