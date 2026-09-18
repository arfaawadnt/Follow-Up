namespace FollowUp.Application.Common.Abstractions;

/// <summary>Outcome of a deductions-automation run.</summary>
/// <param name="Month">The calendar month (yyyy-MM) whose Percentage Deal deductions were recalculated.</param>
/// <param name="Through">The last day whose income the recalculation covered.</param>
/// <param name="DealAreas">Areas with an active Percentage Deal that were considered.</param>
/// <param name="DealCreated">AutoDeal rows created for the month.</param>
/// <param name="DealRecalculated">Existing AutoDeal rows refreshed.</param>
/// <param name="DealSkippedAdjusted">AutoDeal rows left alone because an operator adjusted them.</param>
/// <param name="CollectionsLinked">Cash collections that had no mirroring treasury entry and received one.</param>
/// <param name="CollectionsUnplaced">Cash collections whose lab's branch no active treasury covers (no entry possible).</param>
public sealed record DeductionAutomationResult(
    string Month, DateOnly Through, int DealAreas, int DealCreated, int DealRecalculated, int DealSkippedAdjusted,
    int CollectionsLinked = 0, int CollectionsUnplaced = 0);

/// <summary>
/// Keeps the automated deductions current (operator decisions, 2026-09-15):
/// <list type="bullet">
/// <item>every area with an active Percentage Deal has exactly one AutoDeal deduction for the month of <c>through</c>,
/// valued at the area's income from the 1st through that day × its deal %, unless an operator adjusted it.</item>
/// </list>
/// Implemented in Infrastructure (it reads the synced statistics directly); invoked by the daily Hangfire job with
/// <c>through</c> = the latest synced day, and by the manual "Recalculate now" use case.
/// </summary>
public interface IDeductionAutomationRunner
{
    Task<DeductionAutomationResult> RunAsync(DateOnly through, bool manual, CancellationToken ct);
}
