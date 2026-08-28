using FollowUp.Application.Common.Abstractions;
using FollowUp.Application.Common.Abstractions.Persistence;
using FollowUp.Application.Common.Exceptions;
using FollowUp.Application.Common.Messaging;
using FollowUp.Application.Common.Security;
using FollowUp.Domain.Common;
using FollowUp.Domain.Compensation;
using FollowUp.Domain.Identity;
using FollowUp.Domain.Laboratories;
using FollowUp.Domain.Representatives;
using FluentValidation;
using MediatR;

namespace FollowUp.Application.Features.Compensation;

// ---- Read side ----

public sealed record LoyaltyLedgerDto(Guid LaboratoryId, int Period, int Target, int Achieved, int Points, string? Tier);
public sealed record LoyaltyRowDto(Guid LaboratoryId, string Code, string Name, string? Branch, string? City,
    int MonthlyTarget, int MtdSamples, int LoyaltyPoints, string? LoyaltyTier);
public sealed record CommissionDto(Guid RepId, string Name, string Type, string GoalType, int Period,
    decimal TargetAmount, decimal AchievedAmount, decimal BaseSalary, decimal CommissionEarned,
    decimal BonusEarned, decimal TotalPayout, bool IsLocked);
public sealed record CompensationConfigDto(decimal CommissionRatePercent, decimal BonusThresholdPercent,
    decimal BonusAmount, IReadOnlyList<LoyaltyTierDto> Tiers);
public sealed record LoyaltyTierDto(string Name, decimal MinAchievementPercent, int Points);

public interface ICompensationQueries
{
    Task<IReadOnlyList<LoyaltyLedgerDto>> GetLedgersAsync(int period, OrgScope scope, CancellationToken ct);
    Task<IReadOnlyList<LoyaltyRowDto>> GetLoyaltySummaryAsync(OrgScope scope, bool canSeeEncrypted, CancellationToken ct);
    Task<IReadOnlyList<LoyaltyLedgerDto>> GetLabLedgerAsync(Guid labId, OrgScope scope, CancellationToken ct);
    Task<IReadOnlyList<CommissionDto>> GetCommissionsAsync(int period, OrgScope scope, CancellationToken ct);
    Task<CompensationConfigDto?> GetConfigAsync(CancellationToken ct);
}

// ---- Set lab loyalty target ----

public sealed record SetLabTargetCommand(Guid LaboratoryId, int MonthlyTarget) : ICommand, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ManageLoyalty };
}

public sealed class SetLabTargetValidator : AbstractValidator<SetLabTargetCommand>
{
    public SetLabTargetValidator() => RuleFor(x => x.MonthlyTarget).GreaterThanOrEqualTo(0);
}

public sealed class SetLabTargetHandler : ICommandHandler<SetLabTargetCommand>
{
    private readonly ILaboratoryRepository _labs;
    private readonly ICurrentUser _user;

    public SetLabTargetHandler(ILaboratoryRepository labs, ICurrentUser user) { _labs = labs; _user = user; }

    public async Task<Unit> Handle(SetLabTargetCommand request, CancellationToken ct)
    {
        var lab = await _labs.GetByIdAsync(new LaboratoryId(request.LaboratoryId), ct)
            ?? throw new NotFoundException("Laboratory", request.LaboratoryId);
        _user.EnsureInScope(lab);
        lab.SetMonthlyTarget(request.MonthlyTarget);
        return Unit.Value;
    }
}

// ---- Recalculate loyalty for a lab + period ----

public sealed record RecalculateLoyaltyCommand(Guid LaboratoryId, int Period) : ICommand, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ManageLoyalty };
}

public sealed class RecalculateLoyaltyHandler : ICommandHandler<RecalculateLoyaltyCommand>
{
    private readonly ILaboratoryRepository _labs;
    private readonly ILabLoyaltyLedgerRepository _ledgers;
    private readonly ICompensationConfigRepository _configs;
    private readonly ICompensationData _data;
    private readonly ICurrentUser _user;
    private readonly IClock _clock;

    public RecalculateLoyaltyHandler(ILaboratoryRepository labs, ILabLoyaltyLedgerRepository ledgers,
        ICompensationConfigRepository configs, ICompensationData data, ICurrentUser user, IClock clock)
    {
        _labs = labs; _ledgers = ledgers; _configs = configs; _data = data; _user = user; _clock = clock;
    }

    public async Task<Unit> Handle(RecalculateLoyaltyCommand request, CancellationToken ct)
    {
        var lab = await _labs.GetByIdAsync(new LaboratoryId(request.LaboratoryId), ct)
            ?? throw new NotFoundException("Laboratory", request.LaboratoryId);
        _user.EnsureInScope(lab);

        var config = await _configs.GetAsync(ct)
            ?? throw new ConflictException("Compensation configuration has not been set.");
        var period = YearMonth.FromCode(request.Period);
        var achieved = await _data.GetLabAchievedSamplesAsync(lab.Id, period, ct);

        var (points, tier) = new CompensationCalculator(config).ComputeLoyalty(achieved, lab.MonthlyTarget);

        var ledger = await _ledgers.GetAsync(lab.Id, period, ct);
        if (ledger is null)
        {
            ledger = LabLoyaltyLedger.For(lab.Id, period);
            _ledgers.Add(ledger);
        }
        ledger.Record(lab.MonthlyTarget, achieved, points, tier, _clock.UtcNow);
        lab.SetLoyalty(lab.MonthlyTarget, points, tier); // update snapshot on the lab
        return Unit.Value;
    }
}

// ---- Recalculate loyalty for every in-scope lab (current period) ----

/// <summary>Backs the global "Recalculate points" action: recomputes loyalty for all in-scope labs for the
/// current period. Returns the number of labs recalculated.</summary>
public sealed record RecalculateAllLoyaltyCommand : ICommand<int>, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ManageLoyalty };
}

public sealed class RecalculateAllLoyaltyHandler : ICommandHandler<RecalculateAllLoyaltyCommand, int>
{
    private readonly ILaboratoryRepository _labs;
    private readonly ILabLoyaltyLedgerRepository _ledgers;
    private readonly ICompensationConfigRepository _configs;
    private readonly ICompensationData _data;
    private readonly ICompensationQueries _queries;
    private readonly ICurrentUser _user;
    private readonly IClock _clock;

    public RecalculateAllLoyaltyHandler(ILaboratoryRepository labs, ILabLoyaltyLedgerRepository ledgers,
        ICompensationConfigRepository configs, ICompensationData data, ICompensationQueries queries,
        ICurrentUser user, IClock clock)
    {
        _labs = labs; _ledgers = ledgers; _configs = configs; _data = data; _queries = queries; _user = user; _clock = clock;
    }

    public async Task<int> Handle(RecalculateAllLoyaltyCommand request, CancellationToken ct)
    {
        var config = await _configs.GetAsync(ct)
            ?? throw new ConflictException("Compensation configuration has not been set.");
        var period = YearMonth.From(_clock.CairoToday);
        var calculator = new CompensationCalculator(config);

        // In-scope labs only (the summary query already applies the caller's OrgScope).
        var summary = await _queries.GetLoyaltySummaryAsync(_user.Scope, _user.Has(Privileges.ShowEncryptedLabs), ct);

        var count = 0;
        foreach (var row in summary)
        {
            var lab = await _labs.GetByIdAsync(new LaboratoryId(row.LaboratoryId), ct);
            if (lab is null) continue;

            var achieved = await _data.GetLabAchievedSamplesAsync(lab.Id, period, ct);
            var (points, tier) = calculator.ComputeLoyalty(achieved, lab.MonthlyTarget);

            var ledger = await _ledgers.GetAsync(lab.Id, period, ct);
            if (ledger is null) { ledger = LabLoyaltyLedger.For(lab.Id, period); _ledgers.Add(ledger); }
            ledger.Record(lab.MonthlyTarget, achieved, points, tier, _clock.UtcNow);
            lab.SetLoyalty(lab.MonthlyTarget, points, tier);
            count++;
        }
        return count;
    }
}

// ---- Save commission (server-side recompute, BR-9) ----

public sealed record SaveCommissionCommand(Guid RepresentativeId, int Period) : ICommand, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ManageCommissions };
}

public sealed class SaveCommissionHandler : ICommandHandler<SaveCommissionCommand>
{
    private readonly IRepresentativeRepository _reps;
    private readonly IRepCommissionRepository _commissions;
    private readonly ICompensationConfigRepository _configs;
    private readonly ICompensationData _data;
    private readonly ICurrentUser _user;
    private readonly IClock _clock;

    public SaveCommissionHandler(IRepresentativeRepository reps, IRepCommissionRepository commissions,
        ICompensationConfigRepository configs, ICompensationData data, ICurrentUser user, IClock clock)
    {
        _reps = reps; _commissions = commissions; _configs = configs; _data = data; _user = user; _clock = clock;
    }

    public async Task<Unit> Handle(SaveCommissionCommand request, CancellationToken ct)
    {
        var rep = await _reps.GetByIdAsync(new RepresentativeId(request.RepresentativeId), ct)
            ?? throw new NotFoundException("Representative", request.RepresentativeId);
        _user.EnsureInScope(rep); // finding CPN-3: resource-level org-scope check before the payroll write
        var config = await _configs.GetAsync(ct)
            ?? throw new ConflictException("Compensation configuration has not been set.");

        var period = YearMonth.FromCode(request.Period);
        var achieved = await _data.GetRepAchievedSamplesAsync(rep.Id, period, ct);
        var target = rep.Target.Amount;

        // BR-9: every figure recomputed server-side; client-supplied amounts are ignored entirely.
        var (commission, bonus) = new CompensationCalculator(config).ComputeCommission(achieved, target, rep.Salary);

        var record = await _commissions.GetAsync(rep.Id, period, ct);
        if (record is null)
        {
            record = RepCommission.For(rep.Id, period);
            _commissions.Add(record);
        }
        record.Recompute(target, achieved, rep.Salary, commission, bonus, _clock.UtcNow);
        return Unit.Value;
    }
}

// ---- Set compensation config ----

public sealed record SetCompensationConfigCommand(
    decimal CommissionRatePercent, decimal BonusThresholdPercent, decimal BonusAmount,
    IReadOnlyList<LoyaltyTierInput> Tiers) : ICommand, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ManageUsers, Privileges.SetupRefs };
}

public sealed record LoyaltyTierInput(string Name, decimal MinAchievementPercent, int Points);

public sealed class SetCompensationConfigHandler : ICommandHandler<SetCompensationConfigCommand>
{
    private readonly ICompensationConfigRepository _configs;

    public SetCompensationConfigHandler(ICompensationConfigRepository configs) => _configs = configs;

    public async Task<Unit> Handle(SetCompensationConfigCommand request, CancellationToken ct)
    {
        var tiers = request.Tiers.Select(t => new LoyaltyTier(t.Name, t.MinAchievementPercent, t.Points));
        var existing = await _configs.GetAsync(ct);
        if (existing is null)
        {
            _configs.Add(CompensationConfig.Create(request.CommissionRatePercent, request.BonusThresholdPercent,
                new Money(request.BonusAmount), tiers));
        }
        else
        {
            existing.SetCommission(request.CommissionRatePercent, request.BonusThresholdPercent, new Money(request.BonusAmount));
            existing.SetTiers(tiers);
        }
        return Unit.Value;
    }
}

// ---- Queries ----

public sealed record GetLoyaltyQuery : IQuery<IReadOnlyList<LoyaltyRowDto>>, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ManageLoyalty };
}

public sealed class GetLoyaltyHandler : IQueryHandler<GetLoyaltyQuery, IReadOnlyList<LoyaltyRowDto>>
{
    private readonly ICompensationQueries _queries;
    private readonly ICurrentUser _user;
    public GetLoyaltyHandler(ICompensationQueries queries, ICurrentUser user) { _queries = queries; _user = user; }
    public Task<IReadOnlyList<LoyaltyRowDto>> Handle(GetLoyaltyQuery request, CancellationToken ct) =>
        _queries.GetLoyaltySummaryAsync(_user.Scope, _user.Has(Privileges.ShowEncryptedLabs), ct);
}

public sealed record GetLoyaltyLedgerQuery(Guid LabId) : IQuery<IReadOnlyList<LoyaltyLedgerDto>>, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ManageLoyalty };
}

public sealed class GetLoyaltyLedgerHandler : IQueryHandler<GetLoyaltyLedgerQuery, IReadOnlyList<LoyaltyLedgerDto>>
{
    private readonly ICompensationQueries _queries;
    private readonly ICurrentUser _user;
    public GetLoyaltyLedgerHandler(ICompensationQueries queries, ICurrentUser user) { _queries = queries; _user = user; }
    public Task<IReadOnlyList<LoyaltyLedgerDto>> Handle(GetLoyaltyLedgerQuery request, CancellationToken ct) =>
        _queries.GetLabLedgerAsync(request.LabId, _user.Scope, ct);
}

public sealed record GetCommissionsQuery(int Period) : IQuery<IReadOnlyList<CommissionDto>>, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ManageCommissions };
}

public sealed class GetCommissionsHandler : IQueryHandler<GetCommissionsQuery, IReadOnlyList<CommissionDto>>
{
    private readonly ICompensationQueries _queries;
    private readonly ICurrentUser _user;
    public GetCommissionsHandler(ICompensationQueries queries, ICurrentUser user) { _queries = queries; _user = user; }
    public Task<IReadOnlyList<CommissionDto>> Handle(GetCommissionsQuery request, CancellationToken ct) =>
        _queries.GetCommissionsAsync(request.Period, _user.Scope, ct);
}

public sealed record GetCompensationConfigQuery : IQuery<CompensationConfigDto?>, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ManageUsers, Privileges.SetupRefs };
}

public sealed class GetCompensationConfigHandler : IQueryHandler<GetCompensationConfigQuery, CompensationConfigDto?>
{
    private readonly ICompensationQueries _queries;
    public GetCompensationConfigHandler(ICompensationQueries queries) => _queries = queries;
    public Task<CompensationConfigDto?> Handle(GetCompensationConfigQuery request, CancellationToken ct) =>
        _queries.GetConfigAsync(ct);
}
