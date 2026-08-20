using FollowUp.Domain.Identity;

namespace FollowUp.Application.Features.DailyBoard.Contracts;

/// <summary>A row on the daily follow-up board (mirrors the reference platform's board item).</summary>
public sealed record BoardItemDto(
    Guid VisitId,
    Guid LaboratoryId,
    string LabDisplayCode,
    string LabCode,
    string Lab,
    Guid? CollectorRepId,
    string? Rep,
    string? Branch,
    string? Governorate,
    string? City,
    string? Area,
    DateOnly VisitDate,
    string ScheduledTime,
    string Status,
    int? Samples,
    bool AdminChecked,
    bool TransferDone);

/// <summary>Read-side query interface for the daily board (ADR-0005).</summary>
public interface IDailyBoardQueries
{
    Task<IReadOnlyList<BoardItemDto>> GetBoardAsync(
        DateOnly start, DateOnly end, Guid? repId, string? status, OrgScope scope, bool canSeeEncrypted, CancellationToken ct);

    /// <summary>Suggested sample count for a visit (SRS FR-5 suggested-value helper) — e.g. the lab's recent average.</summary>
    Task<int?> GetSuggestedSampleCountAsync(Guid visitId, CancellationToken ct);
}
