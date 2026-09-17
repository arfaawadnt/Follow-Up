using FollowUp.Application.Features.Inventory;
using MediatR;

namespace FollowUp.Api.Endpoints;

/// <summary>
/// Inventory module under <c>/api/v1/inventory</c>. Handlers are thin: bind → <c>m.Send</c> → result. Authorization is the
/// usual two layers — the edge auth gate plus each request's <c>RequiredPrivileges</c> (ViewInventory / ManageInventory).
/// </summary>
public static class InventoryEndpoints
{
    public sealed record ManufacturerBody(string Name, string? Country, string? Notes, bool IsActive = true);
    public sealed record SupplierBody(string Name, string? ContactPerson, string? Phone, string? Email, string? Address, string? Notes, bool IsActive = true);
    public sealed record StoreBody(string Name, string Branch, string? Location, bool IsActive = true);
    public sealed record ItemBody(string Code, string Name, string Kind, Guid ManufacturerId, string? CatalogNumber, string Unit, decimal MinStock, decimal ReorderQuantity,
        int ExpiryWarningDays, string? StorageConditions, string? Notes, IReadOnlyList<ItemTestLinkInput>? TestLinks, bool IsActive = true);
    public sealed record PurchaseOrderBody(Guid SupplierId, Guid StoreId, DateOnly OrderDate, DateOnly? ExpectedDate, string? Reference, string? Notes,
        IReadOnlyList<PurchaseOrderLineInput> Lines, bool Submit = false);
    public sealed record ReceiveBody(DateOnly ReceivedDate, string? DeliveryNote, string? InvoiceNumber, string? Notes, IReadOnlyList<ReceiptLineInput> Lines);
    public sealed record IssueBody(Guid LotId, DateOnly Date, decimal Quantity, string Reason, string? TestCode, string? Notes);
    public sealed record AdjustBody(Guid LotId, DateOnly Date, decimal CountedQuantity, string Reason, string? Notes);
    public sealed record TransferBody(Guid FromStoreId, Guid ToStoreId, DateOnly Date, string? Notes, IReadOnlyList<TransferLineInput> Lines);
    public sealed record TransferReceiveBody(DateOnly ReceivedDate, IReadOnlyList<TransferReceiveInput>? Lines, string? Notes);
    public sealed record NotesBody(string? Notes);

    public static void MapInventoryEndpoints(this RouteGroupBuilder api)
    {
        const string tag = "Inventory";

        // ---- Master data ----
        api.MapGet("/inventory/manufacturers", async (bool? includeInactive, IMediator m, CancellationToken ct) =>
            Results.Ok(await m.Send(new GetManufacturersQuery(includeInactive ?? false), ct))).WithTags(tag);
        api.MapPost("/inventory/manufacturers", async (ManufacturerBody b, IMediator m, CancellationToken ct) =>
        { var id = await m.Send(new CreateManufacturerCommand(b.Name, b.Country, b.Notes), ct); return Results.Created($"/api/v1/inventory/manufacturers/{id}", new { id }); }).WithTags(tag);
        api.MapPut("/inventory/manufacturers/{id:guid}", async (Guid id, ManufacturerBody b, IMediator m, CancellationToken ct) =>
        { await m.Send(new UpdateManufacturerCommand(id, b.Name, b.Country, b.Notes, b.IsActive), ct); return Results.NoContent(); }).WithTags(tag);

        api.MapGet("/inventory/suppliers", async (bool? includeInactive, IMediator m, CancellationToken ct) =>
            Results.Ok(await m.Send(new GetSuppliersQuery(includeInactive ?? false), ct))).WithTags(tag);
        api.MapPost("/inventory/suppliers", async (SupplierBody b, IMediator m, CancellationToken ct) =>
        { var id = await m.Send(new CreateSupplierCommand(b.Name, b.ContactPerson, b.Phone, b.Email, b.Address, b.Notes), ct); return Results.Created($"/api/v1/inventory/suppliers/{id}", new { id }); }).WithTags(tag);
        api.MapPut("/inventory/suppliers/{id:guid}", async (Guid id, SupplierBody b, IMediator m, CancellationToken ct) =>
        { await m.Send(new UpdateSupplierCommand(id, b.Name, b.ContactPerson, b.Phone, b.Email, b.Address, b.Notes, b.IsActive), ct); return Results.NoContent(); }).WithTags(tag);

        api.MapGet("/inventory/stores", async (bool? includeInactive, IMediator m, CancellationToken ct) =>
            Results.Ok(await m.Send(new GetStoresQuery(includeInactive ?? false), ct))).WithTags(tag);
        api.MapPost("/inventory/stores", async (StoreBody b, IMediator m, CancellationToken ct) =>
        { var id = await m.Send(new CreateStoreCommand(b.Name, b.Branch, b.Location), ct); return Results.Created($"/api/v1/inventory/stores/{id}", new { id }); }).WithTags(tag);
        api.MapPut("/inventory/stores/{id:guid}", async (Guid id, StoreBody b, IMediator m, CancellationToken ct) =>
        { await m.Send(new UpdateStoreCommand(id, b.Name, b.Branch, b.Location, b.IsActive), ct); return Results.NoContent(); }).WithTags(tag);

        api.MapGet("/inventory/items", async (string? search, string? kind, bool? includeInactive, IMediator m, CancellationToken ct) =>
            Results.Ok(await m.Send(new GetInventoryItemsQuery(search, kind, includeInactive ?? false), ct))).WithTags(tag);
        api.MapGet("/inventory/items/{id:guid}", async (Guid id, IMediator m, CancellationToken ct) =>
            Results.Ok(await m.Send(new GetInventoryItemQuery(id), ct))).WithTags(tag);
        api.MapPost("/inventory/items", async (ItemBody b, IMediator m, CancellationToken ct) =>
        {
            var id = await m.Send(new CreateInventoryItemCommand(b.Code, b.Name, b.Kind, b.ManufacturerId, b.CatalogNumber, b.Unit, b.MinStock, b.ReorderQuantity,
                b.ExpiryWarningDays, b.StorageConditions, b.Notes, b.TestLinks), ct);
            return Results.Created($"/api/v1/inventory/items/{id}", new { id });
        }).WithTags(tag);
        api.MapPut("/inventory/items/{id:guid}", async (Guid id, ItemBody b, IMediator m, CancellationToken ct) =>
        {
            await m.Send(new UpdateInventoryItemCommand(id, b.Name, b.Kind, b.ManufacturerId, b.CatalogNumber, b.Unit, b.MinStock, b.ReorderQuantity,
                b.ExpiryWarningDays, b.StorageConditions, b.Notes, b.IsActive, b.TestLinks), ct);
            return Results.NoContent();
        }).WithTags(tag);

        // ---- Stock, lots, alerts, dashboard ----
        api.MapGet("/inventory/dashboard", async (IMediator m, CancellationToken ct) =>
            Results.Ok(await m.Send(new GetInventoryDashboardQuery(), ct))).WithTags(tag);
        api.MapGet("/inventory/stock", async (Guid? storeId, string? kind, string? search, bool? onlyAlerts, IMediator m, CancellationToken ct) =>
            Results.Ok(await m.Send(new GetStockQuery(storeId, kind, search, onlyAlerts ?? false), ct))).WithTags(tag);
        api.MapGet("/inventory/lots", async (Guid? itemId, Guid? storeId, bool? includeEmpty, IMediator m, CancellationToken ct) =>
            Results.Ok(await m.Send(new GetStockLotsQuery(itemId, storeId, includeEmpty ?? false), ct))).WithTags(tag);
        api.MapGet("/inventory/alerts", async (IMediator m, CancellationToken ct) =>
            Results.Ok(await m.Send(new GetInventoryAlertsQuery(), ct))).WithTags(tag);
        // Runs the stock-limit / expiry evaluation now and pushes the in-app summary (same pass as the 06:00 job).
        api.MapPost("/inventory/alerts/run", async (IMediator m, CancellationToken ct) =>
            Results.Ok(await m.Send(new RunInventoryAlertsCommand(), ct))).WithTags(tag);

        // ---- Purchase orders + goods receipts ----
        api.MapGet("/inventory/purchase-orders", async (DateOnly from, DateOnly to, string? status, Guid? supplierId, Guid? storeId, IMediator m, CancellationToken ct) =>
            Results.Ok(await m.Send(new GetPurchaseOrdersQuery(from, to, status, supplierId, storeId), ct))).WithTags(tag);
        api.MapGet("/inventory/purchase-orders/{id:guid}", async (Guid id, IMediator m, CancellationToken ct) =>
            Results.Ok(await m.Send(new GetPurchaseOrderQuery(id), ct))).WithTags(tag);
        api.MapPost("/inventory/purchase-orders", async (PurchaseOrderBody b, IMediator m, CancellationToken ct) =>
        { var id = await m.Send(new CreatePurchaseOrderCommand(b.SupplierId, b.StoreId, b.OrderDate, b.ExpectedDate, b.Reference, b.Notes, b.Lines, b.Submit), ct); return Results.Created($"/api/v1/inventory/purchase-orders/{id}", new { id }); }).WithTags(tag);
        api.MapPut("/inventory/purchase-orders/{id:guid}", async (Guid id, PurchaseOrderBody b, IMediator m, CancellationToken ct) =>
        { await m.Send(new UpdatePurchaseOrderCommand(id, b.SupplierId, b.StoreId, b.OrderDate, b.ExpectedDate, b.Reference, b.Notes, b.Lines), ct); return Results.NoContent(); }).WithTags(tag);
        // Status transitions: submit (Draft → Ordered), close (accept a short delivery), cancel (nothing received yet).
        api.MapPost("/inventory/purchase-orders/{id:guid}/submit", async (Guid id, IMediator m, CancellationToken ct) =>
        { await m.Send(new ChangePurchaseOrderStatusCommand(id, ChangePurchaseOrderStatusCommand.Submit), ct); return Results.NoContent(); }).WithTags(tag);
        api.MapPost("/inventory/purchase-orders/{id:guid}/close", async (Guid id, IMediator m, CancellationToken ct) =>
        { await m.Send(new ChangePurchaseOrderStatusCommand(id, ChangePurchaseOrderStatusCommand.Close), ct); return Results.NoContent(); }).WithTags(tag);
        api.MapPost("/inventory/purchase-orders/{id:guid}/cancel", async (Guid id, IMediator m, CancellationToken ct) =>
        { await m.Send(new ChangePurchaseOrderStatusCommand(id, ChangePurchaseOrderStatusCommand.Cancel), ct); return Results.NoContent(); }).WithTags(tag);
        // Receiving: validates the counts against the order and lands every line in its lot (lot number + expiry).
        api.MapPost("/inventory/purchase-orders/{id:guid}/receive", async (Guid id, ReceiveBody b, IMediator m, CancellationToken ct) =>
        { var receiptId = await m.Send(new ReceivePurchaseOrderCommand(id, b.ReceivedDate, b.DeliveryNote, b.InvoiceNumber, b.Notes, b.Lines), ct); return Results.Created($"/api/v1/inventory/receipts/{receiptId}", new { id = receiptId }); }).WithTags(tag);
        api.MapGet("/inventory/receipts", async (DateOnly from, DateOnly to, Guid? storeId, IMediator m, CancellationToken ct) =>
            Results.Ok(await m.Send(new GetGoodsReceiptsQuery(from, to, storeId), ct))).WithTags(tag);

        // ---- Movements (ledger) + manual issue / adjustment ----
        api.MapGet("/inventory/movements", async (DateOnly from, DateOnly to, Guid? storeId, Guid? itemId, string? type, IMediator m, CancellationToken ct) =>
            Results.Ok(await m.Send(new GetStockMovementsQuery(from, to, storeId, itemId, type), ct))).WithTags(tag);
        api.MapPost("/inventory/issues", async (IssueBody b, IMediator m, CancellationToken ct) =>
        { var id = await m.Send(new IssueStockCommand(b.LotId, b.Date, b.Quantity, b.Reason, b.TestCode, b.Notes), ct); return Results.Created($"/api/v1/inventory/movements/{id}", new { id }); }).WithTags(tag);
        api.MapPost("/inventory/adjustments", async (AdjustBody b, IMediator m, CancellationToken ct) =>
        { var id = await m.Send(new AdjustStockCommand(b.LotId, b.Date, b.CountedQuantity, b.Reason, b.Notes), ct); return Results.Created($"/api/v1/inventory/movements/{id}", new { id }); }).WithTags(tag);

        // ---- Transfers ----
        api.MapGet("/inventory/transfers", async (DateOnly from, DateOnly to, string? status, Guid? storeId, IMediator m, CancellationToken ct) =>
            Results.Ok(await m.Send(new GetStockTransfersQuery(from, to, status, storeId), ct))).WithTags(tag);
        api.MapGet("/inventory/transfers/{id:guid}", async (Guid id, IMediator m, CancellationToken ct) =>
            Results.Ok(await m.Send(new GetStockTransferQuery(id), ct))).WithTags(tag);
        api.MapPost("/inventory/transfers", async (TransferBody b, IMediator m, CancellationToken ct) =>
        { var id = await m.Send(new CreateStockTransferCommand(b.FromStoreId, b.ToStoreId, b.Date, b.Notes, b.Lines), ct); return Results.Created($"/api/v1/inventory/transfers/{id}", new { id }); }).WithTags(tag);
        api.MapPost("/inventory/transfers/{id:guid}/receive", async (Guid id, TransferReceiveBody b, IMediator m, CancellationToken ct) =>
        { await m.Send(new ReceiveStockTransferCommand(id, b.ReceivedDate, b.Lines, b.Notes), ct); return Results.NoContent(); }).WithTags(tag);
        api.MapPost("/inventory/transfers/{id:guid}/cancel", async (Guid id, NotesBody b, IMediator m, CancellationToken ct) =>
        { await m.Send(new CancelStockTransferCommand(id, b.Notes), ct); return Results.NoContent(); }).WithTags(tag);

        // ---- Utilization (expected consumption from test counts vs actual issues) ----
        api.MapGet("/inventory/utilization", async (DateOnly from, DateOnly to, Guid? storeId, Guid? itemId, IMediator m, CancellationToken ct) =>
            Results.Ok(await m.Send(new GetUtilizationQuery(from, to, storeId, itemId), ct))).WithTags(tag);
    }
}
