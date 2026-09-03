using FollowUp.Application.Common.Abstractions;
using FollowUp.Application.Common.Abstractions.Persistence;
using FollowUp.Application.Common.Exceptions;
using FollowUp.Application.Common.Messaging;
using FollowUp.Application.Features.LabStats;
using FollowUp.Domain.Identity;
using FollowUp.Domain.Statistics;
using FluentValidation;
using MediatR;

namespace FollowUp.Application.Features.TestCatalogue;

// ---- Read side ----

public sealed record TestGroupDto(Guid Id, string Code, string NameEn, string? NameAr, string Source);
public sealed record TestSetupDto(Guid Id, string Code, string NameEn, string? NameAr, Guid? GroupId,
    int TestType, decimal Cost, string? GroupCode, string? GroupName, string Source);
public sealed record TestStatDto(DateOnly Date, string TestCode, int TestType, string? TestName, string? GroupName, int Count, decimal Income);

public interface ITestCatalogueQueries
{
    Task<IReadOnlyList<TestGroupDto>> GetGroupsAsync(CancellationToken ct);
    Task<IReadOnlyList<TestSetupDto>> GetSetupsAsync(CancellationToken ct);
    Task<IReadOnlyList<TestStatDto>> GetTestStatsAsync(DateOnly from, DateOnly to, CancellationToken ct);
}

public sealed record GetTestGroupsQuery : IQuery<IReadOnlyList<TestGroupDto>>, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ViewTeststats };
}
public sealed class GetTestGroupsHandler : IQueryHandler<GetTestGroupsQuery, IReadOnlyList<TestGroupDto>>
{
    private readonly ITestCatalogueQueries _q;
    public GetTestGroupsHandler(ITestCatalogueQueries q) => _q = q;
    public Task<IReadOnlyList<TestGroupDto>> Handle(GetTestGroupsQuery r, CancellationToken ct) => _q.GetGroupsAsync(ct);
}

public sealed record GetTestSetupsQuery : IQuery<IReadOnlyList<TestSetupDto>>, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ViewTeststats };
}
public sealed class GetTestSetupsHandler : IQueryHandler<GetTestSetupsQuery, IReadOnlyList<TestSetupDto>>
{
    private readonly ITestCatalogueQueries _q;
    public GetTestSetupsHandler(ITestCatalogueQueries q) => _q = q;
    public Task<IReadOnlyList<TestSetupDto>> Handle(GetTestSetupsQuery r, CancellationToken ct) => _q.GetSetupsAsync(ct);
}

public sealed record GetTestStatsQuery(DateOnly From, DateOnly To) : IQuery<IReadOnlyList<TestStatDto>>, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ViewTeststats };
}
public sealed class GetTestStatsHandler : IQueryHandler<GetTestStatsQuery, IReadOnlyList<TestStatDto>>
{
    private readonly ITestCatalogueQueries _q;
    public GetTestStatsHandler(ITestCatalogueQueries q) => _q = q;
    public Task<IReadOnlyList<TestStatDto>> Handle(GetTestStatsQuery r, CancellationToken ct) => _q.GetTestStatsAsync(r.From, r.To, ct);
}

/// <summary>One test that is counted in Test Statistics but not in Lab Statistics (its registration resolves to no lab).</summary>
public sealed record NoLabTestRowDto(DateTime RegDate, string AccNo, string PatientName, string Doctor, string RegisteredBy, string TestName, string TestCode, int TestType);

/// <summary>
/// Live Oracle report (not synced): the per-test detail of tests present on Test Statistics but absent from Lab
/// Statistics for the selected range — same "test" definition as the stats, restricted to regs with no resolvable
/// lab. Returns accession (lab_no), patient name and test name.
/// </summary>
public sealed record GetNoLabTestsReportQuery(DateOnly From, DateOnly To) : IQuery<IReadOnlyList<NoLabTestRowDto>>, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ViewTeststats };
}

public sealed class GetNoLabTestsReportHandler : IQueryHandler<GetNoLabTestsReportQuery, IReadOnlyList<NoLabTestRowDto>>
{
    private readonly IOracleReader _reader;
    public GetNoLabTestsReportHandler(IOracleReader reader) => _reader = reader;

    public async Task<IReadOnlyList<NoLabTestRowDto>> Handle(GetNoLabTestsReportQuery r, CancellationToken ct)
    {
        var from = r.From; var to = r.To;
        if (to < from) (from, to) = (to, from);
        // Half-open window [from 00:00, (to + 1 day) 00:00) — matches the stats syncs (To is inclusive).
        var window = new OracleDateWindow(from.ToDateTime(TimeOnly.MinValue), to.AddDays(1).ToDateTime(TimeOnly.MinValue));
        var rows = await _reader.ExecuteAsync("NoLabTests", window, ct);
        var list = new List<NoLabTestRowDto>(rows.Count);
        foreach (var row in rows)
        {
            var v = row.Values;
            var regDt = v.TryGetValue("REG_DT", out var d) && d is not null ? Convert.ToDateTime(d) : default;
            var acc = v.TryGetValue("ACC_NO", out var a) && a is not null ? Convert.ToString(a)!.Trim() : string.Empty;
            var patient = v.TryGetValue("PATIENT_NAME", out var p) && p is not null ? Convert.ToString(p)!.Trim() : string.Empty;
            var doctor = v.TryGetValue("DOCTOR", out var dr) && dr is not null ? Convert.ToString(dr)!.Trim() : string.Empty;
            var regBy = v.TryGetValue("REGISTERED_BY", out var rb) && rb is not null ? Convert.ToString(rb)!.Trim() : string.Empty;
            var tname = v.TryGetValue("TEST_NAME", out var tn) && tn is not null ? Convert.ToString(tn)!.Trim() : string.Empty;
            var tcode = v.TryGetValue("TEST_CODE", out var tc) && tc is not null ? Convert.ToString(tc)!.Trim() : string.Empty;
            var ttype = v.TryGetValue("TEST_TYPE", out var tt) && tt is not null ? Convert.ToInt32(tt) : 0;
            list.Add(new NoLabTestRowDto(regDt, acc, patient, doctor, regBy, tname, tcode, ttype));
        }
        return list;
    }
}

// ---- Test group CRUD ----

public sealed record CreateTestGroupCommand(string Code, string NameEn, string? NameAr) : ICommand<Guid>, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.AddGroups };
}
public sealed class CreateTestGroupHandler : ICommandHandler<CreateTestGroupCommand, Guid>
{
    private readonly ITestGroupRepository _repo;
    public CreateTestGroupHandler(ITestGroupRepository repo) => _repo = repo;
    public async Task<Guid> Handle(CreateTestGroupCommand r, CancellationToken ct)
    {
        var code = r.Code?.Trim() ?? string.Empty;
        if (await _repo.GetByCodeAsync(code, ct) is not null)
            throw new Common.Exceptions.ValidationException(new Dictionary<string, string[]>
            { ["code"] = new[] { $"A group with code '{r.Code}' already exists." } });
        var g = TestGroup.Create(code, r.NameEn, r.NameAr);
        _repo.Add(g);
        return g.Id.Value;
    }
}

public sealed record UpdateTestGroupCommand(Guid Id, string NameEn, string? NameAr) : ICommand, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.UpdateGroups };
}
public sealed class UpdateTestGroupHandler : ICommandHandler<UpdateTestGroupCommand>
{
    private readonly ITestGroupRepository _repo;
    public UpdateTestGroupHandler(ITestGroupRepository repo) => _repo = repo;
    public async Task<Unit> Handle(UpdateTestGroupCommand r, CancellationToken ct)
    {
        var g = await _repo.GetByIdAsync(new TestGroupId(r.Id), ct) ?? throw new NotFoundException("Test group", r.Id);
        g.Rename(r.NameEn, r.NameAr);
        return Unit.Value;
    }
}

/// <summary>Deletes a group; its tests are left ungrouped (mirrors the SET NULL FK, SRS FR-14).</summary>
public sealed record DeleteTestGroupCommand(Guid Id) : ICommand, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.DeleteGroups };
}
public sealed class DeleteTestGroupHandler : ICommandHandler<DeleteTestGroupCommand>
{
    private readonly ITestGroupRepository _groups;
    private readonly ITestSetupRepository _setups;
    public DeleteTestGroupHandler(ITestGroupRepository groups, ITestSetupRepository setups) { _groups = groups; _setups = setups; }
    public async Task<Unit> Handle(DeleteTestGroupCommand r, CancellationToken ct)
    {
        var groupId = new TestGroupId(r.Id);
        var group = await _groups.GetByIdAsync(groupId, ct) ?? throw new NotFoundException("Test group", r.Id);
        foreach (var setup in await _setups.GetByGroupAsync(groupId, ct))
            setup.Ungroup();
        _groups.Remove(group);
        return Unit.Value;
    }
}

// ---- Test setup CRUD ----

public sealed record CreateTestSetupCommand(string Code, string NameEn, string? NameAr, Guid? GroupId,
    int TestType = 0, decimal Cost = 0m) : ICommand<Guid>, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.AddTestsetup };
}
public sealed class CreateTestSetupHandler : ICommandHandler<CreateTestSetupCommand, Guid>
{
    private readonly ITestSetupRepository _repo;
    public CreateTestSetupHandler(ITestSetupRepository repo) => _repo = repo;
    public async Task<Guid> Handle(CreateTestSetupCommand r, CancellationToken ct)
    {
        var code = r.Code?.Trim().ToUpperInvariant() ?? string.Empty;
        if (await _repo.GetByCodeAsync(code, r.TestType, ct) is not null)
            throw new Common.Exceptions.ValidationException(new Dictionary<string, string[]>
            { ["code"] = new[] { $"A test with code '{r.Code}' and type {r.TestType} already exists." } });
        var s = TestSetup.Create(code, r.NameEn, r.NameAr, r.GroupId is { } g ? new TestGroupId(g) : null,
            r.TestType, new Domain.Common.Money(r.Cost));
        _repo.Add(s);
        return s.Id.Value;
    }
}

public sealed record UpdateTestSetupCommand(Guid Id, string NameEn, string? NameAr, Guid? GroupId,
    int TestType = 0, decimal Cost = 0m) : ICommand, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.UpdateTestsetup };
}
public sealed class UpdateTestSetupHandler : ICommandHandler<UpdateTestSetupCommand>
{
    private readonly ITestSetupRepository _repo;
    public UpdateTestSetupHandler(ITestSetupRepository repo) => _repo = repo;
    public async Task<Unit> Handle(UpdateTestSetupCommand r, CancellationToken ct)
    {
        var s = await _repo.GetByIdAsync(new TestSetupId(r.Id), ct) ?? throw new NotFoundException("Test setup", r.Id);
        s.Update(r.NameEn, r.NameAr, r.GroupId is { } g ? new TestGroupId(g) : null, r.TestType, new Domain.Common.Money(r.Cost));
        return Unit.Value;
    }
}

public sealed record DeleteTestSetupCommand(Guid Id) : ICommand, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.DeleteTestsetup };
}
public sealed class DeleteTestSetupHandler : ICommandHandler<DeleteTestSetupCommand>
{
    private readonly ITestSetupRepository _repo;
    public DeleteTestSetupHandler(ITestSetupRepository repo) => _repo = repo;
    public async Task<Unit> Handle(DeleteTestSetupCommand r, CancellationToken ct)
    {
        var s = await _repo.GetByIdAsync(new TestSetupId(r.Id), ct) ?? throw new NotFoundException("Test setup", r.Id);
        _repo.Remove(s);
        return Unit.Value;
    }
}

// ---- Test stats import ----

public sealed record ImportTestStatsCommand(byte[] Content) : ICommand<ImportSummary>, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.AddTeststats };
}
public sealed class ImportTestStatsHandler : ICommandHandler<ImportTestStatsCommand, ImportSummary>
{
    private readonly ISpreadsheetReader _reader;
    private readonly ITestStatisticRepository _repository;
    public ImportTestStatsHandler(ISpreadsheetReader reader, ITestStatisticRepository repository)
    {
        _reader = reader; _repository = repository;
    }

    public async Task<ImportSummary> Handle(ImportTestStatsCommand request, CancellationToken ct)
    {
        if (request.Content is null || request.Content.Length == 0)
            throw new Common.Exceptions.ValidationException(new Dictionary<string, string[]> { ["file"] = new[] { "The uploaded file is empty." } });
        IReadOnlyList<IReadOnlyDictionary<string, string>> rows;
        try { rows = _reader.ReadRows(request.Content); }
        catch (Exception ex)
        { throw new Common.Exceptions.ValidationException(new Dictionary<string, string[]> { ["file"] = new[] { $"Could not read the spreadsheet: {ex.Message}" } }); }
        int processed = 0, upserted = 0, skipped = 0;
        var warnings = new List<string>();

        foreach (var row in rows)
        {
            processed++;
            if (!ImportParsing.TryDate(row, "Date", out var date) ||
                !ImportParsing.TryString(row, "TestCode", out var testCode))
            {
                skipped++;
                warnings.Add($"Row {processed}: missing Date or TestCode.");
                continue;
            }

            var count = ImportParsing.Int(row, "Count");
            var income = ImportParsing.Decimal(row, "Income");
            // Manual xlsx imports carry no Oracle test_type — they use type 0.
            var stat = await _repository.GetAsync(date, testCode, 0, ct);
            if (stat is null)
            {
                stat = TestStatistic.For(date, testCode, 0);
                _repository.Add(stat);
            }
            stat.SetCount(count);
            stat.SetIncome(new Domain.Common.Money(income));
            upserted++;
        }

        return new ImportSummary(processed, upserted, skipped, warnings);
    }
}

// ---- Test stats Oracle sync (date-scoped) ----

/// <summary>
/// Pulls per-test daily statistics from Oracle for an inclusive date range and upserts them into existing data
/// (SRS FR-17). Triggered manually from the Test Statistics page (operator-chosen range, default yesterday→today);
/// the nightly job re-uses the same runner for "yesterday".
/// </summary>
public sealed record SyncTestStatsCommand(DateOnly From, DateOnly To) : ICommand<OracleSyncResult>, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.AddTeststats };
}

public sealed class SyncTestStatsValidator : AbstractValidator<SyncTestStatsCommand>
{
    public SyncTestStatsValidator()
    {
        RuleFor(x => x.From).LessThanOrEqualTo(x => x.To)
            .WithMessage("The start date must be on or before the end date.");
    }
}

public sealed class SyncTestStatsHandler : ICommandHandler<SyncTestStatsCommand, OracleSyncResult>
{
    private readonly IOracleSyncRunner _runner;
    public SyncTestStatsHandler(IOracleSyncRunner runner) => _runner = runner;
    public Task<OracleSyncResult> Handle(SyncTestStatsCommand r, CancellationToken ct) =>
        _runner.RunTestStatsAsync(r.From, r.To, manual: true, ct);
}
