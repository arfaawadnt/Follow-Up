namespace FollowUp.Application.Common.Abstractions;

/// <summary>Outcome of an inventory-alert run.</summary>
/// <param name="LowStock">Items at or below their minimum stock (total on hand across all stores).</param>
/// <param name="OutOfStock">Items with nothing on hand although a minimum stock is configured.</param>
/// <param name="Expiring">Lots with stock that expire within the item's warning window.</param>
/// <param name="Expired">Lots with stock already past their expiry date.</param>
/// <param name="Recipients">Users who received the in-app summary (one per user per day).</param>
public sealed record InventoryAlertResult(int LowStock, int OutOfStock, int Expiring, int Expired, int Recipients);

/// <summary>
/// Evaluates the stock limits and expiry dates and pushes one in-app summary notification per day to every active user
/// whose role grants ViewInventory, scoped to the stores their role's Branch scope covers. Implemented in Infrastructure
/// (it reads the stock tables directly); invoked by the daily Hangfire job and by the manual "Check alerts now" use case.
/// </summary>
public interface IInventoryAlertRunner
{
    Task<InventoryAlertResult> RunAsync(DateOnly today, bool manual, CancellationToken ct);
}
