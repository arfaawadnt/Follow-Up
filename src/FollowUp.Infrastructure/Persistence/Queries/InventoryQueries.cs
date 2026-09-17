using FollowUp.Application.Features.Inventory;
using FollowUp.Domain.Identity;
using FollowUp.Domain.Inventory;
using FollowUp.Domain.Reference;
using Microsoft.EntityFrameworkCore;

namespace FollowUp.Infrastructure.Persistence.Queries;

/// <summary>
/// Read side of the Inventory module. Org scope is pushed into every stock read through the store filter: a store (and
/// everything in it) is visible when the caller's Branches scope is wildcard or covers the store's branch
/// (<see cref="StoreScope"/>). Master data (items, distributors, manufacturers) is global; the on-hand figures shown with
/// it are summed over the visible stores only. Money is a converted scalar, so rows are materialized before any
/// <c>.Amount</c> arithmetic (same rule as the stats and accounting queries).
/// </summary>
internal sealed class InventoryQueries : IInventoryQueries
{
    private readonly FollowUpDbContext _db;
    public InventoryQueries(FollowUpDbContext db) => _db = db;

    // ---- Scope helpers ----

    private async Task<Dictionary<StoreId, Store>> VisibleStoresAsync(OrgScope scope, CancellationToken ct) =>
        (await _db.Stores.AsNoTracking().OrderBy(s => s.Name).ToListAsync(ct))
            .Where(s => StoreScope.IsVisible(scope, s.Branch)).ToDictionary(s => s.Id);

    /// <summary>The store ids a read is limited to: the one requested (empty when it is not visible) or every visible store.</summary>
    private static List<StoreId> Restrict(Dictionary<StoreId, Store> visible, Guid? storeId) =>
        storeId is { } sid ? (visible.ContainsKey(new StoreId(sid)) ? new List<StoreId> { new(sid) } : new List<StoreId>()) : visible.Keys.ToList();

    private async Task<Dictionary<InventoryItemId, InventoryItem>> ItemsByIdAsync(IEnumerable<InventoryItemId> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        return list.Count == 0 ? new() : await _db.InventoryItems.AsNoTracking().Where(i => list.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
    }

    private static string LotStatus(StockLot lot, InventoryItem? item, DateOnly today) =>
        lot.IsExpired(today) ? "Expired" : lot.ExpiresWithin(today, item?.ExpiryWarningDays ?? InventoryItem.DefaultExpiryWarningDays) ? "Expiring" : "Ok";

    // ---- Master data ----

    public async Task<IReadOnlyList<ManufacturerDto>> ManufacturersAsync(bool includeInactive, CancellationToken ct)
    {
        var q = _db.Manufacturers.AsNoTracking();
        if (!includeInactive) q = q.Where(m => m.IsActive);
        var rows = await q.OrderBy(m => m.Name).ToListAsync(ct);
        var counts = await _db.InventoryItems.AsNoTracking().GroupBy(i => i.ManufacturerId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);
        return rows.Select(m => new ManufacturerDto(m.Id.Value, m.Name, m.Country, m.Notes, m.IsActive, counts.GetValueOrDefault(m.Id))).ToList();
    }

    public async Task<IReadOnlyList<SupplierDto>> SuppliersAsync(bool includeInactive, CancellationToken ct)
    {
        var q = _db.Suppliers.AsNoTracking();
        if (!includeInactive) q = q.Where(s => s.IsActive);
        var rows = await q.OrderBy(s => s.Name).ToListAsync(ct);
        var open = new[] { PurchaseOrderStatus.Ordered, PurchaseOrderStatus.PartiallyReceived };
        var openCounts = await _db.PurchaseOrders.AsNoTracking().Where(p => open.Contains(p.Status))
            .GroupBy(p => p.SupplierId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);
        return rows.Select(s => new SupplierDto(s.Id.Value, s.Name, s.ContactPerson, s.Phone, s.Email, s.Address, s.Notes, s.IsActive, openCounts.GetValueOrDefault(s.Id))).ToList();
    }

    public async Task<IReadOnlyList<StoreDto>> StoresAsync(OrgScope scope, bool includeInactive, CancellationToken ct)
    {
        var visible = await VisibleStoresAsync(scope, ct);
        var lots = await _db.StockLots.AsNoTracking().Where(l => l.Quantity > 0).Select(l => new { l.StoreId, l.Quantity, l.UnitCost }).ToListAsync(ct);
        var byStore = lots.GroupBy(l => l.StoreId).ToDictionary(g => g.Key, g => (Count: g.Count(), Value: g.Sum(l => l.UnitCost.Amount * l.Quantity)));
        return visible.Values.Where(s => includeInactive || s.IsActive).OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .Select(s => new StoreDto(s.Id.Value, s.Name, s.Branch, s.Location, s.IsActive,
                byStore.TryGetValue(s.Id, out var v) ? v.Count : 0, byStore.TryGetValue(s.Id, out var v2) ? decimal.Round(v2.Value, 2) : 0m)).ToList();
    }

    private static InventoryItemDto Map(InventoryItem i, string manufacturer, decimal onHand) =>
        new(i.Id.Value, i.Code, i.Name, i.Kind.Name, i.ManufacturerId.Value, manufacturer, i.CatalogNumber, i.Unit, i.MinStock, i.ReorderQuantity, i.ExpiryWarningDays,
            i.StorageConditions, i.Notes, i.IsActive, onHand,
            i.TestLinks.OrderBy(t => t.TestCode).Select(t => new ItemTestLinkDto(t.TestCode, t.TestType, t.TestName, t.QuantityPerTest)).ToList());

    public async Task<IReadOnlyList<InventoryItemDto>> ItemsAsync(OrgScope scope, string? search, ItemKind? kind, bool includeInactive, CancellationToken ct)
    {
        var q = _db.InventoryItems.AsNoTracking();
        if (!includeInactive) q = q.Where(i => i.IsActive);
        if (kind is not null) q = q.Where(i => i.Kind == kind);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            q = q.Where(i => EF.Functions.ILike(i.Code, pattern) || EF.Functions.ILike(i.Name, pattern) || (i.CatalogNumber != null && EF.Functions.ILike(i.CatalogNumber, pattern)));
        }
        var items = await q.OrderBy(i => i.Code).ToListAsync(ct);
        var manufacturers = await _db.Manufacturers.AsNoTracking().ToDictionaryAsync(m => m.Id, m => m.Name, ct);
        var stores = Restrict(await VisibleStoresAsync(scope, ct), null);
        var onHand = await _db.StockLots.AsNoTracking().Where(l => stores.Contains(l.StoreId) && l.Quantity > 0)
            .GroupBy(l => l.ItemId).Select(g => new { g.Key, Q = g.Sum(l => l.Quantity) }).ToDictionaryAsync(x => x.Key, x => x.Q, ct);
        return items.Select(i => Map(i, manufacturers.GetValueOrDefault(i.ManufacturerId, "—"), onHand.GetValueOrDefault(i.Id))).ToList();
    }

    public async Task<InventoryItemDto?> ItemAsync(Guid id, OrgScope scope, CancellationToken ct)
    {
        var item = await _db.InventoryItems.AsNoTracking().FirstOrDefaultAsync(i => i.Id == new InventoryItemId(id), ct);
        if (item is null) return null;
        var manufacturer = await _db.Manufacturers.AsNoTracking().Where(m => m.Id == item.ManufacturerId).Select(m => m.Name).FirstOrDefaultAsync(ct) ?? "—";
        var stores = Restrict(await VisibleStoresAsync(scope, ct), null);
        var onHand = await _db.StockLots.AsNoTracking().Where(l => l.ItemId == item.Id && stores.Contains(l.StoreId)).SumAsync(l => l.Quantity, ct);
        return Map(item, manufacturer, onHand);
    }

    // ---- Stock ----

    private sealed record StockAgg(decimal OnHand, decimal Value, int Lots, decimal Expiring, decimal Expired, DateOnly? Nearest);

    private async Task<(Dictionary<InventoryItemId, InventoryItem> Items, Dictionary<InventoryItemId, StockAgg> Stock)> StockAggregateAsync(
        List<StoreId> stores, DateOnly today, CancellationToken ct)
    {
        var lots = stores.Count == 0 ? new List<StockLot>()
            : await _db.StockLots.AsNoTracking().Where(l => stores.Contains(l.StoreId) && l.Quantity > 0).ToListAsync(ct);
        var active = await _db.InventoryItems.AsNoTracking().Where(i => i.IsActive).ToListAsync(ct);
        var items = active.ToDictionary(i => i.Id);
        // Inactive items that still hold stock stay visible on the stock pages (their quantities are real).
        var missing = lots.Select(l => l.ItemId).Distinct().Where(id => !items.ContainsKey(id)).ToList();
        foreach (var kv in await ItemsByIdAsync(missing, ct)) items[kv.Key] = kv.Value;

        var stock = lots.GroupBy(l => l.ItemId).ToDictionary(g => g.Key, g =>
        {
            var item = items.GetValueOrDefault(g.Key);
            var warn = item?.ExpiryWarningDays ?? InventoryItem.DefaultExpiryWarningDays;
            return new StockAgg(g.Sum(l => l.Quantity), decimal.Round(g.Sum(l => l.UnitCost.Amount * l.Quantity), 2), g.Count(),
                g.Where(l => l.ExpiresWithin(today, warn)).Sum(l => l.Quantity), g.Where(l => l.IsExpired(today)).Sum(l => l.Quantity),
                g.Where(l => l.ExpiryDate != null).Select(l => l.ExpiryDate).Min());
        });
        return (items, stock);
    }

    private static StockRowDto Row(InventoryItem i, string manufacturer, StockAgg? a)
    {
        var onHand = a?.OnHand ?? 0m;
        var isOut = i.MinStock > 0 && onHand <= 0;
        var isLow = i.MinStock > 0 && onHand > 0 && onHand <= i.MinStock;
        return new StockRowDto(i.Id.Value, i.Code, i.Name, i.Kind.Name, i.Unit, manufacturer, i.MinStock, i.ReorderQuantity, onHand, a?.Value ?? 0m, a?.Lots ?? 0,
            a?.Expiring ?? 0m, a?.Expired ?? 0m, a?.Nearest, isLow, isOut);
    }

    public async Task<IReadOnlyList<StockRowDto>> StockAsync(OrgScope scope, Guid? storeId, ItemKind? kind, string? search, bool onlyAlerts, DateOnly today, CancellationToken ct)
    {
        var stores = Restrict(await VisibleStoresAsync(scope, ct), storeId);
        var (items, stock) = await StockAggregateAsync(stores, today, ct);
        var manufacturers = await _db.Manufacturers.AsNoTracking().ToDictionaryAsync(m => m.Id, m => m.Name, ct);
        var term = search?.Trim();
        var rows = items.Values
            .Where(i => kind is null || i.Kind == kind)
            .Where(i => string.IsNullOrEmpty(term) || i.Code.Contains(term, StringComparison.OrdinalIgnoreCase) || i.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
                        || (i.CatalogNumber?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false))
            .Select(i => Row(i, manufacturers.GetValueOrDefault(i.ManufacturerId, "—"), stock.GetValueOrDefault(i.Id)))
            .Where(r => !onlyAlerts || r.IsLow || r.IsOut || r.ExpiringQuantity > 0 || r.ExpiredQuantity > 0)
            .OrderBy(r => r.Code, StringComparer.OrdinalIgnoreCase).ToList();
        return rows;
    }

    public async Task<IReadOnlyList<StockLotDto>> LotsAsync(OrgScope scope, Guid? itemId, Guid? storeId, bool includeEmpty, DateOnly today, CancellationToken ct)
    {
        var visible = await VisibleStoresAsync(scope, ct);
        var stores = Restrict(visible, storeId);
        if (stores.Count == 0) return Array.Empty<StockLotDto>();
        var q = _db.StockLots.AsNoTracking().Where(l => stores.Contains(l.StoreId));
        if (itemId is { } iid) q = q.Where(l => l.ItemId == new InventoryItemId(iid));
        if (!includeEmpty) q = q.Where(l => l.Quantity > 0);
        var lots = await q.ToListAsync(ct);
        var items = await ItemsByIdAsync(lots.Select(l => l.ItemId), ct);
        return lots.Select(l =>
        {
            items.TryGetValue(l.ItemId, out var item); visible.TryGetValue(l.StoreId, out var store);
            return new StockLotDto(l.Id.Value, l.ItemId.Value, item?.Code ?? "—", item?.Name ?? "—", item?.Unit ?? "", l.StoreId.Value, store?.Name ?? "—", l.LotNumber,
                l.ExpiryDate, l.Quantity, l.UnitCost.Amount, decimal.Round(l.UnitCost.Amount * l.Quantity, 2), l.FirstReceivedOn, LotStatus(l, item, today));
        })
        .OrderBy(l => l.ItemCode, StringComparer.OrdinalIgnoreCase).ThenBy(l => l.ExpiryDate ?? DateOnly.MaxValue).ThenBy(l => l.LotNumber).ToList();
    }

    private async Task<List<InventoryAlertDto>> BuildAlertsAsync(List<StoreId> stores, Dictionary<StoreId, Store> visible, DateOnly today, CancellationToken ct)
    {
        var (items, stock) = await StockAggregateAsync(stores, today, ct);
        var alerts = new List<InventoryAlertDto>();
        foreach (var i in items.Values.Where(x => x.IsActive && x.MinStock > 0).OrderBy(x => x.Code))
        {
            var onHand = stock.GetValueOrDefault(i.Id)?.OnHand ?? 0m;
            if (onHand <= 0)
                alerts.Add(new InventoryAlertDto("OutOfStock", i.Id.Value, i.Code, i.Name, i.Unit, null, null, null, null, null, onHand, i.MinStock,
                    $"{i.Code} · {i.Name}: out of stock (limit {i.MinStock:0.###} {i.Unit}{(i.ReorderQuantity > 0 ? $", reorder {i.ReorderQuantity:0.###}" : "")})"));
            else if (onHand <= i.MinStock)
                alerts.Add(new InventoryAlertDto("LowStock", i.Id.Value, i.Code, i.Name, i.Unit, null, null, null, null, null, onHand, i.MinStock,
                    $"{i.Code} · {i.Name}: {onHand:0.###} {i.Unit} on hand, at or below the limit of {i.MinStock:0.###}{(i.ReorderQuantity > 0 ? $" (reorder {i.ReorderQuantity:0.###})" : "")}"));
        }
        if (stores.Count > 0)
        {
            var lots = await _db.StockLots.AsNoTracking().Where(l => stores.Contains(l.StoreId) && l.Quantity > 0 && l.ExpiryDate != null).OrderBy(l => l.ExpiryDate).ToListAsync(ct);
            foreach (var l in lots)
            {
                var item = items.GetValueOrDefault(l.ItemId);
                var status = LotStatus(l, item, today);
                if (status == "Ok") continue;
                visible.TryGetValue(l.StoreId, out var store);
                var code = item?.Code ?? "—"; var name = item?.Name ?? "—"; var unit = item?.Unit ?? "";
                var msg = status == "Expired"
                    ? $"{code} · {name}: lot {l.LotNumber} in {store?.Name ?? "store"} expired on {l.ExpiryDate:dd/MM/yyyy} ({l.Quantity:0.###} {unit} on hand)"
                    : $"{code} · {name}: lot {l.LotNumber} in {store?.Name ?? "store"} expires on {l.ExpiryDate:dd/MM/yyyy} ({l.Quantity:0.###} {unit} on hand)";
                alerts.Add(new InventoryAlertDto(status, l.ItemId.Value, code, name, unit, l.StoreId.Value, store?.Name, l.Id.Value, l.LotNumber, l.ExpiryDate, l.Quantity, null, msg));
            }
        }
        return alerts;
    }

    private static int Severity(string kind) => kind switch { "Expired" => 0, "OutOfStock" => 1, "LowStock" => 2, _ => 3 };

    public async Task<IReadOnlyList<InventoryAlertDto>> AlertsAsync(OrgScope scope, DateOnly today, CancellationToken ct)
    {
        var visible = await VisibleStoresAsync(scope, ct);
        var alerts = await BuildAlertsAsync(visible.Keys.ToList(), visible, today, ct);
        return alerts.OrderBy(a => Severity(a.Kind)).ThenBy(a => a.ExpiryDate ?? DateOnly.MaxValue).ThenBy(a => a.ItemCode).ToList();
    }

    public async Task<InventoryDashboardDto> DashboardAsync(OrgScope scope, DateOnly today, CancellationToken ct)
    {
        var visible = await VisibleStoresAsync(scope, ct);
        var stores = visible.Keys.ToList();
        var alerts = (await BuildAlertsAsync(stores, visible, today, ct)).OrderBy(a => Severity(a.Kind)).ThenBy(a => a.ExpiryDate ?? DateOnly.MaxValue).ThenBy(a => a.ItemCode).ToList();
        var lots = stores.Count == 0 ? new() : await _db.StockLots.AsNoTracking().Where(l => stores.Contains(l.StoreId) && l.Quantity > 0).Select(l => new { l.Quantity, l.UnitCost }).ToListAsync(ct);
        var open = new[] { PurchaseOrderStatus.Ordered, PurchaseOrderStatus.PartiallyReceived };
        var openPos = stores.Count == 0 ? 0 : await _db.PurchaseOrders.AsNoTracking().CountAsync(p => stores.Contains(p.StoreId) && open.Contains(p.Status), ct);
        var inTransit = stores.Count == 0 ? 0 : await _db.StockTransfers.AsNoTracking().CountAsync(t => t.Status == TransferStatus.InTransit && (stores.Contains(t.FromStoreId) || stores.Contains(t.ToStoreId)), ct);
        return new InventoryDashboardDto(
            await _db.InventoryItems.AsNoTracking().CountAsync(i => i.IsActive, ct), visible.Values.Count(s => s.IsActive), lots.Count,
            decimal.Round(lots.Sum(l => l.UnitCost.Amount * l.Quantity), 2),
            alerts.Count(a => a.Kind == "LowStock"), alerts.Count(a => a.Kind == "OutOfStock"), alerts.Count(a => a.Kind == "Expiring"), alerts.Count(a => a.Kind == "Expired"),
            openPos, inTransit, alerts.Take(60).ToList());
    }

    // ---- Purchasing ----

    private async Task<List<PurchaseOrderDto>> MapOrdersAsync(List<PurchaseOrder> orders, Dictionary<StoreId, Store> stores, bool withReceipts, CancellationToken ct)
    {
        if (orders.Count == 0) return new();
        var suppliers = await _db.Suppliers.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        var items = await ItemsByIdAsync(orders.SelectMany(o => o.Lines).Select(l => l.ItemId), ct);
        var receipts = new Dictionary<PurchaseOrderId, List<GoodsReceiptDto>>();
        if (withReceipts)
        {
            var ids = orders.Select(o => o.Id).ToList();
            var grs = await _db.GoodsReceipts.AsNoTracking().Where(g => ids.Contains(g.PurchaseOrderId)).OrderBy(g => g.Serial).ToListAsync(ct);
            var byOrder = orders.ToDictionary(o => o.Id);
            foreach (var g in grs)
            {
                if (!receipts.TryGetValue(g.PurchaseOrderId, out var list)) receipts[g.PurchaseOrderId] = list = new();
                list.Add(MapReceipt(g, byOrder[g.PurchaseOrderId].Number, stores, suppliers, items));
            }
        }
        return orders.Select(o => new PurchaseOrderDto(o.Id.Value, o.Serial, o.Number, o.SupplierId.Value, suppliers.GetValueOrDefault(o.SupplierId, "—"),
            o.StoreId.Value, stores.TryGetValue(o.StoreId, out var st) ? st.Name : "—", o.OrderDate, o.ExpectedDate, o.Status.Name, o.Reference, o.Notes, o.OrderedOn, o.ClosedOn,
            o.Total.Amount, o.Lines.Sum(l => l.OrderedQuantity), o.Lines.Sum(l => l.ReceivedQuantity), o.CreatedBy,
            o.Lines.Select(l =>
            {
                items.TryGetValue(l.ItemId, out var item);
                return new PurchaseOrderLineDto(l.Id.Value, l.ItemId.Value, item?.Code ?? "—", item?.Name ?? "—", item?.Unit ?? "", l.OrderedQuantity, l.UnitPrice.Amount,
                    l.ReceivedQuantity, l.Outstanding, l.LineTotal.Amount, l.Notes);
            }).OrderBy(l => l.ItemCode, StringComparer.OrdinalIgnoreCase).ToList(),
            receipts.GetValueOrDefault(o.Id) ?? new List<GoodsReceiptDto>())).ToList();
    }

    private static GoodsReceiptDto MapReceipt(GoodsReceipt g, string poNumber, Dictionary<StoreId, Store> stores, Dictionary<SupplierId, string> suppliers, Dictionary<InventoryItemId, InventoryItem> items) =>
        new(g.Id.Value, g.Serial, g.Number, g.PurchaseOrderId.Value, poNumber, g.StoreId.Value, stores.TryGetValue(g.StoreId, out var st) ? st.Name : "—",
            suppliers.GetValueOrDefault(g.SupplierId, "—"), g.ReceivedDate, g.DeliveryNote, g.InvoiceNumber, g.Notes, g.CreatedBy,
            g.Lines.Select(l =>
            {
                items.TryGetValue(l.ItemId, out var item);
                return new GoodsReceiptLineDto(l.Id.Value, l.OrderLineId.Value, l.ItemId.Value, item?.Code ?? "—", item?.Name ?? "—", item?.Unit ?? "", l.Quantity, l.LotNumber, l.ExpiryDate, l.UnitCost.Amount);
            }).OrderBy(l => l.ItemCode, StringComparer.OrdinalIgnoreCase).ThenBy(l => l.LotNumber).ToList());

    public async Task<IReadOnlyList<PurchaseOrderDto>> PurchaseOrdersAsync(OrgScope scope, DateOnly from, DateOnly to, PurchaseOrderStatus? status, Guid? supplierId, Guid? storeId, CancellationToken ct)
    {
        var visible = await VisibleStoresAsync(scope, ct);
        var stores = Restrict(visible, storeId);
        if (stores.Count == 0) return Array.Empty<PurchaseOrderDto>();
        var q = _db.PurchaseOrders.AsNoTracking().Where(p => stores.Contains(p.StoreId) && p.OrderDate >= from && p.OrderDate <= to);
        if (status is not null) q = q.Where(p => p.Status == status);
        if (supplierId is { } sup) q = q.Where(p => p.SupplierId == new SupplierId(sup));
        var orders = await q.OrderByDescending(p => p.OrderDate).ThenByDescending(p => p.Serial).ToListAsync(ct);
        return await MapOrdersAsync(orders, visible, withReceipts: false, ct);
    }

    public async Task<PurchaseOrderDto?> PurchaseOrderAsync(Guid id, OrgScope scope, CancellationToken ct)
    {
        var po = await _db.PurchaseOrders.AsNoTracking().FirstOrDefaultAsync(p => p.Id == new PurchaseOrderId(id), ct);
        if (po is null) return null;
        var visible = await VisibleStoresAsync(scope, ct);
        if (!visible.ContainsKey(po.StoreId)) return null; // hides out-of-scope orders
        return (await MapOrdersAsync(new List<PurchaseOrder> { po }, visible, withReceipts: true, ct)).Single();
    }

    public async Task<IReadOnlyList<GoodsReceiptDto>> GoodsReceiptsAsync(OrgScope scope, DateOnly from, DateOnly to, Guid? storeId, CancellationToken ct)
    {
        var visible = await VisibleStoresAsync(scope, ct);
        var stores = Restrict(visible, storeId);
        if (stores.Count == 0) return Array.Empty<GoodsReceiptDto>();
        var grs = await _db.GoodsReceipts.AsNoTracking().Where(g => stores.Contains(g.StoreId) && g.ReceivedDate >= from && g.ReceivedDate <= to)
            .OrderByDescending(g => g.ReceivedDate).ThenByDescending(g => g.Serial).ToListAsync(ct);
        if (grs.Count == 0) return Array.Empty<GoodsReceiptDto>();
        var poIds = grs.Select(g => g.PurchaseOrderId).Distinct().ToList();
        var poNumbers = (await _db.PurchaseOrders.AsNoTracking().Where(p => poIds.Contains(p.Id)).Select(p => new { p.Id, p.Serial }).ToListAsync(ct)).ToDictionary(p => p.Id, p => $"PO-{p.Serial:D5}");
        var suppliers = await _db.Suppliers.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        var items = await ItemsByIdAsync(grs.SelectMany(g => g.Lines).Select(l => l.ItemId), ct);
        return grs.Select(g => MapReceipt(g, poNumbers.GetValueOrDefault(g.PurchaseOrderId, "—"), visible, suppliers, items)).ToList();
    }

    // ---- Movements ----

    public async Task<IReadOnlyList<StockMovementDto>> MovementsAsync(OrgScope scope, DateOnly from, DateOnly to, Guid? storeId, Guid? itemId, StockMovementType? type, CancellationToken ct)
    {
        var visible = await VisibleStoresAsync(scope, ct);
        var stores = Restrict(visible, storeId);
        if (stores.Count == 0) return Array.Empty<StockMovementDto>();
        var q = _db.StockMovements.AsNoTracking().Where(m => stores.Contains(m.StoreId) && m.Date >= from && m.Date <= to);
        if (itemId is { } iid) q = q.Where(m => m.ItemId == new InventoryItemId(iid));
        if (type is not null) q = q.Where(m => m.Type == type);
        var rows = await q.OrderByDescending(m => m.Date).ThenByDescending(m => m.Serial).ToListAsync(ct);
        var items = await ItemsByIdAsync(rows.Select(m => m.ItemId), ct);
        var refIds = rows.Where(m => m.ReferenceId != null).Select(m => m.ReferenceId!.Value).Distinct().ToList();
        var numbers = new Dictionary<Guid, string>();
        if (refIds.Count > 0)
        {
            var grIds = refIds.Select(g => new GoodsReceiptId(g)).ToList();
            foreach (var g in await _db.GoodsReceipts.AsNoTracking().Where(x => grIds.Contains(x.Id)).Select(x => new { x.Id, x.Serial }).ToListAsync(ct)) numbers[g.Id.Value] = $"GR-{g.Serial:D5}";
            var trIds = refIds.Select(g => new StockTransferId(g)).ToList();
            foreach (var t in await _db.StockTransfers.AsNoTracking().Where(x => trIds.Contains(x.Id)).Select(x => new { x.Id, x.Serial }).ToListAsync(ct)) numbers[t.Id.Value] = $"TR-{t.Serial:D5}";
        }
        return rows.Select(m =>
        {
            items.TryGetValue(m.ItemId, out var item); visible.TryGetValue(m.StoreId, out var store);
            return new StockMovementDto(m.Id.Value, m.Serial, m.Date, m.CreatedAt, m.Type.Name, m.ItemId.Value, item?.Code ?? "—", item?.Name ?? "—", item?.Unit ?? "",
                m.StoreId.Value, store?.Name ?? "—", m.LotId.Value, m.LotNumber, m.ExpiryDate, m.Quantity, m.BalanceAfter, m.UnitCost.Amount,
                m.ReferenceKind, m.ReferenceId, m.ReferenceId is { } rid ? numbers.GetValueOrDefault(rid) : null, m.Reason, m.TestCode, m.Notes, m.CreatedBy);
        }).ToList();
    }

    // ---- Transfers ----

    private async Task<List<StockTransferDto>> MapTransfersAsync(List<StockTransfer> transfers, Dictionary<StoreId, Store> visible, CancellationToken ct)
    {
        var allStores = await _db.Stores.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        var items = await ItemsByIdAsync(transfers.SelectMany(t => t.Lines).Select(l => l.ItemId), ct);
        return transfers.Select(t => new StockTransferDto(t.Id.Value, t.Serial, t.Number, t.FromStoreId.Value, allStores.GetValueOrDefault(t.FromStoreId, "—"),
            t.ToStoreId.Value, allStores.GetValueOrDefault(t.ToStoreId, "—"), t.Date, t.Status.Name, t.Notes, t.ReceivedDate, t.ReceiveNotes, t.CreatedBy,
            t.Status == TransferStatus.InTransit && visible.ContainsKey(t.ToStoreId),
            t.Lines.Select(l =>
            {
                items.TryGetValue(l.ItemId, out var item);
                return new StockTransferLineDto(l.Id.Value, l.ItemId.Value, item?.Code ?? "—", item?.Name ?? "—", item?.Unit ?? "", l.SourceLotId.Value, l.LotNumber, l.ExpiryDate,
                    l.Quantity, l.ReceivedQuantity, l.UnitCost.Amount);
            }).OrderBy(l => l.ItemCode, StringComparer.OrdinalIgnoreCase).ThenBy(l => l.LotNumber).ToList())).ToList();
    }

    public async Task<IReadOnlyList<StockTransferDto>> TransfersAsync(OrgScope scope, DateOnly from, DateOnly to, TransferStatus? status, Guid? storeId, CancellationToken ct)
    {
        var visible = await VisibleStoresAsync(scope, ct);
        var stores = Restrict(visible, storeId);
        if (stores.Count == 0) return Array.Empty<StockTransferDto>();
        var q = _db.StockTransfers.AsNoTracking().Where(t => (stores.Contains(t.FromStoreId) || stores.Contains(t.ToStoreId)) && t.Date >= from && t.Date <= to);
        if (status is not null) q = q.Where(t => t.Status == status);
        var rows = await q.OrderByDescending(t => t.Date).ThenByDescending(t => t.Serial).ToListAsync(ct);
        return await MapTransfersAsync(rows, visible, ct);
    }

    public async Task<StockTransferDto?> TransferAsync(Guid id, OrgScope scope, CancellationToken ct)
    {
        var t = await _db.StockTransfers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == new StockTransferId(id), ct);
        if (t is null) return null;
        var visible = await VisibleStoresAsync(scope, ct);
        if (!visible.ContainsKey(t.FromStoreId) && !visible.ContainsKey(t.ToStoreId)) return null;
        return (await MapTransfersAsync(new List<StockTransfer> { t }, visible, ct)).Single();
    }

    // ---- Utilization ----

    public async Task<IReadOnlyList<UtilizationRowDto>> UtilizationAsync(OrgScope scope, DateOnly from, DateOnly to, Guid? storeId, Guid? itemId, CancellationToken ct)
    {
        var visible = await VisibleStoresAsync(scope, ct);
        var stores = Restrict(visible, storeId);
        var itemsQ = _db.InventoryItems.AsNoTracking().Where(i => i.IsActive);
        if (itemId is { } iid) itemsQ = itemsQ.Where(i => i.Id == new InventoryItemId(iid));
        var items = await itemsQ.OrderBy(i => i.Code).ToListAsync(ct);

        // Expected side: the synced test counts of the period. A store narrows them to its branch's registrations when the
        // store's Branch resolves to a Branch reference code (test_statistic.branch carries reg.branch_code); otherwise all.
        List<string>? branchCodes = null;
        if (storeId is { } sid && visible.TryGetValue(new StoreId(sid), out var store))
        {
            var refs = await _db.RefItems.AsNoTracking().Where(r => r.Type == RefType.Branch).Select(r => new { r.Code, r.NameEn, r.RealName }).ToListAsync(ct);
            branchCodes = refs.Where(r => string.Equals(r.NameEn, store.Branch, StringComparison.OrdinalIgnoreCase)
                                          || string.Equals(r.RealName, store.Branch, StringComparison.OrdinalIgnoreCase)
                                          || string.Equals(r.Code, store.Branch, StringComparison.OrdinalIgnoreCase))
                .Select(r => r.Code.Trim().ToUpperInvariant()).Distinct().ToList();
            if (branchCodes.Count == 0) branchCodes = null;
        }
        var codes = items.SelectMany(i => i.TestLinks).Select(l => l.TestCode).Distinct().ToList();
        var statsQ = _db.TestStatistics.AsNoTracking().Where(s => s.Date >= from && s.Date <= to && codes.Contains(s.TestCode));
        if (branchCodes is not null) statsQ = statsQ.Where(s => branchCodes.Contains(s.Branch.ToUpper()));
        var counts = codes.Count == 0 ? new Dictionary<(string, int), int>()
            : (await statsQ.GroupBy(s => new { s.TestCode, s.TestType }).Select(g => new { g.Key.TestCode, g.Key.TestType, N = g.Sum(s => s.Count) }).ToListAsync(ct))
                .ToDictionary(x => (x.TestCode, x.TestType), x => x.N);

        // Actual side: what was issued as consumption from the (visible / selected) stores in the period.
        var consumption = StockMovementType.Consumption;
        var actual = stores.Count == 0 ? new Dictionary<InventoryItemId, decimal>()
            : await _db.StockMovements.AsNoTracking().Where(m => stores.Contains(m.StoreId) && m.Date >= from && m.Date <= to && m.Type == consumption)
                .GroupBy(m => m.ItemId).Select(g => new { g.Key, Q = -g.Sum(m => m.Quantity) }).ToDictionaryAsync(x => x.Key, x => x.Q, ct);

        var rows = new List<UtilizationRowDto>();
        foreach (var i in items)
        {
            var tests = i.TestLinks.OrderBy(l => l.TestCode).Select(l =>
            {
                var n = counts.GetValueOrDefault((l.TestCode, l.TestType));
                return new UtilizationTestDto(l.TestCode, l.TestType, l.TestName, n, l.QuantityPerTest, decimal.Round(n * l.QuantityPerTest, 3));
            }).ToList();
            var expected = tests.Sum(t => t.Expected);
            var used = actual.GetValueOrDefault(i.Id);
            if (tests.Count == 0 && used == 0) continue;
            rows.Add(new UtilizationRowDto(i.Id.Value, i.Code, i.Name, i.Unit, tests.Sum(t => t.TestCount), expected, used, decimal.Round(used - expected, 3),
                used > 0 ? decimal.Round(expected / used * 100m, 1) : null, tests));
        }
        return rows;
    }
}
