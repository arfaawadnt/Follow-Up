using FollowUp.Domain.Identity;
using FollowUp.Domain.Laboratories;

namespace FollowUp.Application.Features.DailyBoard.Contracts;

/// <summary>A row on the daily follow-up board (mirrors the reference platform's board item).</summary>
public sealed record BoardItemDto(
    Guid VisitId,
    Guid LaboratoryId,
    string LabDisplayCode,
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
    string? MarkedAt,
    bool AdminChecked,
    bool TransferDone);

/// <summary>The outcome of a lab-scoped board reconcile — visits added and stale Pending visits pruned.</summary>
public readonly record struct BoardReconciliation(int Added, int Pruned);

/// <summary>
/// Intra-day board scheduling (BR-3). Reconciles the current day's board so a newly onboarded or
/// rescheduled lab is scheduled immediately instead of waiting for the midnight roll-over. Implemented by
/// the board service in Infrastructure.
/// </summary>
public interface IBoardScheduler
{
    /// <summary>Generates any missing visits across today's board (additive, idempotent); returns the number added.</summary>
    Task<int> ReconcileTodayAsync(CancellationToken ct = default);

    /// <summary>
    /// Reconciles a single lab's visits for today against its current schedule: adds visits for newly
    /// scheduled times and prunes still-<c>Pending</c> visits whose time is no longer scheduled (or all of
    /// them when today is no longer a work day / the lab is no longer schedulable). Visits with real activity
    /// (Visited/Missed/Received) are never touched.
    /// </summary>
    Task<BoardReconciliation> ReconcileLabTodayAsync(LaboratoryId laboratoryId, CancellationToken ct = default);
}

/// <summary>Read-side query interface for the daily board (ADR-0005).</summary>
public interface IDailyBoardQueries
{
    Task<IReadOnlyList<BoardItemDto>> GetBoardAsync(
        DateOnly start, DateOnly end, Guid? repId, string? status, OrgScope scope, bool canSeeEncrypted, CancellationToken ct);

    /// <summary>Suggested sample count for a visit (SRS FR-5 suggested-value helper) — e.g. the lab's recent average.</summary>
    Task<int?> GetSuggestedSampleCountAsync(Guid visitId, CancellationToken ct);
}
