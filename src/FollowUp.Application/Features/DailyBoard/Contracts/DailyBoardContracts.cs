using FollowUp.Domain.Identity;

namespace FollowUp.Application.Features.DailyBoard.Contracts;

/// <summary>A row on the daily follow-up board.</summary>
public sealed record BoardItemDto(
    Guid VisitId,
    Guid LaboratoryId,
    string LabDisplayCode,
    string LabName,
    Guid? CollectorRepId,
    DateOnly VisitDate,
    string ScheduledTime,
    string Status,
    int? SampleCount,
    bool AdminChecked);

/// <summary>Read-side query interface for the daily board (ADR-0005).</summary>
public interface IDailyBoardQueries
{
    Task<IReadOnlyList<BoardItemDto>> GetBoardAsync(DateOnly date, OrgScope scope, bool canSeeEncrypted, CancellationToken ct);

    /// <summary>Suggested sample count for a visit (SRS FR-5 suggested-value helper) — e.g. the lab's recent average.</summary>
    Task<int?> GetSuggestedSampleCountAsync(Guid visitId, CancellationToken ct);
}
