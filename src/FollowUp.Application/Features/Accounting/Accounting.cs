using FollowUp.Application.Common.Abstractions;
using FollowUp.Application.Common.Abstractions.Persistence;
using FollowUp.Application.Common.Exceptions;
using FollowUp.Application.Common.Messaging;
using FollowUp.Application.Common.Security;
using FollowUp.Domain.Accounting;
using FollowUp.Domain.Common;
using FollowUp.Domain.Identity;
using FollowUp.Domain.Laboratories;
using FollowUp.Domain.Reference;
using FollowUp.Domain.Representatives;
using FluentValidation;
using MediatR;

namespace FollowUp.Application.Features.Accounting;

// =====================================================================================================================
// Accounting feature slice: five report pages (Penalty Statement, Deductions, Treasury, Collection, Rep Statement) plus
// the treasury/reason configuration. Reads need ViewAccounting; writes need ManageAccounting (which implies View).
// Record-level org scope is enforced on every write (lab / area / rep / treasury-branch) and pushed into every read.
// =====================================================================================================================

// ---- Read side: DTOs ----

public sealed record TreasuryReasonDto(Guid Id, string Name, bool IsActive);
public sealed record TreasuryDto(Guid Id, string Name, IReadOnlyList<string> Branches, bool IsActive);
public sealed record TreasuryEntryDto(Guid Id, long Serial, DateOnly Date, Guid TreasuryId, string TreasuryName,
    decimal Debit, decimal Credit, Guid ReasonId, string ReasonName, string? Notes);

public sealed record PenaltyDto(Guid Id, long Serial, DateOnly Date, Guid LaboratoryId, string LabDisplayCode, string LabName,
    string AccNo, string PatientName, string WrongTestCode, string WrongTestName, decimal WrongValue,
    string RightTestCode, string RightTestName, decimal RightValue, decimal Penalty,
    string UserType, Guid? PerformedById, string? PerformedByName);

/// <summary>A person a penalty can be attributed to: an in-scope active representative (UserType = Rep) or an active
/// system user (DataEntry / Technician). <c>Detail</c> carries the rep type for disambiguation.</summary>
public sealed record PenaltyActorDto(Guid Id, string Name, string? Detail);

public sealed record DeductionDto(Guid Id, long Serial, DateOnly Date, Guid AreaId, string AreaName, string Reason, decimal Value,
    string? Notes, DateOnly? PeriodFrom, DateOnly? PeriodTo,
    string Origin = nameof(DeductionOrigin.Manual), bool IsAdjusted = false, string? SystemNote = null,
    Guid? PenaltyRecordId = null, long? PenaltySerial = null);

/// <summary>A server-suggested deduction value and the basis it was computed from (shown beside the editable field).</summary>
public sealed record DeductionSuggestionDto(decimal Value, string Basis);

public sealed record CollectionDto(Guid Id, long Serial, DateOnly Date, Guid LaboratoryId, string LabDisplayCode, string LabName,
    string Type, IReadOnlyList<Guid> RepIds, IReadOnlyList<string> RepNames, decimal Cash, decimal Bank, decimal Total,
    string? Iban, string? DoneBy, string? Notes);

/// <summary>One statement line. Kind: OracleIncome (derived from the rep's labs' synced income), ManualIncome (a
/// RepIncomeEntry, deletable via SourceId), or Collection (Credit). Balance is the running Debit − Credit.</summary>
public sealed record RepStatementRowDto(DateOnly Date, string Kind, decimal Debit, decimal Credit, string? Notes, decimal Balance, Guid? SourceId);
public sealed record RepStatementDto(Guid RepresentativeId, string RepName, IReadOnlyList<RepStatementRowDto> Rows,
    decimal TotalDebit, decimal TotalCredit, decimal Balance);

// ---- Read side: query interface (implemented in Infrastructure, org scope pushed into SQL) ----

public interface IAccountingQueries
{
    Task<IReadOnlyList<TreasuryReasonDto>> TreasuryReasonsAsync(CancellationToken ct);
    Task<IReadOnlyList<TreasuryDto>> TreasuriesAsync(OrgScope scope, CancellationToken ct);
    Task<IReadOnlyList<TreasuryEntryDto>> TreasuryEntriesAsync(DateOnly from, DateOnly to, Guid? treasuryId, OrgScope scope, CancellationToken ct);
    Task<IReadOnlyList<PenaltyDto>> PenaltiesAsync(DateOnly from, DateOnly to, Guid? laboratoryId, OrgScope scope, bool canSeeEncrypted, CancellationToken ct);
    Task<IReadOnlyList<PenaltyActorDto>> PenaltyActorsAsync(PenaltyUser userType, OrgScope scope, CancellationToken ct);
    Task<IReadOnlyList<DeductionDto>> DeductionsAsync(DateOnly from, DateOnly to, Guid? areaId, OrgScope scope, CancellationToken ct);
    Task<DeductionSuggestionDto> SuggestDeductionAsync(Guid areaId, DeductionReason reason, DateOnly from, DateOnly to, OrgScope scope, CancellationToken ct);
    Task<IReadOnlyList<CollectionDto>> CollectionsAsync(DateOnly from, DateOnly to, Guid? laboratoryId, Guid? repId, OrgScope scope, bool canSeeEncrypted, CancellationToken ct);
    Task<RepStatementDto?> RepStatementAsync(Guid repId, DateOnly from, DateOnly to, OrgScope scope, CancellationToken ct);
}

/// <summary>Treasury rows are org-scoped on the Branch dimension: visible when the scope is wildcard on Branches or
/// covers at least one of the treasury's branches. Shared by the write-side guard and the read-side filter.</summary>
public static class TreasuryScope
{
    public static bool IsVisible(OrgScope scope, IEnumerable<string> branches) =>
        scope.Branches.Contains(OrgScope.Wildcard) || branches.Any(b => scope.Branches.Contains(b));

    public static void EnsureInScope(ICurrentUser user, Treasury treasury)
    {
        if (!IsVisible(user.Scope, treasury.Branches))
            throw new ForbiddenException("The treasury is outside your organizational scope.");
    }
}

internal static class EnumParse
{
    public static T Name<T>(string value, string field) where T : Enumeration
    {
        var match = Enumeration.GetAll<T>().FirstOrDefault(e => string.Equals(e.Name, value, StringComparison.OrdinalIgnoreCase));
        return match ?? throw new Common.Exceptions.ValidationException(new Dictionary<string, string[]>
        {
            [field] = new[] { $"'{value}' is not a valid {typeof(T).Name}." }
        });
    }

    public static bool IsValid<T>(string? value) where T : Enumeration =>
        value is not null && Enumeration.GetAll<T>().Any(e => string.Equals(e.Name, value, StringComparison.OrdinalIgnoreCase));
}

// ---- Queries ----

public sealed record GetTreasuryReasonsQuery() : IQuery<IReadOnlyList<TreasuryReasonDto>>, IAuthorizedRequest
{ public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ViewAccounting }; }
public sealed class GetTreasuryReasonsHandler : IQueryHandler<GetTreasuryReasonsQuery, IReadOnlyList<TreasuryReasonDto>>
{
    private readonly IAccountingQueries _q;
    public GetTreasuryReasonsHandler(IAccountingQueries q) => _q = q;
    public Task<IReadOnlyList<TreasuryReasonDto>> Handle(GetTreasuryReasonsQuery r, CancellationToken ct) => _q.TreasuryReasonsAsync(ct);
}

public sealed record GetTreasuriesQuery() : IQuery<IReadOnlyList<TreasuryDto>>, IAuthorizedRequest
{ public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ViewAccounting }; }
public sealed class GetTreasuriesHandler : IQueryHandler<GetTreasuriesQuery, IReadOnlyList<TreasuryDto>>
{
    private readonly IAccountingQueries _q; private readonly ICurrentUser _user;
    public GetTreasuriesHandler(IAccountingQueries q, ICurrentUser user) { _q = q; _user = user; }
    public Task<IReadOnlyList<TreasuryDto>> Handle(GetTreasuriesQuery r, CancellationToken ct) => _q.TreasuriesAsync(_user.Scope, ct);
}

public sealed record GetTreasuryEntriesQuery(DateOnly From, DateOnly To, Guid? TreasuryId) : IQuery<IReadOnlyList<TreasuryEntryDto>>, IAuthorizedRequest
{ public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ViewAccounting }; }
public sealed class GetTreasuryEntriesHandler : IQueryHandler<GetTreasuryEntriesQuery, IReadOnlyList<TreasuryEntryDto>>
{
    private readonly IAccountingQueries _q; private readonly ICurrentUser _user;
    public GetTreasuryEntriesHandler(IAccountingQueries q, ICurrentUser user) { _q = q; _user = user; }
    public Task<IReadOnlyList<TreasuryEntryDto>> Handle(GetTreasuryEntriesQuery r, CancellationToken ct) =>
        _q.TreasuryEntriesAsync(r.From, r.To, r.TreasuryId, _user.Scope, ct);
}

public sealed record GetPenaltiesQuery(DateOnly From, DateOnly To, Guid? LaboratoryId) : IQuery<IReadOnlyList<PenaltyDto>>, IAuthorizedRequest
{ public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ViewAccounting }; }
public sealed class GetPenaltiesHandler : IQueryHandler<GetPenaltiesQuery, IReadOnlyList<PenaltyDto>>
{
    private readonly IAccountingQueries _q; private readonly ICurrentUser _user;
    public GetPenaltiesHandler(IAccountingQueries q, ICurrentUser user) { _q = q; _user = user; }
    public Task<IReadOnlyList<PenaltyDto>> Handle(GetPenaltiesQuery r, CancellationToken ct) =>
        _q.PenaltiesAsync(r.From, r.To, r.LaboratoryId, _user.Scope, _user.Has(Privileges.ShowEncryptedLabs), ct);
}

/// <summary>The people a penalty can be attributed to for a user type — the "User" picker of the record dialog.
/// Recording needs ManageAccounting, so the lookup is gated the same way (it lists usernames, cf. IDN-9).</summary>
public sealed record GetPenaltyActorsQuery(string UserType) : IQuery<IReadOnlyList<PenaltyActorDto>>, IAuthorizedRequest
{ public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ManageAccounting }; }
public sealed class GetPenaltyActorsHandler : IQueryHandler<GetPenaltyActorsQuery, IReadOnlyList<PenaltyActorDto>>
{
    private readonly IAccountingQueries _q; private readonly ICurrentUser _user;
    public GetPenaltyActorsHandler(IAccountingQueries q, ICurrentUser user) { _q = q; _user = user; }
    public Task<IReadOnlyList<PenaltyActorDto>> Handle(GetPenaltyActorsQuery r, CancellationToken ct) =>
        _q.PenaltyActorsAsync(EnumParse.Name<PenaltyUser>(r.UserType, nameof(r.UserType)), _user.Scope, ct);
}

public sealed record GetDeductionsQuery(DateOnly From, DateOnly To, Guid? AreaId) : IQuery<IReadOnlyList<DeductionDto>>, IAuthorizedRequest
{ public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ViewAccounting }; }
public sealed class GetDeductionsHandler : IQueryHandler<GetDeductionsQuery, IReadOnlyList<DeductionDto>>
{
    private readonly IAccountingQueries _q; private readonly ICurrentUser _user;
    public GetDeductionsHandler(IAccountingQueries q, ICurrentUser user) { _q = q; _user = user; }
    public Task<IReadOnlyList<DeductionDto>> Handle(GetDeductionsQuery r, CancellationToken ct) =>
        _q.DeductionsAsync(r.From, r.To, r.AreaId, _user.Scope, ct);
}

/// <summary>Suggests a deduction value: Penalty = Σ(wrong − right) over the area's labs; PercentageDeal = area income ×
/// the area's deal percentage. Transportation has no suggestion (typed) and is refused by the validator.</summary>
public sealed record SuggestDeductionQuery(Guid AreaId, string Reason, DateOnly From, DateOnly To) : IQuery<DeductionSuggestionDto>, IAuthorizedRequest
{ public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ViewAccounting }; }
public sealed class SuggestDeductionValidator : AbstractValidator<SuggestDeductionQuery>
{
    public SuggestDeductionValidator()
    {
        RuleFor(x => x.AreaId).NotEmpty();
        RuleFor(x => x.Reason).Must(r => r is nameof(DeductionReason.Penalty) or nameof(DeductionReason.PercentageDeal))
            .WithMessage("Only Penalty and PercentageDeal deductions have a suggested value.");
        RuleFor(x => x.To).GreaterThanOrEqualTo(x => x.From);
    }
}
public sealed class SuggestDeductionHandler : IQueryHandler<SuggestDeductionQuery, DeductionSuggestionDto>
{
    private readonly IAccountingQueries _q; private readonly ICurrentUser _user;
    public SuggestDeductionHandler(IAccountingQueries q, ICurrentUser user) { _q = q; _user = user; }
    public Task<DeductionSuggestionDto> Handle(SuggestDeductionQuery r, CancellationToken ct) =>
        _q.SuggestDeductionAsync(r.AreaId, EnumParse.Name<DeductionReason>(r.Reason, nameof(r.Reason)), r.From, r.To, _user.Scope, ct);
}

public sealed record GetCollectionsQuery(DateOnly From, DateOnly To, Guid? LaboratoryId, Guid? RepId) : IQuery<IReadOnlyList<CollectionDto>>, IAuthorizedRequest
{ public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ViewAccounting }; }
public sealed class GetCollectionsHandler : IQueryHandler<GetCollectionsQuery, IReadOnlyList<CollectionDto>>
{
    private readonly IAccountingQueries _q; private readonly ICurrentUser _user;
    public GetCollectionsHandler(IAccountingQueries q, ICurrentUser user) { _q = q; _user = user; }
    public Task<IReadOnlyList<CollectionDto>> Handle(GetCollectionsQuery r, CancellationToken ct) =>
        _q.CollectionsAsync(r.From, r.To, r.LaboratoryId, r.RepId, _user.Scope, _user.Has(Privileges.ShowEncryptedLabs), ct);
}

public sealed record GetRepStatementQuery(Guid RepresentativeId, DateOnly From, DateOnly To) : IQuery<RepStatementDto>, IAuthorizedRequest
{ public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ViewAccounting }; }
public sealed class GetRepStatementHandler : IQueryHandler<GetRepStatementQuery, RepStatementDto>
{
    private readonly IAccountingQueries _q; private readonly ICurrentUser _user;
    public GetRepStatementHandler(IAccountingQueries q, ICurrentUser user) { _q = q; _user = user; }
    public async Task<RepStatementDto> Handle(GetRepStatementQuery r, CancellationToken ct) =>
        await _q.RepStatementAsync(r.RepresentativeId, r.From, r.To, _user.Scope, ct)
        ?? throw new NotFoundException("Representative", r.RepresentativeId); // also hides out-of-scope reps
}

// ---- Commands: treasury configuration ----

public sealed record CreateTreasuryReasonCommand(string Name) : ICommand<Guid>, IAuthorizedRequest
{ public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ManageAccounting }; }
public sealed class CreateTreasuryReasonValidator : AbstractValidator<CreateTreasuryReasonCommand>
{ public CreateTreasuryReasonValidator() => RuleFor(x => x.Name).NotEmpty().MaximumLength(100); }
public sealed class CreateTreasuryReasonHandler : ICommandHandler<CreateTreasuryReasonCommand, Guid>
{
    private readonly ITreasuryReasonRepository _repo;
    public CreateTreasuryReasonHandler(ITreasuryReasonRepository repo) => _repo = repo;
    public Task<Guid> Handle(CreateTreasuryReasonCommand r, CancellationToken ct)
    {
        var reason = TreasuryReason.Create(r.Name);
        _repo.Add(reason);
        return Task.FromResult(reason.Id.Value);
    }
}

public sealed record UpdateTreasuryReasonCommand(Guid Id, string Name, bool IsActive) : ICommand, IAuthorizedRequest
{ public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ManageAccounting }; }
public sealed class UpdateTreasuryReasonValidator : AbstractValidator<UpdateTreasuryReasonCommand>
{ public UpdateTreasuryReasonValidator() { RuleFor(x => x.Id).NotEmpty(); RuleFor(x => x.Name).NotEmpty().MaximumLength(100); } }
public sealed class UpdateTreasuryReasonHandler : ICommandHandler<UpdateTreasuryReasonCommand>
{
    private readonly ITreasuryReasonRepository _repo;
    public UpdateTreasuryReasonHandler(ITreasuryReasonRepository repo) => _repo = repo;
    public async Task<Unit> Handle(UpdateTreasuryReasonCommand r, CancellationToken ct)
    {
        var reason = await _repo.GetByIdAsync(new TreasuryReasonId(r.Id), ct) ?? throw new NotFoundException("TreasuryReason", r.Id);
        reason.Rename(r.Name);
        reason.Activate(r.IsActive);
        return Unit.Value;
    }
}

public sealed record CreateTreasuryCommand(string Name, IReadOnlyList<string> Branches) : ICommand<Guid>, IAuthorizedRequest
{ public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ManageAccounting }; }
public sealed class CreateTreasuryValidator : AbstractValidator<CreateTreasuryCommand>
{
    public CreateTreasuryValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Branches).NotNull().Must(b => b.Any(s => !string.IsNullOrWhiteSpace(s))).WithMessage("Assign at least one branch.");
    }
}
public sealed class CreateTreasuryHandler : ICommandHandler<CreateTreasuryCommand, Guid>
{
    private readonly ITreasuryRepository _repo; private readonly ICurrentUser _user;
    public CreateTreasuryHandler(ITreasuryRepository repo, ICurrentUser user) { _repo = repo; _user = user; }
    public Task<Guid> Handle(CreateTreasuryCommand r, CancellationToken ct)
    {
        var treasury = Treasury.Create(r.Name, r.Branches);
        TreasuryScope.EnsureInScope(_user, treasury); // a scoped admin can only create treasuries on branches they cover
        _repo.Add(treasury);
        return Task.FromResult(treasury.Id.Value);
    }
}

public sealed record UpdateTreasuryCommand(Guid Id, string Name, IReadOnlyList<string> Branches, bool IsActive) : ICommand, IAuthorizedRequest
{ public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ManageAccounting }; }
public sealed class UpdateTreasuryValidator : AbstractValidator<UpdateTreasuryCommand>
{
    public UpdateTreasuryValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Branches).NotNull().Must(b => b.Any(s => !string.IsNullOrWhiteSpace(s))).WithMessage("Assign at least one branch.");
    }
}
public sealed class UpdateTreasuryHandler : ICommandHandler<UpdateTreasuryCommand>
{
    private readonly ITreasuryRepository _repo; private readonly ICurrentUser _user;
    public UpdateTreasuryHandler(ITreasuryRepository repo, ICurrentUser user) { _repo = repo; _user = user; }
    public async Task<Unit> Handle(UpdateTreasuryCommand r, CancellationToken ct)
    {
        var treasury = await _repo.GetByIdAsync(new TreasuryId(r.Id), ct) ?? throw new NotFoundException("Treasury", r.Id);
        TreasuryScope.EnsureInScope(_user, treasury);
        treasury.Rename(r.Name);
        treasury.SetBranches(r.Branches);
        TreasuryScope.EnsureInScope(_user, treasury); // the new branch set must still be within the caller's scope
        treasury.Activate(r.IsActive);
        return Unit.Value;
    }
}

// ---- Commands: treasury entries ----

public sealed record CreateTreasuryEntryCommand(Guid TreasuryId, DateOnly Date, decimal Debit, decimal Credit, Guid ReasonId, string? Notes)
    : ICommand<Guid>, IAuthorizedRequest
{ public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ManageAccounting }; }
public sealed class CreateTreasuryEntryValidator : AbstractValidator<CreateTreasuryEntryCommand>
{
    public CreateTreasuryEntryValidator()
    {
        RuleFor(x => x.TreasuryId).NotEmpty();
        RuleFor(x => x.ReasonId).NotEmpty();
        RuleFor(x => x.Debit).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Credit).GreaterThanOrEqualTo(0);
        RuleFor(x => x).Must(x => (x.Debit > 0) != (x.Credit > 0)).WithMessage("Enter either a debit (cash in) or a credit (expense out), not both and not neither.");
        RuleFor(x => x.Notes).MaximumLength(500);
    }
}
public sealed class CreateTreasuryEntryHandler : ICommandHandler<CreateTreasuryEntryCommand, Guid>
{
    private readonly ITreasuryEntryRepository _entries; private readonly ITreasuryRepository _treasuries;
    private readonly ITreasuryReasonRepository _reasons; private readonly ICurrentUser _user;
    public CreateTreasuryEntryHandler(ITreasuryEntryRepository entries, ITreasuryRepository treasuries, ITreasuryReasonRepository reasons, ICurrentUser user)
    { _entries = entries; _treasuries = treasuries; _reasons = reasons; _user = user; }

    public async Task<Guid> Handle(CreateTreasuryEntryCommand r, CancellationToken ct)
    {
        var treasury = await _treasuries.GetByIdAsync(new TreasuryId(r.TreasuryId), ct) ?? throw new NotFoundException("Treasury", r.TreasuryId);
        TreasuryScope.EnsureInScope(_user, treasury);
        if (!treasury.IsActive) throw new ConflictException("The treasury is inactive.");
        var reason = await _reasons.GetByIdAsync(new TreasuryReasonId(r.ReasonId), ct) ?? throw new NotFoundException("TreasuryReason", r.ReasonId);
        if (!reason.IsActive) throw new ConflictException("The reason is inactive.");

        var entry = TreasuryEntry.Create(treasury.Id, r.Date, r.Debit, r.Credit, reason.Id, r.Notes);
        _entries.Add(entry);
        return entry.Id.Value;
    }
}

public sealed record UpdateTreasuryEntryCommand(Guid Id, DateOnly Date, decimal Debit, decimal Credit, Guid ReasonId, string? Notes) : ICommand, IAuthorizedRequest
{ public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ManageAccounting }; }
public sealed class UpdateTreasuryEntryValidator : AbstractValidator<UpdateTreasuryEntryCommand>
{
    public UpdateTreasuryEntryValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.ReasonId).NotEmpty();
        RuleFor(x => x.Debit).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Credit).GreaterThanOrEqualTo(0);
        RuleFor(x => x).Must(x => (x.Debit > 0) != (x.Credit > 0)).WithMessage("Enter either a debit (cash in) or a credit (expense out), not both and not neither.");
        RuleFor(x => x.Notes).MaximumLength(500);
    }
}
public sealed class UpdateTreasuryEntryHandler : ICommandHandler<UpdateTreasuryEntryCommand>
{
    private readonly ITreasuryEntryRepository _entries; private readonly ITreasuryRepository _treasuries;
    private readonly ITreasuryReasonRepository _reasons; private readonly ICurrentUser _user;
    public UpdateTreasuryEntryHandler(ITreasuryEntryRepository entries, ITreasuryRepository treasuries, ITreasuryReasonRepository reasons, ICurrentUser user)
    { _entries = entries; _treasuries = treasuries; _reasons = reasons; _user = user; }

    public async Task<Unit> Handle(UpdateTreasuryEntryCommand r, CancellationToken ct)
    {
        var entry = await _entries.GetByIdAsync(new TreasuryEntryId(r.Id), ct) ?? throw new NotFoundException("TreasuryEntry", r.Id);
        var treasury = await _treasuries.GetByIdAsync(entry.TreasuryId, ct) ?? throw new NotFoundException("Treasury", entry.TreasuryId.Value);
        TreasuryScope.EnsureInScope(_user, treasury);
        var reason = await _reasons.GetByIdAsync(new TreasuryReasonId(r.ReasonId), ct) ?? throw new NotFoundException("TreasuryReason", r.ReasonId);
        entry.Update(r.Date, r.Debit, r.Credit, reason.Id, r.Notes);
        return Unit.Value;
    }
}

public sealed record DeleteTreasuryEntryCommand(Guid Id) : ICommand, IAuthorizedRequest
{ public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ManageAccounting }; }
public sealed class DeleteTreasuryEntryValidator : AbstractValidator<DeleteTreasuryEntryCommand>
{ public DeleteTreasuryEntryValidator() => RuleFor(x => x.Id).NotEmpty(); }
public sealed class DeleteTreasuryEntryHandler : ICommandHandler<DeleteTreasuryEntryCommand>
{
    private readonly ITreasuryEntryRepository _entries; private readonly ITreasuryRepository _treasuries; private readonly ICurrentUser _user;
    public DeleteTreasuryEntryHandler(ITreasuryEntryRepository entries, ITreasuryRepository treasuries, ICurrentUser user)
    { _entries = entries; _treasuries = treasuries; _user = user; }
    public async Task<Unit> Handle(DeleteTreasuryEntryCommand r, CancellationToken ct)
    {
        var entry = await _entries.GetByIdAsync(new TreasuryEntryId(r.Id), ct) ?? throw new NotFoundException("TreasuryEntry", r.Id);
        var treasury = await _treasuries.GetByIdAsync(entry.TreasuryId, ct);
        if (treasury is not null) TreasuryScope.EnsureInScope(_user, treasury);
        _entries.Remove(entry);
        return Unit.Value;
    }
}

// ---- Commands: penalty statement ----

public sealed record CreatePenaltyCommand(DateOnly Date, Guid LaboratoryId, string AccNo, string PatientName,
    string WrongTestCode, string WrongTestName, decimal WrongValue, string RightTestCode, string RightTestName, decimal RightValue,
    string UserType, Guid? PerformedByUserId, Guid? PerformedByRepId)
    : ICommand<Guid>, IAuthorizedRequest
{ public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ManageAccounting }; }
public sealed class CreatePenaltyValidator : AbstractValidator<CreatePenaltyCommand>
{
    public CreatePenaltyValidator()
    {
        RuleFor(x => x.LaboratoryId).NotEmpty();
        RuleFor(x => x.AccNo).NotEmpty().MaximumLength(50);
        RuleFor(x => x.PatientName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.WrongTestCode).NotEmpty().MaximumLength(32);
        RuleFor(x => x.WrongTestName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.RightTestCode).NotEmpty().MaximumLength(32);
        RuleFor(x => x.RightTestName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.WrongValue).GreaterThanOrEqualTo(0);
        RuleFor(x => x.RightValue).GreaterThanOrEqualTo(0);
        PenaltyActorRules.Apply(this, x => x.UserType, x => x.PerformedByUserId, x => x.PerformedByRepId);
    }
}
public sealed class CreatePenaltyHandler : ICommandHandler<CreatePenaltyCommand, Guid>
{
    private readonly IPenaltyRecordRepository _repo; private readonly ILaboratoryRepository _labs;
    private readonly IRepresentativeRepository _reps; private readonly IAppUserRepository _users;
    private readonly IDeductionRepository _deductions; private readonly IAreaRepository _areas; private readonly ICurrentUser _user;
    public CreatePenaltyHandler(IPenaltyRecordRepository repo, ILaboratoryRepository labs, IRepresentativeRepository reps, IAppUserRepository users,
        IDeductionRepository deductions, IAreaRepository areas, ICurrentUser user)
    { _repo = repo; _labs = labs; _reps = reps; _users = users; _deductions = deductions; _areas = areas; _user = user; }
    public async Task<Guid> Handle(CreatePenaltyCommand r, CancellationToken ct)
    {
        var lab = await _labs.GetByIdAsync(new LaboratoryId(r.LaboratoryId), ct) ?? throw new NotFoundException("Laboratory", r.LaboratoryId);
        _user.EnsureInScope(lab);
        var (userId, repId) = await PenaltyActorSupport.ResolveAsync(r.PerformedByUserId, r.PerformedByRepId, _reps, _users, _user, ct);
        var p = PenaltyRecord.Create(lab.Id, r.Date, r.AccNo, r.PatientName, r.WrongTestCode, r.WrongTestName, r.WrongValue,
            r.RightTestCode, r.RightTestName, r.RightValue, EnumParse.Name<PenaltyUser>(r.UserType, nameof(r.UserType)), userId, repId);
        _repo.Add(p);
        await PenaltyDeductionSync.UpsertAsync(p, lab, _areas, _deductions, ct); // same transaction (TransactionBehavior)
        return p.Id.Value;
    }
}

public sealed record UpdatePenaltyCommand(Guid Id, DateOnly Date, string AccNo, string PatientName,
    string WrongTestCode, string WrongTestName, decimal WrongValue, string RightTestCode, string RightTestName, decimal RightValue,
    string UserType, Guid? PerformedByUserId, Guid? PerformedByRepId)
    : ICommand, IAuthorizedRequest
{ public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ManageAccounting }; }
public sealed class UpdatePenaltyValidator : AbstractValidator<UpdatePenaltyCommand>
{
    public UpdatePenaltyValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.AccNo).NotEmpty().MaximumLength(50);
        RuleFor(x => x.PatientName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.WrongTestCode).NotEmpty().MaximumLength(32);
        RuleFor(x => x.WrongTestName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.RightTestCode).NotEmpty().MaximumLength(32);
        RuleFor(x => x.RightTestName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.WrongValue).GreaterThanOrEqualTo(0);
        RuleFor(x => x.RightValue).GreaterThanOrEqualTo(0);
        PenaltyActorRules.Apply(this, x => x.UserType, x => x.PerformedByUserId, x => x.PerformedByRepId);
    }
}
public sealed class UpdatePenaltyHandler : ICommandHandler<UpdatePenaltyCommand>
{
    private readonly IPenaltyRecordRepository _repo; private readonly ILaboratoryRepository _labs;
    private readonly IRepresentativeRepository _reps; private readonly IAppUserRepository _users;
    private readonly IDeductionRepository _deductions; private readonly IAreaRepository _areas; private readonly ICurrentUser _user;
    public UpdatePenaltyHandler(IPenaltyRecordRepository repo, ILaboratoryRepository labs, IRepresentativeRepository reps, IAppUserRepository users,
        IDeductionRepository deductions, IAreaRepository areas, ICurrentUser user)
    { _repo = repo; _labs = labs; _reps = reps; _users = users; _deductions = deductions; _areas = areas; _user = user; }
    public async Task<Unit> Handle(UpdatePenaltyCommand r, CancellationToken ct)
    {
        var p = await _repo.GetByIdAsync(new PenaltyRecordId(r.Id), ct) ?? throw new NotFoundException("PenaltyRecord", r.Id);
        var lab = await _labs.GetByIdAsync(p.LaboratoryId, ct) ?? throw new NotFoundException("Laboratory", p.LaboratoryId.Value);
        _user.EnsureInScope(lab);
        var (userId, repId) = await PenaltyActorSupport.ResolveAsync(r.PerformedByUserId, r.PerformedByRepId, _reps, _users, _user, ct);
        p.Update(r.Date, r.AccNo, r.PatientName, r.WrongTestCode, r.WrongTestName, r.WrongValue,
            r.RightTestCode, r.RightTestName, r.RightValue, EnumParse.Name<PenaltyUser>(r.UserType, nameof(r.UserType)), userId, repId);
        await PenaltyDeductionSync.UpsertAsync(p, lab, _areas, _deductions, ct);
        return Unit.Value;
    }
}

/// <summary>
/// Keeps the AutoPenalty deduction of a penalty in step with the penalty (operator decision: penalties flow into the
/// Deductions report automatically, with their details noted for investigation). The deduction lands on the area the
/// lab carries by name; a lab with no (resolvable) area gets no deduction — the daily automation links it later if the
/// lab is placed. Runs inside the penalty command's transaction.
/// </summary>
public static class PenaltyDeductionSync
{
    /// <summary>Creates or refreshes the mirroring deduction. Returns false when the lab has no resolvable area.</summary>
    public static async Task<bool> UpsertAsync(PenaltyRecord penalty, Laboratory lab, IAreaRepository areas, IDeductionRepository deductions, CancellationToken ct)
    {
        var existing = await deductions.GetByPenaltyAsync(penalty.Id, ct);
        var area = string.IsNullOrWhiteSpace(lab.Area) ? null : await areas.GetByNameAsync(lab.Area, ct);
        if (area is null)
        {
            // The lab lost its area (or never had one): the mirrored row can no longer be attributed — drop it.
            if (existing is not null) deductions.Remove(existing);
            return false;
        }
        if (existing is null) deductions.Add(Deduction.FromPenalty(area.Id, penalty, lab.Name));
        else existing.RefreshFromPenalty(penalty, lab.Name);
        return true;
    }

    public static async Task RemoveAsync(PenaltyRecord penalty, IDeductionRepository deductions, CancellationToken ct)
    {
        var existing = await deductions.GetByPenaltyAsync(penalty.Id, ct);
        if (existing is not null) deductions.Remove(existing);
    }
}

/// <summary>Shape rules for a penalty's "performed by" person, shared by the create and update validators: a valid user
/// type, and exactly the matching id — a representative for Rep, a system user for DataEntry / Technician.</summary>
internal static class PenaltyActorRules
{
    public static void Apply<T>(AbstractValidator<T> v, System.Linq.Expressions.Expression<Func<T, string>> userType,
        System.Linq.Expressions.Expression<Func<T, Guid?>> userId, System.Linq.Expressions.Expression<Func<T, Guid?>> repId)
    {
        var typeOf = userType.Compile();
        v.RuleFor(userType).Must(EnumParse.IsValid<PenaltyUser>).WithMessage("User type must be Rep, DataEntry or Technician.");
        v.When(x => string.Equals(typeOf(x), nameof(PenaltyUser.Rep), StringComparison.OrdinalIgnoreCase), () =>
        {
            v.RuleFor(repId).NotEmpty().WithMessage("Select the representative who made the error.");
            v.RuleFor(userId).Null().WithMessage("A representative penalty cannot also name a system user.");
        }).Otherwise(() =>
        {
            v.RuleFor(userId).NotEmpty().WithMessage("Select the system user who made the error.");
            v.RuleFor(repId).Null().WithMessage("A data-entry / technician penalty cannot also name a representative.");
        });
    }
}

/// <summary>Resolves the "performed by" person: the representative must exist and lie within the caller's org-scope
/// (record-scope rule); the system user must exist and be active. Returns the typed ids for the domain factory.</summary>
internal static class PenaltyActorSupport
{
    public static async Task<(AppUserId? UserId, RepresentativeId? RepId)> ResolveAsync(Guid? performedByUserId, Guid? performedByRepId,
        IRepresentativeRepository reps, IAppUserRepository users, ICurrentUser caller, CancellationToken ct)
    {
        RepresentativeId? repId = null; AppUserId? userId = null;
        if (performedByRepId is { } rid)
        {
            var rep = await reps.GetByIdAsync(new RepresentativeId(rid), ct) ?? throw new NotFoundException("Representative", rid);
            caller.EnsureInScope(rep);
            repId = rep.Id;
        }
        if (performedByUserId is { } uid)
        {
            var user = await users.GetByIdAsync(new AppUserId(uid), ct) ?? throw new NotFoundException("User", uid);
            if (!user.IsActive)
                throw new Common.Exceptions.ValidationException(new Dictionary<string, string[]> { ["performedByUserId"] = new[] { "The selected user is inactive." } });
            userId = user.Id;
        }
        return (userId, repId);
    }
}

public sealed record DeletePenaltyCommand(Guid Id) : ICommand, IAuthorizedRequest
{ public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ManageAccounting }; }
public sealed class DeletePenaltyValidator : AbstractValidator<DeletePenaltyCommand> { public DeletePenaltyValidator() => RuleFor(x => x.Id).NotEmpty(); }
public sealed class DeletePenaltyHandler : ICommandHandler<DeletePenaltyCommand>
{
    private readonly IPenaltyRecordRepository _repo; private readonly ILaboratoryRepository _labs;
    private readonly IDeductionRepository _deductions; private readonly ICurrentUser _user;
    public DeletePenaltyHandler(IPenaltyRecordRepository repo, ILaboratoryRepository labs, IDeductionRepository deductions, ICurrentUser user)
    { _repo = repo; _labs = labs; _deductions = deductions; _user = user; }
    public async Task<Unit> Handle(DeletePenaltyCommand r, CancellationToken ct)
    {
        var p = await _repo.GetByIdAsync(new PenaltyRecordId(r.Id), ct) ?? throw new NotFoundException("PenaltyRecord", r.Id);
        var lab = await _labs.GetByIdAsync(p.LaboratoryId, ct);
        if (lab is not null) _user.EnsureInScope(lab);
        await PenaltyDeductionSync.RemoveAsync(p, _deductions, ct); // the mirrored deduction goes with its penalty
        _repo.Remove(p);
        return Unit.Value;
    }
}

// ---- Commands: deductions ----

public sealed record CreateDeductionCommand(DateOnly Date, Guid AreaId, string Reason, decimal Value, string? Notes, DateOnly? PeriodFrom, DateOnly? PeriodTo)
    : ICommand<Guid>, IAuthorizedRequest
{ public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ManageAccounting }; }
public sealed class CreateDeductionValidator : AbstractValidator<CreateDeductionCommand>
{
    public CreateDeductionValidator()
    {
        RuleFor(x => x.AreaId).NotEmpty();
        RuleFor(x => x.Reason).Must(EnumParse.IsValid<DeductionReason>).WithMessage("Reason must be Transportation, Penalty or PercentageDeal.");
        RuleFor(x => x.Value).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Notes).MaximumLength(500);
        RuleFor(x => x.PeriodTo).GreaterThanOrEqualTo(x => x.PeriodFrom!.Value).When(x => x.PeriodFrom is not null && x.PeriodTo is not null);
        RuleFor(x => x).Must(x => (x.PeriodFrom is null) == (x.PeriodTo is null)).WithMessage("A period needs both a start and an end.");
    }
}
public sealed class CreateDeductionHandler : ICommandHandler<CreateDeductionCommand, Guid>
{
    private readonly IDeductionRepository _repo; private readonly IAreaRepository _areas; private readonly ICurrentUser _user;
    public CreateDeductionHandler(IDeductionRepository repo, IAreaRepository areas, ICurrentUser user) { _repo = repo; _areas = areas; _user = user; }
    public async Task<Guid> Handle(CreateDeductionCommand r, CancellationToken ct)
    {
        var area = await _areas.GetByIdAsync(new AreaId(r.AreaId), ct) ?? throw new NotFoundException("Area", r.AreaId);
        _user.EnsureAreaInScope(area.Name);
        var d = Deduction.Create(area.Id, r.Date, EnumParse.Name<DeductionReason>(r.Reason, nameof(r.Reason)), r.Value, r.Notes, r.PeriodFrom, r.PeriodTo);
        _repo.Add(d);
        return d.Id.Value;
    }
}

/// <summary>Edits a deduction. For a manual row every field applies. For an automated row (AutoPenalty / AutoDeal) only
/// <c>Value</c>, <c>Notes</c> and the optional <c>Basis</c> apply — the area, reason and period are fixed — and a changed
/// value marks the row manually adjusted. <c>Basis</c> is what "Suggest value" computed, when the operator used it.</summary>
public sealed record UpdateDeductionCommand(Guid Id, DateOnly Date, string Reason, decimal Value, string? Notes, DateOnly? PeriodFrom, DateOnly? PeriodTo,
    string? Basis = null)
    : ICommand, IAuthorizedRequest
{ public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ManageAccounting }; }
public sealed class UpdateDeductionValidator : AbstractValidator<UpdateDeductionCommand>
{
    public UpdateDeductionValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Reason).Must(EnumParse.IsValid<DeductionReason>).WithMessage("Reason must be Transportation, Penalty or PercentageDeal.");
        RuleFor(x => x.Value).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Notes).MaximumLength(500);
        RuleFor(x => x.PeriodTo).GreaterThanOrEqualTo(x => x.PeriodFrom!.Value).When(x => x.PeriodFrom is not null && x.PeriodTo is not null);
        RuleFor(x => x).Must(x => (x.PeriodFrom is null) == (x.PeriodTo is null)).WithMessage("A period needs both a start and an end.");
        RuleFor(x => x.Basis).MaximumLength(1000);
    }
}
public sealed class UpdateDeductionHandler : ICommandHandler<UpdateDeductionCommand>
{
    private readonly IDeductionRepository _repo; private readonly IAreaRepository _areas; private readonly ICurrentUser _user;
    public UpdateDeductionHandler(IDeductionRepository repo, IAreaRepository areas, ICurrentUser user) { _repo = repo; _areas = areas; _user = user; }
    public async Task<Unit> Handle(UpdateDeductionCommand r, CancellationToken ct)
    {
        var d = await _repo.GetByIdAsync(new DeductionId(r.Id), ct) ?? throw new NotFoundException("Deduction", r.Id);
        var area = await _areas.GetByIdAsync(d.AreaId, ct) ?? throw new NotFoundException("Area", d.AreaId.Value);
        _user.EnsureAreaInScope(area.Name);
        if (d.Origin == DeductionOrigin.Manual)
            d.Update(r.Date, EnumParse.Name<DeductionReason>(r.Reason, nameof(r.Reason)), r.Value, r.Notes, r.PeriodFrom, r.PeriodTo);
        else
            d.Adjust(r.Value, r.Notes, r.Basis); // automated row: value + notes only; a changed value marks it adjusted
        return Unit.Value;
    }
}

public sealed record DeleteDeductionCommand(Guid Id) : ICommand, IAuthorizedRequest
{ public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ManageAccounting }; }
public sealed class DeleteDeductionValidator : AbstractValidator<DeleteDeductionCommand> { public DeleteDeductionValidator() => RuleFor(x => x.Id).NotEmpty(); }
public sealed class DeleteDeductionHandler : ICommandHandler<DeleteDeductionCommand>
{
    private readonly IDeductionRepository _repo; private readonly IAreaRepository _areas; private readonly ICurrentUser _user;
    public DeleteDeductionHandler(IDeductionRepository repo, IAreaRepository areas, ICurrentUser user) { _repo = repo; _areas = areas; _user = user; }
    public async Task<Unit> Handle(DeleteDeductionCommand r, CancellationToken ct)
    {
        var d = await _repo.GetByIdAsync(new DeductionId(r.Id), ct) ?? throw new NotFoundException("Deduction", r.Id);
        var area = await _areas.GetByIdAsync(d.AreaId, ct);
        if (area is not null) _user.EnsureAreaInScope(area.Name);
        if (d.Origin == DeductionOrigin.AutoPenalty)
            throw new ConflictException("This deduction mirrors a penalty record; delete the penalty on the Penalty Statement instead.");
        _repo.Remove(d);
        return Unit.Value;
    }
}

/// <summary>Runs the deductions automation now (the daily job's work) for the month of the latest synced day: links any
/// penalty that lacks its mirroring deduction and recalculates the month's Percentage Deal deductions.</summary>
public sealed record RecalculateDeductionsCommand : ICommand<DeductionAutomationResult>, IAuthorizedRequest
{ public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ManageAccounting }; }
public sealed class RecalculateDeductionsValidator : AbstractValidator<RecalculateDeductionsCommand> { }
public sealed class RecalculateDeductionsHandler : ICommandHandler<RecalculateDeductionsCommand, DeductionAutomationResult>
{
    private readonly IDeductionAutomationRunner _runner; private readonly IClock _clock;
    public RecalculateDeductionsHandler(IDeductionAutomationRunner runner, IClock clock) { _runner = runner; _clock = clock; }
    public Task<DeductionAutomationResult> Handle(RecalculateDeductionsCommand r, CancellationToken ct) =>
        // Income is synced nightly for the previous day, so "through yesterday" is the latest complete figure.
        _runner.RunAsync(_clock.CairoToday.AddDays(-1), manual: true, ct);
}

// ---- Commands: collections ----

public sealed record CreateCollectionCommand(DateOnly Date, Guid LaboratoryId, string Type, IReadOnlyList<Guid> RepIds,
    decimal Cash, decimal Bank, string? Iban, string? DoneBy, string? Notes) : ICommand<Guid>, IAuthorizedRequest
{ public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ManageAccounting }; }
public sealed class CreateCollectionValidator : AbstractValidator<CreateCollectionCommand>
{
    public CreateCollectionValidator()
    {
        RuleFor(x => x.LaboratoryId).NotEmpty();
        RuleFor(x => x.Type).Must(EnumParse.IsValid<CollectionType>).WithMessage("Type must be Single or Group.");
        RuleFor(x => x.RepIds).NotNull().Must(r => r.Count > 0).WithMessage("Select at least one rep.");
        RuleFor(x => x.RepIds).Must(r => r.Count == 1).When(x => x.Type == nameof(CollectionType.Single)).WithMessage("A single collection names exactly one rep.");
        RuleFor(x => x.RepIds).Must(r => r.Distinct().Count() >= 2).When(x => x.Type == nameof(CollectionType.Group)).WithMessage("A group collection names at least two reps.");
        RuleFor(x => x.Cash).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Bank).GreaterThanOrEqualTo(0);
        RuleFor(x => x).Must(x => x.Cash + x.Bank > 0).WithMessage("Enter a cash or bank amount.");
        RuleFor(x => x.Iban).Must(EnumParse.IsValid<IbanOption>).When(x => x.Bank > 0).WithMessage("Select the IBAN (12, 16 or 18) for a bank amount.");
        RuleFor(x => x.DoneBy).MaximumLength(200);
        RuleFor(x => x.Notes).MaximumLength(500);
    }
}
public sealed class CreateCollectionHandler : ICommandHandler<CreateCollectionCommand, Guid>
{
    private readonly ICollectionRepository _repo; private readonly ILaboratoryRepository _labs;
    private readonly IRepresentativeRepository _reps; private readonly ICurrentUser _user;
    public CreateCollectionHandler(ICollectionRepository repo, ILaboratoryRepository labs, IRepresentativeRepository reps, ICurrentUser user)
    { _repo = repo; _labs = labs; _reps = reps; _user = user; }

    public async Task<Guid> Handle(CreateCollectionCommand r, CancellationToken ct)
    {
        var lab = await _labs.GetByIdAsync(new LaboratoryId(r.LaboratoryId), ct) ?? throw new NotFoundException("Laboratory", r.LaboratoryId);
        _user.EnsureInScope(lab);
        var repIds = await CollectionSupport.ResolveRepsAsync(r.RepIds, _reps, _user, ct);
        var c = Collection.Create(lab.Id, r.Date, EnumParse.Name<CollectionType>(r.Type, nameof(r.Type)), repIds, r.Cash, r.Bank,
            r.Bank > 0 ? EnumParse.Name<IbanOption>(r.Iban!, nameof(r.Iban)) : null, r.DoneBy, r.Notes);
        _repo.Add(c);
        return c.Id.Value;
    }
}

public sealed record UpdateCollectionCommand(Guid Id, DateOnly Date, string Type, IReadOnlyList<Guid> RepIds,
    decimal Cash, decimal Bank, string? Iban, string? DoneBy, string? Notes) : ICommand, IAuthorizedRequest
{ public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ManageAccounting }; }
public sealed class UpdateCollectionValidator : AbstractValidator<UpdateCollectionCommand>
{
    public UpdateCollectionValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Type).Must(EnumParse.IsValid<CollectionType>).WithMessage("Type must be Single or Group.");
        RuleFor(x => x.RepIds).NotNull().Must(r => r.Count > 0).WithMessage("Select at least one rep.");
        RuleFor(x => x.RepIds).Must(r => r.Count == 1).When(x => x.Type == nameof(CollectionType.Single)).WithMessage("A single collection names exactly one rep.");
        RuleFor(x => x.RepIds).Must(r => r.Distinct().Count() >= 2).When(x => x.Type == nameof(CollectionType.Group)).WithMessage("A group collection names at least two reps.");
        RuleFor(x => x.Cash).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Bank).GreaterThanOrEqualTo(0);
        RuleFor(x => x).Must(x => x.Cash + x.Bank > 0).WithMessage("Enter a cash or bank amount.");
        RuleFor(x => x.Iban).Must(EnumParse.IsValid<IbanOption>).When(x => x.Bank > 0).WithMessage("Select the IBAN (12, 16 or 18) for a bank amount.");
        RuleFor(x => x.DoneBy).MaximumLength(200);
        RuleFor(x => x.Notes).MaximumLength(500);
    }
}
public sealed class UpdateCollectionHandler : ICommandHandler<UpdateCollectionCommand>
{
    private readonly ICollectionRepository _repo; private readonly ILaboratoryRepository _labs;
    private readonly IRepresentativeRepository _reps; private readonly ICurrentUser _user;
    public UpdateCollectionHandler(ICollectionRepository repo, ILaboratoryRepository labs, IRepresentativeRepository reps, ICurrentUser user)
    { _repo = repo; _labs = labs; _reps = reps; _user = user; }

    public async Task<Unit> Handle(UpdateCollectionCommand r, CancellationToken ct)
    {
        var c = await _repo.GetByIdAsync(new CollectionId(r.Id), ct) ?? throw new NotFoundException("Collection", r.Id);
        var lab = await _labs.GetByIdAsync(c.LaboratoryId, ct) ?? throw new NotFoundException("Laboratory", c.LaboratoryId.Value);
        _user.EnsureInScope(lab);
        var repIds = await CollectionSupport.ResolveRepsAsync(r.RepIds, _reps, _user, ct);
        c.Update(r.Date, EnumParse.Name<CollectionType>(r.Type, nameof(r.Type)), repIds, r.Cash, r.Bank,
            r.Bank > 0 ? EnumParse.Name<IbanOption>(r.Iban!, nameof(r.Iban)) : null, r.DoneBy, r.Notes);
        return Unit.Value;
    }
}

public sealed record DeleteCollectionCommand(Guid Id) : ICommand, IAuthorizedRequest
{ public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ManageAccounting }; }
public sealed class DeleteCollectionValidator : AbstractValidator<DeleteCollectionCommand> { public DeleteCollectionValidator() => RuleFor(x => x.Id).NotEmpty(); }
public sealed class DeleteCollectionHandler : ICommandHandler<DeleteCollectionCommand>
{
    private readonly ICollectionRepository _repo; private readonly ILaboratoryRepository _labs; private readonly ICurrentUser _user;
    public DeleteCollectionHandler(ICollectionRepository repo, ILaboratoryRepository labs, ICurrentUser user) { _repo = repo; _labs = labs; _user = user; }
    public async Task<Unit> Handle(DeleteCollectionCommand r, CancellationToken ct)
    {
        var c = await _repo.GetByIdAsync(new CollectionId(r.Id), ct) ?? throw new NotFoundException("Collection", r.Id);
        var lab = await _labs.GetByIdAsync(c.LaboratoryId, ct);
        if (lab is not null) _user.EnsureInScope(lab);
        _repo.Remove(c);
        return Unit.Value;
    }
}

internal static class CollectionSupport
{
    /// <summary>Every rep on a collection must exist and be within the caller's scope (fail-closed, like the lab).</summary>
    public static async Task<List<RepresentativeId>> ResolveRepsAsync(IReadOnlyList<Guid> ids, IRepresentativeRepository reps, ICurrentUser user, CancellationToken ct)
    {
        var result = new List<RepresentativeId>();
        foreach (var id in ids.Distinct())
        {
            var rep = await reps.GetByIdAsync(new RepresentativeId(id), ct) ?? throw new NotFoundException("Representative", id);
            user.EnsureInScope(rep);
            result.Add(rep.Id);
        }
        return result;
    }
}

// ---- Commands: rep statement manual income ----

public sealed record CreateRepIncomeEntryCommand(DateOnly Date, Guid RepresentativeId, decimal Amount, string? Notes) : ICommand<Guid>, IAuthorizedRequest
{ public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ManageAccounting }; }
public sealed class CreateRepIncomeEntryValidator : AbstractValidator<CreateRepIncomeEntryCommand>
{
    public CreateRepIncomeEntryValidator()
    {
        RuleFor(x => x.RepresentativeId).NotEmpty();
        RuleFor(x => x.Amount).GreaterThan(0);
        RuleFor(x => x.Notes).MaximumLength(500);
    }
}
public sealed class CreateRepIncomeEntryHandler : ICommandHandler<CreateRepIncomeEntryCommand, Guid>
{
    private readonly IRepIncomeEntryRepository _repo; private readonly IRepresentativeRepository _reps; private readonly ICurrentUser _user;
    public CreateRepIncomeEntryHandler(IRepIncomeEntryRepository repo, IRepresentativeRepository reps, ICurrentUser user) { _repo = repo; _reps = reps; _user = user; }
    public async Task<Guid> Handle(CreateRepIncomeEntryCommand r, CancellationToken ct)
    {
        var rep = await _reps.GetByIdAsync(new RepresentativeId(r.RepresentativeId), ct) ?? throw new NotFoundException("Representative", r.RepresentativeId);
        _user.EnsureInScope(rep);
        var e = RepIncomeEntry.Create(rep.Id, r.Date, r.Amount, r.Notes);
        _repo.Add(e);
        return e.Id.Value;
    }
}

public sealed record DeleteRepIncomeEntryCommand(Guid Id) : ICommand, IAuthorizedRequest
{ public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ManageAccounting }; }
public sealed class DeleteRepIncomeEntryValidator : AbstractValidator<DeleteRepIncomeEntryCommand> { public DeleteRepIncomeEntryValidator() => RuleFor(x => x.Id).NotEmpty(); }
public sealed class DeleteRepIncomeEntryHandler : ICommandHandler<DeleteRepIncomeEntryCommand>
{
    private readonly IRepIncomeEntryRepository _repo; private readonly IRepresentativeRepository _reps; private readonly ICurrentUser _user;
    public DeleteRepIncomeEntryHandler(IRepIncomeEntryRepository repo, IRepresentativeRepository reps, ICurrentUser user) { _repo = repo; _reps = reps; _user = user; }
    public async Task<Unit> Handle(DeleteRepIncomeEntryCommand r, CancellationToken ct)
    {
        var e = await _repo.GetByIdAsync(new RepIncomeEntryId(r.Id), ct) ?? throw new NotFoundException("RepIncomeEntry", r.Id);
        var rep = await _reps.GetByIdAsync(e.RepresentativeId, ct);
        if (rep is not null) _user.EnsureInScope(rep);
        _repo.Remove(e);
        return Unit.Value;
    }
}
