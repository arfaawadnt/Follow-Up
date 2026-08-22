using System.Globalization;
using FollowUp.Application.Common.Abstractions;
using FollowUp.Application.Common.Abstractions.Persistence;
using FollowUp.Application.Common.Messaging;
using FollowUp.Domain.Common;
using FollowUp.Domain.Identity;
using FollowUp.Domain.Statistics;

namespace FollowUp.Application.Features.LabStats;

public sealed record LabStatDto(DateOnly Date, string LabCode, string? Name, string? Segment,
    string? Governorate, string? City, string? Area, int Registrations, int TestCount, decimal Income);
public sealed record ImportSummary(int Processed, int Upserted, int Skipped, IReadOnlyList<string> Warnings);

public interface ILabStatsQueries
{
    Task<IReadOnlyList<LabStatDto>> ListAsync(DateOnly from, DateOnly to, OrgScope scope, CancellationToken ct);
}

/// <summary>Lists daily lab statistics over a range (SRS FR-13).</summary>
public sealed record GetLabStatsQuery(DateOnly From, DateOnly To) : IQuery<IReadOnlyList<LabStatDto>>, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ViewLabStats };
}

public sealed class GetLabStatsHandler : IQueryHandler<GetLabStatsQuery, IReadOnlyList<LabStatDto>>
{
    private readonly ILabStatsQueries _queries;
    private readonly ICurrentUser _user;
    public GetLabStatsHandler(ILabStatsQueries queries, ICurrentUser user) { _queries = queries; _user = user; }
    public Task<IReadOnlyList<LabStatDto>> Handle(GetLabStatsQuery request, CancellationToken ct) =>
        _queries.ListAsync(request.From, request.To, _user.Scope, ct);
}

/// <summary>Imports daily lab statistics from an xlsx workbook, upserting by (date, lab code) (SRS FR-13).</summary>
public sealed record ImportLabStatsCommand(byte[] Content) : ICommand<ImportSummary>, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ViewLabStats };
}

public sealed class ImportLabStatsHandler : ICommandHandler<ImportLabStatsCommand, ImportSummary>
{
    private readonly ISpreadsheetReader _reader;
    private readonly IDailyLabStatisticRepository _repository;

    public ImportLabStatsHandler(ISpreadsheetReader reader, IDailyLabStatisticRepository repository)
    {
        _reader = reader; _repository = repository;
    }

    public async Task<ImportSummary> Handle(ImportLabStatsCommand request, CancellationToken ct)
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
                !ImportParsing.TryString(row, "LabCode", out var labCode))
            {
                skipped++;
                warnings.Add($"Row {processed}: missing Date or LabCode.");
                continue;
            }

            var registrations = ImportParsing.Int(row, "Registrations");
            var testCount = ImportParsing.Int(row, "TestCount");
            var income = ImportParsing.Decimal(row, "Income");

            var stat = await _repository.GetAsync(date, labCode, ct);
            if (stat is null)
            {
                stat = DailyLabStatistic.For(date, labCode);
                _repository.Add(stat);
            }
            stat.Set(registrations, testCount, new Money(income));
            upserted++;
        }

        return new ImportSummary(processed, upserted, skipped, warnings);
    }
}

/// <summary>Shared, culture-invariant cell parsing for imports.</summary>
internal static class ImportParsing
{
    public static bool TryString(IReadOnlyDictionary<string, string> row, string key, out string value)
    {
        value = row.TryGetValue(key, out var v) ? v?.Trim() ?? string.Empty : string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }

    public static bool TryDate(IReadOnlyDictionary<string, string> row, string key, out DateOnly date)
    {
        date = default;
        return row.TryGetValue(key, out var v) &&
               DateOnly.TryParse(v, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }

    public static int Int(IReadOnlyDictionary<string, string> row, string key) =>
        row.TryGetValue(key, out var v) && int.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out var n) ? n : 0;

    public static decimal Decimal(IReadOnlyDictionary<string, string> row, string key) =>
        row.TryGetValue(key, out var v) && decimal.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out var n) ? n : 0m;
}
