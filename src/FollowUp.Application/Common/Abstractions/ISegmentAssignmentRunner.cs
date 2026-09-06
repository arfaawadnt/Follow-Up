using FollowUp.Domain.Common;

namespace FollowUp.Application.Common.Abstractions;

/// <summary>Outcome of a monthly segment auto-assignment run.</summary>
/// <param name="Ran">False when the run was skipped (e.g. no segment income bands are configured).</param>
/// <param name="Status">"ok", or a short reason when skipped.</param>
/// <param name="Month">The evaluated calendar month (yyyy-MM) whose income drove the assignment.</param>
/// <param name="LabsEvaluated">Labs considered.</param>
/// <param name="LabsReassigned">Labs whose segment actually changed.</param>
/// <param name="PerSegment">Post-run lab count per segment code (only segments that gained/kept labs this run).</param>
public sealed record SegmentAssignmentResult(
    bool Ran, string Status, string Month, int LabsEvaluated, int LabsReassigned,
    IReadOnlyDictionary<string, int> PerSegment);

/// <summary>
/// Auto-assigns each laboratory to the segment whose monthly target-income band contains the lab's achieved income
/// for a given month (the previous calendar month, for the scheduled run). Achieved income = sum of the detailed
/// registration fees (patient + insurance) for the lab in that month. Implemented in Infrastructure; invoked by the
/// month-start Hangfire job and by the manual "run now" use case.
/// </summary>
public interface ISegmentAssignmentRunner
{
    Task<SegmentAssignmentResult> RunAsync(YearMonth month, bool manual, CancellationToken ct);
}
