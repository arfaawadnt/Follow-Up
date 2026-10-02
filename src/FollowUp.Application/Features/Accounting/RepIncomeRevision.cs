using FollowUp.Application.Common.Abstractions;
using FollowUp.Application.Common.Abstractions.Persistence;
using FollowUp.Application.Common.Exceptions;
using FollowUp.Application.Common.Messaging;
using FollowUp.Application.Common.Security;
using FollowUp.Domain.Accounting;
using FollowUp.Domain.Identity;
using FollowUp.Domain.Laboratories;
using FollowUp.Domain.Representatives;
using FluentValidation;

namespace FollowUp.Application.Features.Accounting;

// =====================================================================================================================
// Rep Income Revision (2026-09-30): the reviewer compares every entered Rep Income sheet line (samples, total required,
// paid, remaining, delayed payment, notes) with LDM — the synced income of the lab that day, its accessions, tests, tests
// not yet verified and tests added late to their registration (within / after 3 hours) — and records the ACTUAL figures
// beside it (actual income, actual paid, actual remaining = income − paid, actual delayed payment, notes). The revision is
// a review record: the statement keeps posting the entered figures.
// =====================================================================================================================

/// <summary>
/// One revision grid line = one lab on one day: the entered sheet line (null <c>EntryId</c> when the lab had LDM income
/// that day but no sheet entry — listed only when a Lab Responsible is chosen), the LDM side, and the recorded revision.
/// </summary>
public sealed record RepIncomeRevisionRowDto(Guid LaboratoryId, string LabDisplayCode, string LabName, string? Governorate, string? City, string? Area,
    Guid RepresentativeId, string RepName, DateOnly Date, DateTimeOffset? EnteredAt, Guid? EntryId,
    int Samples, decimal TotalRequired, decimal Paid, decimal Remaining, decimal DelayedPayment, string? Notes,
    decimal LdmIncome, int Accessions, int Tests, int NotVerified, int AddedWithin3h, int AddedAfter3h,
    Guid? RevisionId, decimal? ActualIncome, decimal? ActualPaid, decimal? ActualRemaining, decimal? ActualDelayedPayment,
    string? RevisionNotes, DateTimeOffset? RevisedAt, string? RevisedBy);

/// <summary>Revision lines over an inclusive range, optionally narrowed to one Lab Responsible and/or one lab (ViewAccounting).</summary>
public sealed record GetRepIncomeRevisionQuery(DateOnly From, DateOnly To, Guid? RepresentativeId = null, Guid? LaboratoryId = null)
    : IQuery<IReadOnlyList<RepIncomeRevisionRowDto>>, IAuthorizedRequest
{ public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ViewAccounting }; }
public sealed class GetRepIncomeRevisionValidator : AbstractValidator<GetRepIncomeRevisionQuery>
{
    public GetRepIncomeRevisionValidator()
    {
        RuleFor(x => x.From).LessThanOrEqualTo(x => x.To).WithMessage("The start date must be on or before the end date.");
        RuleFor(x => x).Must(x => x.To.DayNumber - x.From.DayNumber <= 366).WithMessage("The range may span at most one year.");
    }
}
public sealed class GetRepIncomeRevisionHandler : IQueryHandler<GetRepIncomeRevisionQuery, IReadOnlyList<RepIncomeRevisionRowDto>>
{
    private readonly IAccountingQueries _queries; private readonly ICurrentUser _user;
    public GetRepIncomeRevisionHandler(IAccountingQueries queries, ICurrentUser user) { _queries = queries; _user = user; }
    public Task<IReadOnlyList<RepIncomeRevisionRowDto>> Handle(GetRepIncomeRevisionQuery r, CancellationToken ct) =>
        _queries.RepIncomeRevisionAsync(r.From, r.To, r.RepresentativeId, r.LaboratoryId, _user.Scope, _user.Has(Privileges.ShowEncryptedLabs), ct);
}

/// <summary>
/// Records (or replaces) the reviewer's actual figures for one lab on one day; an all-zero line with no notes removes the
/// revision. Returns the revision id, or null when it was removed. ManageAccounting.
/// </summary>
public sealed record SaveRepIncomeRevisionCommand(Guid LaboratoryId, Guid RepresentativeId, DateOnly Date,
    decimal ActualIncome, decimal ActualPaid, decimal ActualDelayedPayment, string? Notes) : ICommand<Guid?>, IAuthorizedRequest
{ public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ManageAccounting }; }
public sealed class SaveRepIncomeRevisionValidator : AbstractValidator<SaveRepIncomeRevisionCommand>
{
    public SaveRepIncomeRevisionValidator()
    {
        RuleFor(x => x.LaboratoryId).NotEmpty();
        RuleFor(x => x.RepresentativeId).NotEmpty();
        RuleFor(x => x.ActualIncome).GreaterThanOrEqualTo(0);
        RuleFor(x => x.ActualPaid).GreaterThanOrEqualTo(0);
        RuleFor(x => x.ActualDelayedPayment).GreaterThanOrEqualTo(0);
        RuleFor(x => x.ActualPaid).LessThanOrEqualTo(x => x.ActualIncome).WithMessage("Actual paid cannot exceed the actual income.");
        RuleFor(x => x.Notes).MaximumLength(500);
    }
}
public sealed class SaveRepIncomeRevisionHandler : ICommandHandler<SaveRepIncomeRevisionCommand, Guid?>
{
    private readonly IRepIncomeRevisionRepository _repo; private readonly ILaboratoryRepository _labs; private readonly IRepresentativeRepository _reps; private readonly ICurrentUser _user;
    public SaveRepIncomeRevisionHandler(IRepIncomeRevisionRepository repo, ILaboratoryRepository labs, IRepresentativeRepository reps, ICurrentUser user)
    { _repo = repo; _labs = labs; _reps = reps; _user = user; }

    public async Task<Guid?> Handle(SaveRepIncomeRevisionCommand r, CancellationToken ct)
    {
        var lab = await _labs.GetByIdAsync(new LaboratoryId(r.LaboratoryId), ct) ?? throw new NotFoundException("Laboratory", r.LaboratoryId);
        _user.EnsureInScope(lab);
        var rep = await _reps.GetByIdAsync(new RepresentativeId(r.RepresentativeId), ct) ?? throw new NotFoundException("Representative", r.RepresentativeId);
        _user.EnsureInScope(rep);
        var existing = await _repo.GetAsync(rep.Id, lab.Id, r.Date, ct);
        var empty = r.ActualIncome == 0 && r.ActualPaid == 0 && r.ActualDelayedPayment == 0 && string.IsNullOrWhiteSpace(r.Notes);
        if (empty)
        {
            if (existing is not null) _repo.Remove(existing);
            return null;
        }
        if (existing is not null)
        {
            existing.Update(r.ActualIncome, r.ActualPaid, r.ActualDelayedPayment, r.Notes);
            return existing.Id.Value;
        }
        var created = RepIncomeRevision.Create(rep.Id, lab.Id, r.Date, r.ActualIncome, r.ActualPaid, r.ActualDelayedPayment, r.Notes);
        _repo.Add(created);
        return created.Id.Value;
    }
}
