using FluentValidation;
using FollowUp.Application.Common.Abstractions;
using FollowUp.Application.Common.Messaging;
using FollowUp.Domain.Identity;

namespace FollowUp.Application.Features.RegistrationChanges;

// =====================================================================================================================
// Registration Changes (2026-09-30): the LDM REG_LOG audit — every edit made to a registration (which column, old →
// new, who, when) joined to the registration (accession, patient, creation time, lab). Synced nightly per
// modification-date window (RegLog feed), listed on the Auditing → Registration Changes page (by registration-date
// and/or modification-date range) and mailed as a section of the daily Email Reports.
// =====================================================================================================================

/// <summary>One registration change, enriched with the lab's stamped geography. <c>DelayMinutes</c> = minutes between the
/// registration's creation and the change (null when the creation time is unknown).</summary>
public sealed record RegistrationChangeDto(long TransId, long RegKey, string AccNo, string PatientName, DateTime? RegCreatedAt, DateOnly? RegDate,
    string? RegBranch, string? LabCode, string? LabName, string? Governorate, string? City, string? Area,
    string Column, string? OldValue, string? NewValue, string ModifiedBy, DateTime ModifiedAt, double? DelayMinutes);

public interface IRegistrationChangeQueries
{
    /// <summary>Changes whose registration date and/or modification date fall in the given inclusive ranges (either range may be
    /// omitted, not both), scoped to the caller's labs ("No lab" rows only for a global scope).</summary>
    Task<IReadOnlyList<RegistrationChangeDto>> ListAsync(DateOnly? regFrom, DateOnly? regTo, DateOnly? modFrom, DateOnly? modTo, OrgScope scope, CancellationToken ct);
}

public sealed record GetRegistrationChangesQuery(DateOnly? RegFrom, DateOnly? RegTo, DateOnly? ModFrom, DateOnly? ModTo)
    : IQuery<IReadOnlyList<RegistrationChangeDto>>, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ViewRegistrationChanges };
}

public sealed class GetRegistrationChangesValidator : AbstractValidator<GetRegistrationChangesQuery>
{
    public GetRegistrationChangesValidator()
    {
        RuleFor(x => x).Must(x => (x.RegFrom.HasValue && x.RegTo.HasValue) || (x.ModFrom.HasValue && x.ModTo.HasValue))
            .WithMessage("Choose a registration-date range or a modification-date range.");
        RuleFor(x => x).Must(x => !x.RegFrom.HasValue || !x.RegTo.HasValue || (x.RegFrom <= x.RegTo && x.RegTo.Value.DayNumber - x.RegFrom.Value.DayNumber <= 366))
            .WithMessage("The registration-date range must be ordered and span at most one year.");
        RuleFor(x => x).Must(x => !x.ModFrom.HasValue || !x.ModTo.HasValue || (x.ModFrom <= x.ModTo && x.ModTo.Value.DayNumber - x.ModFrom.Value.DayNumber <= 366))
            .WithMessage("The modification-date range must be ordered and span at most one year.");
    }
}

public sealed class GetRegistrationChangesHandler : IQueryHandler<GetRegistrationChangesQuery, IReadOnlyList<RegistrationChangeDto>>
{
    private readonly IRegistrationChangeQueries _queries;
    private readonly ICurrentUser _user;
    public GetRegistrationChangesHandler(IRegistrationChangeQueries queries, ICurrentUser user) { _queries = queries; _user = user; }
    public Task<IReadOnlyList<RegistrationChangeDto>> Handle(GetRegistrationChangesQuery r, CancellationToken ct) =>
        _queries.ListAsync(r.RegFrom, r.RegTo, r.ModFrom, r.ModTo, _user.Scope, ct);
}

/// <summary>Pulls REG_LOG from Oracle for an inclusive modification-date range and replaces the synced rows of that
/// window (the nightly job does yesterday). Triggered from the page's "Sync from Oracle" dialog.</summary>
public sealed record SyncRegistrationChangesCommand(DateOnly From, DateOnly To) : ICommand<OracleSyncResult>, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ViewRegistrationChanges };
}

public sealed class SyncRegistrationChangesValidator : AbstractValidator<SyncRegistrationChangesCommand>
{
    public SyncRegistrationChangesValidator()
    {
        RuleFor(x => x.From).LessThanOrEqualTo(x => x.To).WithMessage("The start date must be on or before the end date.");
        RuleFor(x => x).Must(x => x.To.DayNumber - x.From.DayNumber <= 92).WithMessage("Sync at most three months at a time.");
    }
}

public sealed class SyncRegistrationChangesHandler : ICommandHandler<SyncRegistrationChangesCommand, OracleSyncResult>
{
    private readonly IOracleSyncRunner _runner;
    public SyncRegistrationChangesHandler(IOracleSyncRunner runner) => _runner = runner;
    public Task<OracleSyncResult> Handle(SyncRegistrationChangesCommand r, CancellationToken ct) =>
        _runner.RunRegistrationChangesAsync(r.From, r.To, manual: true, ct);
}
