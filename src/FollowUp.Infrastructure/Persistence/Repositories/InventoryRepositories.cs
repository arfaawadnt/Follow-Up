using FollowUp.Application.Common.Abstractions.Persistence;
using FollowUp.Domain.Inventory;
using Microsoft.EntityFrameworkCore;

namespace FollowUp.Infrastructure.Persistence.Repositories;

// Inventory module repositories: tracked reads for the write side (owned line collections load with their document —
// EF includes owned types automatically), append-only ledgers.

internal sealed class ManufacturerRepository : IManufacturerRepository
{
    private readonly FollowUpDbContext _db;
    public ManufacturerRepository(FollowUpDbContext db) => _db = db;
    public Task<Manufacturer?> GetByIdAsync(ManufacturerId id, CancellationToken ct) => _db.Manufacturers.FirstOrDefaultAsync(x => x.Id == id, ct);
    public void Add(Manufacturer manufacturer) => _db.Manufacturers.Add(manufacturer);
}

internal sealed class SupplierRepository : ISupplierRepository
{
    private readonly FollowUpDbContext _db;
    public SupplierRepository(FollowUpDbContext db) => _db = db;
    public Task<Supplier?> GetByIdAsync(SupplierId id, CancellationToken ct) => _db.Suppliers.FirstOrDefaultAsync(x => x.Id == id, ct);
    public void Add(Supplier supplier) => _db.Suppliers.Add(supplier);
}

internal sealed class StoreRepository : IStoreRepository
{
    private readonly FollowUpDbContext _db;
    public StoreRepository(FollowUpDbContext db) => _db = db;
    public Task<Store?> GetByIdAsync(StoreId id, CancellationToken ct) => _db.Stores.FirstOrDefaultAsync(x => x.Id == id, ct);
    public async Task<IReadOnlyList<Store>> GetAllAsync(CancellationToken ct) => await _db.Stores.OrderBy(x => x.Name).ToListAsync(ct);
    public void Add(Store store) => _db.Stores.Add(store);
}

internal sealed class InventoryItemRepository : IInventoryItemRepository
{
    private readonly FollowUpDbContext _db;
    public InventoryItemRepository(FollowUpDbContext db) => _db = db;
    public Task<InventoryItem?> GetByIdAsync(InventoryItemId id, CancellationToken ct) => _db.InventoryItems.FirstOrDefaultAsync(x => x.Id == id, ct);
    public async Task<IReadOnlyList<InventoryItem>> GetByIdsAsync(IEnumerable<InventoryItemId> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        return list.Count == 0 ? Array.Empty<InventoryItem>() : await _db.InventoryItems.Where(x => list.Contains(x.Id)).ToListAsync(ct);
    }
    public Task<bool> CodeExistsAsync(string code, InventoryItemId? exceptId, CancellationToken ct) =>
        _db.InventoryItems.AnyAsync(x => x.Code == code && (exceptId == null || x.Id != exceptId.Value), ct);
    public void Add(InventoryItem item) => _db.InventoryItems.Add(item);
}

internal sealed class PurchaseOrderRepository : IPurchaseOrderRepository
{
    private readonly FollowUpDbContext _db;
    public PurchaseOrderRepository(FollowUpDbContext db) => _db = db;
    public Task<PurchaseOrder?> GetByIdAsync(PurchaseOrderId id, CancellationToken ct) => _db.PurchaseOrders.FirstOrDefaultAsync(x => x.Id == id, ct);
    public void Add(PurchaseOrder order) => _db.PurchaseOrders.Add(order);
}

internal sealed class GoodsReceiptRepository : IGoodsReceiptRepository
{
    private readonly FollowUpDbContext _db;
    public GoodsReceiptRepository(FollowUpDbContext db) => _db = db;
    public void Add(GoodsReceipt receipt) => _db.GoodsReceipts.Add(receipt);
}

internal sealed class StockLotRepository : IStockLotRepository
{
    private readonly FollowUpDbContext _db;
    public StockLotRepository(FollowUpDbContext db) => _db = db;
    public Task<StockLot?> GetByIdAsync(StockLotId id, CancellationToken ct) => _db.StockLots.FirstOrDefaultAsync(x => x.Id == id, ct);
    public async Task<IReadOnlyList<StockLot>> GetByIdsAsync(IEnumerable<StockLotId> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        return list.Count == 0 ? Array.Empty<StockLot>() : await _db.StockLots.Where(x => list.Contains(x.Id)).ToListAsync(ct);
    }
    public Task<StockLot?> FindAsync(InventoryItemId itemId, StoreId storeId, string lotNumber, CancellationToken ct)
    {
        var lot = lotNumber.Trim().ToUpper();
        return _db.StockLots.FirstOrDefaultAsync(x => x.ItemId == itemId && x.StoreId == storeId && x.LotNumber.ToUpper() == lot, ct);
    }
    public void Add(StockLot lot) => _db.StockLots.Add(lot);
}

internal sealed class StockMovementRepository : IStockMovementRepository
{
    private readonly FollowUpDbContext _db;
    public StockMovementRepository(FollowUpDbContext db) => _db = db;
    public void Add(StockMovement movement) => _db.StockMovements.Add(movement);
}

internal sealed class StockTransferRepository : IStockTransferRepository
{
    private readonly FollowUpDbContext _db;
    public StockTransferRepository(FollowUpDbContext db) => _db = db;
    public Task<StockTransfer?> GetByIdAsync(StockTransferId id, CancellationToken ct) => _db.StockTransfers.FirstOrDefaultAsync(x => x.Id == id, ct);
    public void Add(StockTransfer transfer) => _db.StockTransfers.Add(transfer);
}
