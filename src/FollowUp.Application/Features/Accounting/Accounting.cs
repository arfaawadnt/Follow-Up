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
/// <summary>A treasury as the caller sees it: only treasuries the caller may View are listed, with their own rights.</summary>
public sealed record TreasuryDto(Guid Id, string Name, IReadOnlyList<string> Branches, bool IsActive,
    bool CanValidate = false, bool CanUpdate = false);
public sealed record TreasuryEntryDto(Guid Id, long Serial, DateOnly Date, Guid TreasuryId, string TreasuryName,
    decimal Debit, decimal Credit, Guid? ReasonId, string ReasonName, string? Notes,
    string Origin = nameof(TreasuryEntryOrigin.Manual), string ValidationStatus = nameof(TreasuryValidationStatus.NotRequired),
    Guid? CollectionId = null, decimal? CollectedCash = null, string? SystemNote = null,
    DateTimeOffset? ValidatedAt = null, string? ValidatedBy = null, string? ValidationNote = null);

/// <summary>A role's rights on one treasury, for the Roles page (every treasury is listed, granted or not).</summary>
public sealed record TreasuryGrantDto(Guid TreasuryId, string TreasuryName, bool IsActive, bool CanView, bool CanValidate, bool CanUpdate);
public sealed record TreasuryGrantInput(Guid TreasuryId, bool CanView, bool CanValidate, bool CanUpdate);

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

/// <summary>One rep's part of a collection (the amount that rep handed in). For a Single collection it is the total.</summary>
public sealed record CollectionShareDto(Guid RepId, string RepName, decimal Amount);
/// <summary>A collection belongs to its reps (a Lab Responsible collects from many labs), so there is no lab on it.</summary>
public sealed record CollectionDto(Guid Id, long Serial, DateOnly Date, string Type, IReadOnlyList<CollectionShareDto> Shares,
    IReadOnlyList<Guid> RepIds, IReadOnlyList<string> RepNames, decimal Cash, decimal Bank, decimal Total,
    string? Iban, string? DoneBy, string? Notes);
/// <summary>Write-side share: the rep and the amount they handed in.</summary>
public sealed record CollectionShareInput(Guid RepId, decimal Amount);

/// <summary>One statement line. Kind: OracleIncome (derived from the rep's labs' synced income), ManualIncome (a
/// RepIncomeEntry, deletable via SourceId — legacy, superseded by the sheet), RealIncome (Σ Paid + DelayedPayment of the
/// rep's real-income sheet for the date), or Collection (Credit). Balance is the running Debit − Credit.</summary>
public sealed record RepStatementRowDto(DateOnly Date, string Kind, decimal Debit, decimal Credit, string? Notes, decimal Balance, Guid? SourceId);
/// <summary>A Lab Responsible linked to the area (responsible for at least one of its labs), for the sheet's rep picker.</summary>
public sealed record RealIncomeRepDto(Guid Id, string FullName, int LabCount);
/// <summary>A lab of the area, for adding a row the visits did not produce.</summary>
public sealed record RealIncomeLabDto(Guid Id, string DisplayCode, string Name);
/// <summary>One sheet row: view-only context (visit, LDM income, penalty, remaining carried from earlier days) + the rep's entry.</summary>
public sealed record RealIncomeRowDto(Guid LaboratoryId, string LabDisplayCode, string LabName, bool HasVisit, int? VisitTotalRequired, int? VisitSamples,
    decimal LdmIncome, decimal Penalty, decimal PreviousRemaining,
    Guid? EntryId, int Samples, decimal TotalRequired, decimal Paid, decimal Remaining, decimal DelayedPayment, string? Notes);
public sealed record RealIncomeSheetDto(DateOnly Date, Guid AreaId, string AreaName, Guid RepresentativeId, string RepName, IReadOnlyList<RealIncomeRowDto> Rows);
public sealed record RealIncomeRowInput(Guid LaboratoryId, int Samples, decimal TotalRequired, decimal Paid, decimal DelayedPayment, string? Notes);

/// <summary>The dimension a statement is drawn for: one Lab Responsible (the classic rep statement), one Area (all its
/// labs) or one Lab. Collections and legacy manual lines only exist on the Responsible dimension.</summary>
public static class StatementBy
{
    public const string Responsible = "Responsible";
    public const string Area = "Area";
    public const string Lab = "Lab";
    public static readonly string[] All = { Responsible, Area, Lab };
}
public sealed record StatementDto(string By, Guid SubjectId, string SubjectName, IReadOnlyList<RepStatementRowDto> Rows,
    decimal TotalDebit, decimal TotalCredit, decimal Balance);

public sealed record RepStatementDto(Guid RepresentativeId, string RepName, IReadOnlyList<RepStatementRowDto> Rows,
    decimal TotalDebit, decimal TotalCredit, decimal Balance);

// ---- Read side: query interface (implemented in Infrastructure, org scope pushed into SQL) ----

public interface IAccountingQueries
{
    Task<IReadOnlyList<TreasuryReasonDto>> TreasuryReasonsAsync(CancellationToken ct);
    Task<IReadOnlyList<TreasuryDto>> TreasuriesAsync(OrgScope scope, TreasuryAccessMap access, CancellationToken ct);
    Task<IReadOnlyList<TreasuryEntryDto>> TreasuryEntriesAsync(DateOnly from, DateOnly to, Guid? treasuryId, OrgScope scope, TreasuryAccessMap access, CancellationToken ct);
    /// <summary>Every treasury with the given role's rights on it (for the Roles page).</summary>
    Task<IReadOnlyList<TreasuryGrantDto>> TreasuryGrantsAsync(RoleId roleId, CancellationToken ct);
    Task<IReadOnlyList<PenaltyDto>> PenaltiesAsync(DateOnly from, DateOnly to, Guid? laboratoryId, OrgScope scope, bool canSeeEncrypted, CancellationToken ct);
    Task<IReadOnlyList<PenaltyActorDto>> PenaltyActorsAsync(PenaltyUser userType, OrgScope scope, CancellationToken ct);
    Task<IReadOnlyList<DeductionDto>> DeductionsAsync(DateOnly from, DateOnly to, Guid? areaId, OrgScope scope, CancellationToken ct);
    Task<DeductionSuggestionDto> SuggestDeductionAsync(Guid areaId, DeductionReason reason, DateOnly from, DateOnly to, OrgScope scope, CancellationToken ct);
    /// <summary>Collections whose reps are all visible in the caller's rep scope (a collection is the reps' act, not a lab's).</summary>
    Task<IReadOnlyList<CollectionDto>> CollectionsAsync(DateOnly from, DateOnly to, Guid? repId, OrgScope scope, CancellationToken ct);
    Task<RepStatementDto?> RepStatementAsync(Guid repId, DateOnly from, DateOnly to, OrgScope scope, CancellationToken ct);
    /// <summary>Statement by <see cref="StatementBy"/> dimension; null when the subject is not visible in scope.</summary>
    Task<StatementDto?> StatementAsync(string by, Guid id, DateOnly from, DateOnly to, OrgScope scope, CancellationToken ct);
    /// <summary>Lab Responsibles responsible for at least one (in-scope) lab of the area.</summary>
    Task<IReadOnlyList<RealIncomeRepDto>> RealIncomeRepsAsync(Guid areaId, OrgScope scope, CancellationToken ct);
    /// <summary>The area's (in-scope) labs, for adding a sheet row by hand.</summary>
    Task<IReadOnlyList<RealIncomeLabDto>> RealIncomeLabsAsync(Guid areaId, OrgScope scope, bool canSeeEncrypted, CancellationToken ct);
    /// <summary>The rep's sheet for the area and date: the rep's labs with a recorded visit that day plus labs already
    /// entered; null when the area or the rep is not visible.</summary>
    Task<RealIncomeSheetDto?> RealIncomeSheetAsync(Guid areaId, DateOnly date, Guid repId, OrgScope scope, bool canSeeEncrypted, CancellationToken ct);
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
    private readonly IAccountingQueries _q; private readonly ICurrentUser _user; private readonly ITreasuryAccess _access;
    public GetTreasuriesHandler(IAccountingQueries q, ICurrentUser user, ITreasuryAccess access) { _q = q; _user = user; _access = access; }
    public async Task<IReadOnlyList<TreasuryDto>> Handle(GetTreasuriesQuery r, CancellationToken ct) =>
        await _q.TreasuriesAsync(_user.Scope, await _access.ResolveAsync(ct), ct);
}

public sealed record GetTreasuryEntriesQuery(DateOnly From, DateOnly To, Guid? TreasuryId) : IQuery<IReadOnlyList<TreasuryEntryDto>>, IAuthorizedRequest
{ public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ViewAccounting }; }
public sealed class GetTreasuryEntriesHandler : IQueryHandler<GetTreasuryEntriesQuery, IReadOnlyList<TreasuryEntryDto>>
{
    private readonly IAccountingQueries _q; private readonly ICurrentUser _user; private readonly ITreasuryAccess _access;
    public GetTreasuryEntriesHandler(IAccountingQueries q, ICurrentUser user, ITreasuryAccess access) { _q = q; _user = user; _access = access; }
    public async Task<IReadOnlyList<TreasuryEntryDto>> Handle(GetTreasuryEntriesQuery r, CancellationToken ct) =>
        await _q.TreasuryEntriesAsync(r.From, r.To, r.TreasuryId, _user.Scope, await _access.ResolveAsync(ct), ct);
}

/// <summary>Every treasury with a role's View / Validate / Update rights — the Roles page's treasury panel (ManageUsers,
/// like the rest of role editing).</summary>
public sealed record GetTreasuryGrantsQuery(Guid RoleId) : IQuery<IReadOnlyList<TreasuryGrantDto>>, IAuthorizedRequest
{ public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ManageUsers }; }
public sealed class GetTreasuryGrantsHandler : IQueryHandler<GetTreasuryGrantsQuery, IReadOnlyList<TreasuryGrantDto>>
{
    private readonly IAccountingQueries _q;
    public GetTreasuryGrantsHandler(IAccountingQueries q) => _q = q;
    public Task<IReadOnlyList<TreasuryGrantDto>> Handle(GetTreasuryGrantsQuery r, CancellationToken ct) => _q.TreasuryGrantsAsync(new RoleId(r.RoleId), ct);
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

public sealed record GetCollectionsQuery(DateOnly From, DateOnly To, Guid? RepId) : IQuery<IReadOnlyList<CollectionDto>>, IAuthorizedRequest
{ public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ViewAccounting }; }
public sealed class GetCollectionsHandler : IQueryHandler<GetCollectionsQuery, IReadOnlyList<CollectionDto>>
{
    private readonly IAccountingQueries _q; private readonly ICurrentUser _user;
    public GetCollectionsHandler(IAccountingQueries q, ICurrentUser user) { _q = q; _user = user; }
    public Task<IReadOnlyList<CollectionDto>> Handle(GetCollectionsQuery r, CancellationToken ct) =>
        _q.CollectionsAsync(r.From, r.To, r.RepId, _user.Scope, ct);
}

public sealed record GetRealIncomeRepsQuery(Guid AreaId) : IQuery<IReadOnlyList<RealIncomeRepDto>>, IAuthorizedRequest
{ public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ViewAccounting }; }
public sealed class GetRealIncomeRepsHandler : IQueryHandler<GetRealIncomeRepsQuery, IReadOnlyList<RealIncomeRepDto>>
{
    private readonly IAccountingQueries _q; private readonly ICurrentUser _user;
    public GetRealIncomeRepsHandler(IAccountingQueries q, ICurrentUser user) { _q = q; _user = user; }
    public Task<IReadOnlyList<RealIncomeRepDto>> Handle(GetRealIncomeRepsQuery r, CancellationToken ct) => _q.RealIncomeRepsAsync(r.AreaId, _user.Scope, ct);
}
public sealed record GetRealIncomeLabsQuery(Guid AreaId) : IQuery<IReadOnlyList<RealIncomeLabDto>>, IAuthorizedRequest
{ public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ViewAccounting }; }
public sealed class GetRealIncomeLabsHandler : IQueryHandler<GetRealIncomeLabsQuery, IReadOnlyList<RealIncomeLabDto>>
{
    private readonly IAccountingQueries _q; private readonly ICurrentUser _user;
    public GetRealIncomeLabsHandler(IAccountingQueries q, ICurrentUser user) { _q = q; _user = user; }
    public Task<IReadOnlyList<RealIncomeLabDto>> Handle(GetRealIncomeLabsQuery r, CancellationToken ct) =>
        _q.RealIncomeLabsAsync(r.AreaId, _user.Scope, _user.Has(Privileges.ShowEncryptedLabs), ct);
}
public sealed record GetRealIncomeSheetQuery(Guid AreaId, DateOnly Date, Guid RepresentativeId) : IQuery<RealIncomeSheetDto>, IAuthorizedRequest
{ public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ViewAccounting }; }
public sealed class GetRealIncomeSheetHandler : IQueryHandler<GetRealIncomeSheetQuery, RealIncomeSheetDto>
{
    private readonly IAccountingQueries _q; private readonly ICurrentUser _user;
    public GetRealIncomeSheetHandler(IAccountingQueries q, ICurrentUser user) { _q = q; _user = user; }
    public async Task<RealIncomeSheetDto> Handle(GetRealIncomeSheetQuery r, CancellationToken ct) =>
        await _q.RealIncomeSheetAsync(r.AreaId, r.Date, r.RepresentativeId, _user.Scope, _user.Has(Privileges.ShowEncryptedLabs), ct)
        ?? throw new NotFoundException("RealIncomeSheet", r.RepresentativeId);
}

public sealed record GetStatementQuery(string By, Guid Id, DateOnly From, DateOnly To) : IQuery<StatementDto>, IAuthorizedRequest
{ public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ViewAccounting }; }
public sealed class GetStatementHandler : IQueryHandler<GetStatementQuery, StatementDto>
{
    private readonly IAccountingQueries _q; private readonly ICurrentUser _user;
    public GetStatementHandler(IAccountingQueries q, ICurrentUser user) { _q = q; _user = user; }
    public async Task<StatementDto> Handle(GetStatementQuery r, CancellationToken ct)
    {
        if (!StatementBy.All.Contains(r.By))
            throw new Common.Exceptions.ValidationException(new Dictionary<string, string[]> { ["by"] = new[] { "View by must be Responsible, Area or Lab." } });
        return await _q.StatementAsync(r.By, r.Id, r.From, r.To, _user.Scope, ct) ?? throw new NotFoundException(r.By, r.Id);
    }
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
    private readonly ITreasuryReasonRepository _reasons; private readonly ICurrentUser _user; private readonly ITreasuryAccess _access;
    public CreateTreasuryEntryHandler(ITreasuryEntryRepository entries, ITreasuryRepository treasuries, ITreasuryReasonRepository reasons, ICurrentUser user, ITreasuryAccess access)
    { _entries = entries; _treasuries = treasuries; _reasons = reasons; _user = user; _access = access; }

    public async Task<Guid> Handle(CreateTreasuryEntryCommand r, CancellationToken ct)
    {
        var treasury = await _treasuries.GetByIdAsync(new TreasuryId(r.TreasuryId), ct) ?? throw new NotFoundException("Treasury", r.TreasuryId);
        TreasuryScope.EnsureInScope(_user, treasury);
        (await _access.ResolveAsync(ct)).EnsureUpdate(treasury.Id); // recording needs the treasury's Update right
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
        // ReasonId is required for a manual row (the handler resolves it → 404 when missing) and ignored for a
        // validated collection mirror, which has no reason; so it is not validated for emptiness here.
        RuleFor(x => x.Debit).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Credit).GreaterThanOrEqualTo(0);
        RuleFor(x => x).Must(x => (x.Debit > 0) != (x.Credit > 0)).WithMessage("Enter either a debit (cash in) or a credit (expense out), not both and not neither.");
        RuleFor(x => x.Notes).MaximumLength(500);
    }
}
public sealed class UpdateTreasuryEntryHandler : ICommandHandler<UpdateTreasuryEntryCommand>
{
    private readonly ITreasuryEntryRepository _entries; private readonly ITreasuryRepository _treasuries;
    private readonly ITreasuryReasonRepository _reasons; private readonly ICurrentUser _user; private readonly ITreasuryAccess _access;
    public UpdateTreasuryEntryHandler(ITreasuryEntryRepository entries, ITreasuryRepository treasuries, ITreasuryReasonRepository reasons, ICurrentUser user, ITreasuryAccess access)
    { _entries = entries; _treasuries = treasuries; _reasons = reasons; _user = user; _access = access; }

    public async Task<Unit> Handle(UpdateTreasuryEntryCommand r, CancellationToken ct)
    {
        var entry = await _entries.GetByIdAsync(new TreasuryEntryId(r.Id), ct) ?? throw new NotFoundException("TreasuryEntry", r.Id);
        var treasury = await _treasuries.GetByIdAsync(entry.TreasuryId, ct) ?? throw new NotFoundException("Treasury", entry.TreasuryId.Value);
        TreasuryScope.EnsureInScope(_user, treasury);
        (await _access.ResolveAsync(ct)).EnsureUpdate(treasury.Id);
        if (entry.Origin == TreasuryEntryOrigin.AutoCollection)
        {
            // A mirrored collection: only a validated one can be corrected, and only its amount and notes.
            if (entry.ValidationStatus != TreasuryValidationStatus.Validated)
                throw new ConflictException("Validate the collection's cash first; the amount can be corrected during validation.");
            entry.AdjustValidated(r.Debit, r.Notes);
            return Unit.Value;
        }
        if (r.ReasonId == Guid.Empty)
            throw new Common.Exceptions.ValidationException(new Dictionary<string, string[]> { ["reasonId"] = new[] { "A reason is required." } });
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
    private readonly ITreasuryEntryRepository _entries; private readonly ITreasuryRepository _treasuries; private readonly ICurrentUser _user; private readonly ITreasuryAccess _access;
    public DeleteTreasuryEntryHandler(ITreasuryEntryRepository entries, ITreasuryRepository treasuries, ICurrentUser user, ITreasuryAccess access)
    { _entries = entries; _treasuries = treasuries; _user = user; _access = access; }
    public async Task<Unit> Handle(DeleteTreasuryEntryCommand r, CancellationToken ct)
    {
        var entry = await _entries.GetByIdAsync(new TreasuryEntryId(r.Id), ct) ?? throw new NotFoundException("TreasuryEntry", r.Id);
        var treasury = await _treasuries.GetByIdAsync(entry.TreasuryId, ct);
        if (treasury is not null) TreasuryScope.EnsureInScope(_user, treasury);
        (await _access.ResolveAsync(ct)).EnsureUpdate(entry.TreasuryId);
        if (entry.Origin == TreasuryEntryOrigin.AutoCollection)
            throw new ConflictException("This entry mirrors a collection; delete the collection on the Collection page instead.");
        _entries.Remove(entry);
        return Unit.Value;
    }
}

/// <summary>The treasury confirms the cash received for a mirrored collection — the collected amount or a corrected one.
/// Needs the treasury's Validate right (the page privilege alone is not enough).</summary>
public sealed record ValidateTreasuryEntryCommand(Guid Id, decimal ReceivedAmount, string? Note) : ICommand, IAuthorizedRequest
{ public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ViewAccounting }; }
public sealed class ValidateTreasuryEntryValidator : AbstractValidator<ValidateTreasuryEntryCommand>
{
    public ValidateTreasuryEntryValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.ReceivedAmount).GreaterThan(0);
        RuleFor(x => x.Note).MaximumLength(500);
    }
}
public sealed class ValidateTreasuryEntryHandler : ICommandHandler<ValidateTreasuryEntryCommand>
{
    private readonly ITreasuryEntryRepository _entries; private readonly ITreasuryRepository _treasuries;
    private readonly ICurrentUser _user; private readonly ITreasuryAccess _access; private readonly IClock _clock;
    public ValidateTreasuryEntryHandler(ITreasuryEntryRepository entries, ITreasuryRepository treasuries, ICurrentUser user, ITreasuryAccess access, IClock clock)
    { _entries = entries; _treasuries = treasuries; _user = user; _access = access; _clock = clock; }
    public async Task<Unit> Handle(ValidateTreasuryEntryCommand r, CancellationToken ct)
    {
        var entry = await _entries.GetByIdAsync(new TreasuryEntryId(r.Id), ct) ?? throw new NotFoundException("TreasuryEntry", r.Id);
        var treasury = await _treasuries.GetByIdAsync(entry.TreasuryId, ct) ?? throw new NotFoundException("Treasury", entry.TreasuryId.Value);
        TreasuryScope.EnsureInScope(_user, treasury);
        (await _access.ResolveAsync(ct)).EnsureValidate(treasury.Id);
        if (entry.Origin != TreasuryEntryOrigin.AutoCollection) throw new ConflictException("Only a collection's treasury entry is validated.");
        if (entry.ValidationStatus == TreasuryValidationStatus.Validated) throw new ConflictException("This entry is already validated.");
        entry.Validate(r.ReceivedAmount, r.Note, _user.Username, _clock.UtcNow);
        return Unit.Value;
    }
}

/// <summary>Treasury page action: mirrors every cash collection that has no treasury entry yet (collections recorded
/// before the mirroring, labs whose branch gained a treasury later). Same pass the nightly automation runs.</summary>
public sealed record SyncCollectionsToTreasuryCommand : ICommand<CollectionTreasurySyncResult>, IAuthorizedRequest
{ public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ManageAccounting }; }
public sealed class SyncCollectionsToTreasuryValidator : AbstractValidator<SyncCollectionsToTreasuryCommand> { }
public sealed class SyncCollectionsToTreasuryHandler : ICommandHandler<SyncCollectionsToTreasuryCommand, CollectionTreasurySyncResult>
{
    private readonly ICollectionTreasurySync _sync;
    public SyncCollectionsToTreasuryHandler(ICollectionTreasurySync sync) => _sync = sync;
    public Task<CollectionTreasurySyncResult> Handle(SyncCollectionsToTreasuryCommand r, CancellationToken ct) => _sync.RunAsync(ct);
}

/// <summary>Replaces a role's treasury rights (ManageUsers, like the rest of role editing). Rows with no right are removed.</summary>
public sealed record SetTreasuryGrantsCommand(Guid RoleId, IReadOnlyList<TreasuryGrantInput> Grants) : ICommand, IAuthorizedRequest
{ public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ManageUsers }; }
public sealed class SetTreasuryGrantsValidator : AbstractValidator<SetTreasuryGrantsCommand>
{
    public SetTreasuryGrantsValidator()
    {
        RuleFor(x => x.RoleId).NotEmpty();
        RuleFor(x => x.Grants).NotNull();
        RuleFor(x => x.Grants).Must(g => g.Select(x => x.TreasuryId).Distinct().Count() == g.Count).WithMessage("Each treasury may appear once.");
        RuleForEach(x => x.Grants).ChildRules(g => g.RuleFor(x => x.TreasuryId).NotEmpty());
    }
}
public sealed class SetTreasuryGrantsHandler : ICommandHandler<SetTreasuryGrantsCommand>
{
    private readonly ITreasuryGrantRepository _grants; private readonly ITreasuryRepository _treasuries; private readonly IRoleRepository _roles; private readonly ICurrentUser _user;
    public SetTreasuryGrantsHandler(ITreasuryGrantRepository grants, ITreasuryRepository treasuries, IRoleRepository roles, ICurrentUser user)
    { _grants = grants; _treasuries = treasuries; _roles = roles; _user = user; }
    public async Task<Unit> Handle(SetTreasuryGrantsCommand r, CancellationToken ct)
    {
        var role = await _roles.GetByIdAsync(new RoleId(r.RoleId), ct) ?? throw new NotFoundException("Role", r.RoleId);
        if (role.IsBuiltIn) throw new ConflictException("The built-in administrator role holds every treasury right; it is not edited.");
        if (_user.RoleId == role.Id) throw new ForbiddenException("You cannot modify your own role.");

        var existing = (await _grants.GetForRoleAsync(role.Id, ct)).ToDictionary(g => g.TreasuryId);
        foreach (var input in r.Grants)
        {
            var treasury = await _treasuries.GetByIdAsync(new TreasuryId(input.TreasuryId), ct) ?? throw new NotFoundException("Treasury", input.TreasuryId);
            var wanted = new TreasuryGrant.Rights(input.CanView, input.CanValidate, input.CanUpdate);
            if (existing.TryGetValue(treasury.Id, out var g))
            {
                if (wanted.IsEmpty) _grants.Remove(g); else g.Set(wanted.View, wanted.Validate, wanted.Update);
                existing.Remove(treasury.Id);
            }
            else if (!wanted.IsEmpty) _grants.Add(TreasuryGrant.Create(role.Id, treasury.Id, wanted.View, wanted.Validate, wanted.Update));
        }
        // Treasuries omitted from the payload lose their rights (the page always sends the full list).
        foreach (var leftover in existing.Values) _grants.Remove(leftover);
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
        if (penalty.UserType == PenaltyUser.LabRequest)
        {
            // The lab asked for the wrong test: charged to the lab through the Rep Income sheet, never deducted from
            // the area. A penalty re-typed to LabRequest loses its mirror.
            if (existing is not null) deductions.Remove(existing);
            return false;
        }
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
        v.RuleFor(userType).Must(EnumParse.IsValid<PenaltyUser>).WithMessage("User type must be Rep, DataEntry, Technician or LabRequest.");
        v.When(x => string.Equals(typeOf(x), nameof(PenaltyUser.Rep), StringComparison.OrdinalIgnoreCase), () =>
        {
            v.RuleFor(repId).NotEmpty().WithMessage("Select the representative who made the error.");
            v.RuleFor(userId).Null().WithMessage("A representative penalty cannot also name a system user.");
        });
        v.When(x => string.Equals(typeOf(x), nameof(PenaltyUser.LabRequest), StringComparison.OrdinalIgnoreCase), () =>
        {
            v.RuleFor(repId).Null().WithMessage("A lab-request penalty names no person.");
            v.RuleFor(userId).Null().WithMessage("A lab-request penalty names no person.");
        });
        v.When(x => EnumParse.IsValid<PenaltyUser>(typeOf(x))
            && !string.Equals(typeOf(x), nameof(PenaltyUser.Rep), StringComparison.OrdinalIgnoreCase)
            && !string.Equals(typeOf(x), nameof(PenaltyUser.LabRequest), StringComparison.OrdinalIgnoreCase), () =>
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

/// <summary>Shared rules of Create/Update: type, shares (count per type, distinct reps, positive amounts summing to the total
/// for a group), amounts and IBAN. The domain re-checks all of it; these give field-level 400s.</summary>
internal static class CollectionRules
{
    public static void Apply<T>(AbstractValidator<T> v, Func<T, string> type, Func<T, IReadOnlyList<CollectionShareInput>> shares,
        Func<T, decimal> cash, Func<T, decimal> bank, Func<T, string?> iban, Func<T, string?> doneBy, Func<T, string?> notes)
    {
        v.RuleFor(x => type(x)).Must(EnumParse.IsValid<CollectionType>).WithMessage("Type must be Single or Group.").OverridePropertyName("Type");
        v.RuleFor(x => shares(x)).NotNull().Must(s => s.Count > 0).WithMessage("Select at least one rep.").OverridePropertyName("Shares");
        v.RuleFor(x => shares(x)).Must(s => s.Select(x => x.RepId).Distinct().Count() == s.Count).When(x => shares(x) is not null)
            .WithMessage("A rep appears once on a collection.").OverridePropertyName("Shares");
        v.RuleFor(x => shares(x)).Must(s => s.Count == 1).When(x => shares(x) is not null && type(x) == nameof(CollectionType.Single))
            .WithMessage("A single collection names exactly one rep.").OverridePropertyName("Shares");
        v.RuleFor(x => shares(x)).Must(s => s.Count >= 2).When(x => shares(x) is not null && type(x) == nameof(CollectionType.Group))
            .WithMessage("A group collection names at least two reps.").OverridePropertyName("Shares");
        v.RuleFor(x => shares(x)).Must(s => s.All(x => x.Amount > 0)).When(x => shares(x) is not null && type(x) == nameof(CollectionType.Group))
            .WithMessage("Enter each rep's collected amount (greater than zero).").OverridePropertyName("Shares");
        v.RuleFor(x => shares(x)).Must((x, s) => s.Sum(y => y.Amount) == cash(x) + bank(x)).When(x => shares(x) is not null && type(x) == nameof(CollectionType.Group))
            .WithMessage("The reps' amounts must add up to cash + bank.").OverridePropertyName("Shares");
        v.RuleFor(x => cash(x)).GreaterThanOrEqualTo(0).OverridePropertyName("Cash");
        v.RuleFor(x => bank(x)).GreaterThanOrEqualTo(0).OverridePropertyName("Bank");
        v.RuleFor(x => x).Must(x => cash(x) + bank(x) > 0).WithMessage("Enter a cash or bank amount.").OverridePropertyName("Cash");
        v.RuleFor(x => iban(x)).Must(EnumParse.IsValid<IbanOption>).When(x => bank(x) > 0).WithMessage("Select the IBAN (12, 16 or 18) for a bank amount.").OverridePropertyName("Iban");
        v.RuleFor(x => doneBy(x)).MaximumLength(200).OverridePropertyName("DoneBy");
        v.RuleFor(x => notes(x)).MaximumLength(500).OverridePropertyName("Notes");
    }
}

public sealed record CreateCollectionCommand(DateOnly Date, string Type, IReadOnlyList<CollectionShareInput> Shares,
    decimal Cash, decimal Bank, string? Iban, string? DoneBy, string? Notes) : ICommand<Guid>, IAuthorizedRequest
{ public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ManageAccounting }; }
public sealed class CreateCollectionValidator : AbstractValidator<CreateCollectionCommand>
{
    public CreateCollectionValidator() => CollectionRules.Apply(this, x => x.Type, x => x.Shares, x => x.Cash, x => x.Bank, x => x.Iban, x => x.DoneBy, x => x.Notes);
}
public sealed class CreateCollectionHandler : ICommandHandler<CreateCollectionCommand, Guid>
{
    private readonly ICollectionRepository _repo; private readonly IRepresentativeRepository _reps; private readonly ICollectionRouting _routing;
    private readonly ITreasuryRepository _treasuries; private readonly ITreasuryEntryRepository _entries; private readonly ICurrentUser _user;
    public CreateCollectionHandler(ICollectionRepository repo, IRepresentativeRepository reps, ICollectionRouting routing,
        ITreasuryRepository treasuries, ITreasuryEntryRepository entries, ICurrentUser user)
    { _repo = repo; _reps = reps; _routing = routing; _treasuries = treasuries; _entries = entries; _user = user; }

    public async Task<Guid> Handle(CreateCollectionCommand r, CancellationToken ct)
    {
        var shares = await CollectionSupport.ResolveSharesAsync(r.Shares, _reps, _user, ct);
        var c = Collection.Create(r.Date, EnumParse.Name<CollectionType>(r.Type, nameof(r.Type)), shares, r.Cash, r.Bank,
            r.Bank > 0 ? EnumParse.Name<IbanOption>(r.Iban!, nameof(r.Iban)) : null, r.DoneBy, r.Notes);
        _repo.Add(c);
        await CollectionTreasurySync.UpsertAsync(c, _routing, _treasuries, _entries, _reps, ct); // same transaction
        return c.Id.Value;
    }
}

public sealed record UpdateCollectionCommand(Guid Id, DateOnly Date, string Type, IReadOnlyList<CollectionShareInput> Shares,
    decimal Cash, decimal Bank, string? Iban, string? DoneBy, string? Notes) : ICommand, IAuthorizedRequest
{ public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ManageAccounting }; }
public sealed class UpdateCollectionValidator : AbstractValidator<UpdateCollectionCommand>
{
    public UpdateCollectionValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        CollectionRules.Apply(this, x => x.Type, x => x.Shares, x => x.Cash, x => x.Bank, x => x.Iban, x => x.DoneBy, x => x.Notes);
    }
}
public sealed class UpdateCollectionHandler : ICommandHandler<UpdateCollectionCommand>
{
    private readonly ICollectionRepository _repo; private readonly IRepresentativeRepository _reps; private readonly ICollectionRouting _routing;
    private readonly ITreasuryRepository _treasuries; private readonly ITreasuryEntryRepository _entries; private readonly ICurrentUser _user;
    public UpdateCollectionHandler(ICollectionRepository repo, IRepresentativeRepository reps, ICollectionRouting routing,
        ITreasuryRepository treasuries, ITreasuryEntryRepository entries, ICurrentUser user)
    { _repo = repo; _reps = reps; _routing = routing; _treasuries = treasuries; _entries = entries; _user = user; }

    public async Task<Unit> Handle(UpdateCollectionCommand r, CancellationToken ct)
    {
        var c = await _repo.GetByIdAsync(new CollectionId(r.Id), ct) ?? throw new NotFoundException("Collection", r.Id);
        await CollectionSupport.EnsureRepsInScopeAsync(c, _reps, _user, ct); // the existing reps gate the edit (fail-closed)
        var shares = await CollectionSupport.ResolveSharesAsync(r.Shares, _reps, _user, ct);
        c.Update(r.Date, EnumParse.Name<CollectionType>(r.Type, nameof(r.Type)), shares, r.Cash, r.Bank,
            r.Bank > 0 ? EnumParse.Name<IbanOption>(r.Iban!, nameof(r.Iban)) : null, r.DoneBy, r.Notes);
        await CollectionTreasurySync.UpsertAsync(c, _routing, _treasuries, _entries, _reps, ct);
        return Unit.Value;
    }
}

public sealed record DeleteCollectionCommand(Guid Id) : ICommand, IAuthorizedRequest
{ public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ManageAccounting }; }
public sealed class DeleteCollectionValidator : AbstractValidator<DeleteCollectionCommand> { public DeleteCollectionValidator() => RuleFor(x => x.Id).NotEmpty(); }
public sealed class DeleteCollectionHandler : ICommandHandler<DeleteCollectionCommand>
{
    private readonly ICollectionRepository _repo; private readonly IRepresentativeRepository _reps; private readonly ITreasuryEntryRepository _entries; private readonly ICurrentUser _user;
    public DeleteCollectionHandler(ICollectionRepository repo, IRepresentativeRepository reps, ITreasuryEntryRepository entries, ICurrentUser user)
    { _repo = repo; _reps = reps; _entries = entries; _user = user; }
    public async Task<Unit> Handle(DeleteCollectionCommand r, CancellationToken ct)
    {
        var c = await _repo.GetByIdAsync(new CollectionId(r.Id), ct) ?? throw new NotFoundException("Collection", r.Id);
        await CollectionSupport.EnsureRepsInScopeAsync(c, _reps, _user, ct);
        await CollectionTreasurySync.RemoveAsync(c, _entries, ct); // refuses once the treasury has validated the cash
        _repo.Remove(c);
        return Unit.Value;
    }
}

/// <summary>
/// Keeps a collection's cash mirrored into the treasury serving the collecting rep's branch (operator decision,
/// 2026-09-16; the branch comes from <see cref="ICollectionRouting"/> now that a collection has no lab): the entry is
/// created with the collection, refreshed when it changes, removed when it is deleted, and carries the collection's
/// particulars. A collection whose reps resolve to no branch, or to a branch no active treasury covers, gets no entry
/// (the collection is never blocked; "Sync collections" / the daily automation link it once a treasury covers the
/// branch). When several active treasuries share the branch the first by name is used. Runs inside the command's transaction.
/// </summary>
public static class CollectionTreasurySync
{
    /// <summary>Creates or refreshes the mirroring entry. Returns false when no treasury can receive it.</summary>
    public static async Task<bool> UpsertAsync(Collection collection, ICollectionRouting routing, ITreasuryRepository treasuries,
        ITreasuryEntryRepository entries, IRepresentativeRepository reps, CancellationToken ct)
    {
        var existing = await entries.GetByCollectionAsync(collection.Id, ct);
        var branch = collection.Cash.Amount <= 0 ? null : await routing.ServingBranchAsync(collection.RepIds, ct);
        var treasury = string.IsNullOrWhiteSpace(branch) ? null : (await treasuries.GetActiveByBranchAsync(branch, ct)).FirstOrDefault();
        if (treasury is null || collection.Cash.Amount <= 0)
        {
            // Nothing to receive it (or no cash any more): a not-yet-validated mirror is dropped; a validated one is kept
            // — the treasury did receive that cash — and stays attributable through its system note.
            if (existing is not null && existing.ValidationStatus != TreasuryValidationStatus.Validated) entries.Remove(existing);
            return false;
        }
        var repNames = new List<string>();
        foreach (var id in collection.RepIds) repNames.Add((await reps.GetByIdAsync(id, ct))?.FullName ?? "—");
        if (existing is null) entries.Add(TreasuryEntry.FromCollection(treasury.Id, collection, repNames));
        else existing.RefreshFromCollection(collection, repNames);
        return true;
    }

    /// <summary>Removes the mirror with its collection; refused once the treasury has validated the cash (accounting integrity).</summary>
    public static async Task RemoveAsync(Collection collection, ITreasuryEntryRepository entries, CancellationToken ct)
    {
        var existing = await entries.GetByCollectionAsync(collection.Id, ct);
        if (existing is null) return;
        if (existing.ValidationStatus == TreasuryValidationStatus.Validated)
            throw new ConflictException("The treasury has already validated this collection's cash; the collection can no longer be deleted.");
        entries.Remove(existing);
    }
}

internal static class CollectionSupport
{
    /// <summary>Every rep on a collection must exist, be within the caller's scope (fail-closed) and be a Lab Responsible
    /// — the only type that collects labs' money (operator decision, 2026-09-16).</summary>
    public static async Task<List<(RepresentativeId RepId, decimal Amount)>> ResolveSharesAsync(IReadOnlyList<CollectionShareInput> shares,
        IRepresentativeRepository reps, ICurrentUser user, CancellationToken ct)
    {
        var result = new List<(RepresentativeId, decimal)>();
        foreach (var s in shares)
        {
            var rep = await reps.GetByIdAsync(new RepresentativeId(s.RepId), ct) ?? throw new NotFoundException("Representative", s.RepId);
            user.EnsureInScope(rep);
            if (rep.Type != RepresentativeType.LabResponsible)
                throw new Common.Exceptions.ValidationException(new Dictionary<string, string[]>
                { ["Shares"] = new[] { $"{rep.FullName} is not a Lab Responsible; only Lab Responsible reps collect." } });
            result.Add((rep.Id, s.Amount));
        }
        return result;
    }

    /// <summary>An existing collection may be edited/deleted only by a caller who can see all of its reps (record-level scope).</summary>
    public static async Task EnsureRepsInScopeAsync(Collection c, IRepresentativeRepository reps, ICurrentUser user, CancellationToken ct)
    {
        foreach (var id in c.RepIds)
        {
            var rep = await reps.GetByIdAsync(id, ct);
            if (rep is not null) user.EnsureInScope(rep);
        }
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

/// <summary>
/// Saves a Lab Responsible's real-income sheet for one date: every row shown is sent back; a row is created, updated, or
/// — when it comes back all-zero with no notes — removed. Rows not in the payload are left untouched.
/// </summary>
public sealed record SaveRealIncomeSheetCommand(DateOnly Date, Guid RepresentativeId, IReadOnlyList<RealIncomeRowInput> Rows) : ICommand, IAuthorizedRequest
{ public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ManageAccounting }; }
public sealed class SaveRealIncomeSheetValidator : AbstractValidator<SaveRealIncomeSheetCommand>
{
    public SaveRealIncomeSheetValidator()
    {
        RuleFor(x => x.RepresentativeId).NotEmpty();
        RuleFor(x => x.Rows).NotNull();
        RuleFor(x => x.Rows).Must(r => r.Select(x => x.LaboratoryId).Distinct().Count() == r.Count).When(x => x.Rows is not null).WithMessage("A lab appears once on the sheet.");
        RuleForEach(x => x.Rows).ChildRules(row =>
        {
            row.RuleFor(r => r.LaboratoryId).NotEmpty();
            row.RuleFor(r => r.Samples).GreaterThanOrEqualTo(0);
            row.RuleFor(r => r.TotalRequired).GreaterThanOrEqualTo(0);
            row.RuleFor(r => r.Paid).GreaterThanOrEqualTo(0);
            row.RuleFor(r => r.DelayedPayment).GreaterThanOrEqualTo(0);
            row.RuleFor(r => r.Paid).LessThanOrEqualTo(r => r.TotalRequired).WithMessage("Paid cannot exceed the total required.");
            row.RuleFor(r => r.Notes).MaximumLength(500);
        });
    }
}
public sealed class SaveRealIncomeSheetHandler : ICommandHandler<SaveRealIncomeSheetCommand>
{
    private readonly IRepLabIncomeRepository _repo; private readonly IRepresentativeRepository _reps; private readonly ILaboratoryRepository _labs; private readonly ICurrentUser _user;
    public SaveRealIncomeSheetHandler(IRepLabIncomeRepository repo, IRepresentativeRepository reps, ILaboratoryRepository labs, ICurrentUser user)
    { _repo = repo; _reps = reps; _labs = labs; _user = user; }

    public async Task<Unit> Handle(SaveRealIncomeSheetCommand r, CancellationToken ct)
    {
        var rep = await _reps.GetByIdAsync(new RepresentativeId(r.RepresentativeId), ct) ?? throw new NotFoundException("Representative", r.RepresentativeId);
        _user.EnsureInScope(rep);
        if (rep.Type != RepresentativeType.LabResponsible)
            throw new Common.Exceptions.ValidationException(new Dictionary<string, string[]> { ["representativeId"] = new[] { "Real income is recorded per Lab Responsible." } });
        var existing = (await _repo.GetForRepDateAsync(rep.Id, r.Date, ct)).ToDictionary(e => e.LaboratoryId);
        foreach (var row in r.Rows)
        {
            var lab = await _labs.GetByIdAsync(new LaboratoryId(row.LaboratoryId), ct) ?? throw new NotFoundException("Laboratory", row.LaboratoryId);
            _user.EnsureInScope(lab);
            var empty = row.Samples == 0 && row.TotalRequired == 0 && row.Paid == 0 && row.DelayedPayment == 0 && string.IsNullOrWhiteSpace(row.Notes);
            if (existing.TryGetValue(lab.Id, out var line))
            {
                if (empty) _repo.Remove(line);
                else line.Update(row.Samples, row.TotalRequired, row.Paid, row.DelayedPayment, row.Notes);
            }
            else if (!empty)
                _repo.Add(RepLabIncome.Create(rep.Id, lab.Id, r.Date, row.Samples, row.TotalRequired, row.Paid, row.DelayedPayment, row.Notes));
        }
        return Unit.Value;
    }
}

/// <summary>
/// Rep Income page: pulls the LDM (Oracle) lab statistics for ONE date on demand — the same LabStats feed the nightly
/// labstats-sync runs at 00:05 for the previous day — so the sheet's "LDM income" column is current before the rep's
/// figures are compared with it. ManageAccounting (it rewrites that day's daily_lab_statistic rows).
/// </summary>
public sealed record SyncRealIncomeLdmCommand(DateOnly Date) : ICommand<OracleSyncResult>, IAuthorizedRequest
{ public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ManageAccounting }; }
public sealed class SyncRealIncomeLdmValidator : AbstractValidator<SyncRealIncomeLdmCommand> { public SyncRealIncomeLdmValidator() => RuleFor(x => x.Date).NotEmpty(); }
public sealed class SyncRealIncomeLdmHandler : ICommandHandler<SyncRealIncomeLdmCommand, OracleSyncResult>
{
    private readonly IOracleSyncRunner _runner;
    public SyncRealIncomeLdmHandler(IOracleSyncRunner runner) => _runner = runner;
    public Task<OracleSyncResult> Handle(SyncRealIncomeLdmCommand r, CancellationToken ct) => _runner.RunLabStatsAsync(r.Date, r.Date, manual: true, ct);
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
