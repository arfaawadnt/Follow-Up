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

public sealed record TestGroupDto(Guid Id, string Code, string NameEn, string? NameAr);
public sealed record TestSetupDto(Guid Id, string Code, string NameEn, string? NameAr, Guid? GroupId);
public sealed record TestStatDto(DateOnly Date, string TestCode, int Count);

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

// ---- Test group CRUD ----

public sealed record CreateTestGroupCommand(string Code, string NameEn, string? NameAr) : ICommand<Guid>, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.AddGroups };
}
public sealed class CreateTestGroupHandler : ICommandHandler<CreateTestGroupCommand, Guid>
{
    private readonly ITestGroupRepository _repo;
    public CreateTestGroupHandler(ITestGroupRepository repo) => _repo = repo;
    public Task<Guid> Handle(CreateTestGroupCommand r, CancellationToken ct)
    {
        var g = TestGroup.Create(r.Code, r.NameEn, r.NameAr);
        _repo.Add(g);
        return Task.FromResult(g.Id.Value);
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

public sealed record CreateTestSetupCommand(string Code, string NameEn, string? NameAr, Guid? GroupId) : ICommand<Guid>, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.AddTestsetup };
}
public sealed class CreateTestSetupHandler : ICommandHandler<CreateTestSetupCommand, Guid>
{
    private readonly ITestSetupRepository _repo;
    public CreateTestSetupHandler(ITestSetupRepository repo) => _repo = repo;
    public Task<Guid> Handle(CreateTestSetupCommand r, CancellationToken ct)
    {
        var s = TestSetup.Create(r.Code, r.NameEn, r.NameAr, r.GroupId is { } g ? new TestGroupId(g) : null);
        _repo.Add(s);
        return Task.FromResult(s.Id.Value);
    }
}

public sealed record UpdateTestSetupCommand(Guid Id, string NameEn, string? NameAr, Guid? GroupId) : ICommand, IAuthorizedRequest
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
        s.Update(r.NameEn, r.NameAr, r.GroupId is { } g ? new TestGroupId(g) : null);
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
            var stat = await _repository.GetAsync(date, testCode, ct);
            if (stat is null)
            {
                stat = TestStatistic.For(date, testCode);
                _repository.Add(stat);
            }
            stat.SetCount(count);
            upserted++;
        }

        return new ImportSummary(processed, upserted, skipped, warnings);
    }
}
