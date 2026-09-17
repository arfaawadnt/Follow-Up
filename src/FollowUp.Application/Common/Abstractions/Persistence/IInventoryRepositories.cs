using FollowUp.Domain.Inventory;

namespace FollowUp.Application.Common.Abstractions.Persistence;

// Inventory module repositories. Reads are async; Add/Remove are void — the DbContext is the unit of work and the
// TransactionBehavior commits (handlers never call SaveChanges). Ledgers (movements, receipts) are append-only.

public interface IManufacturerRepository
{
    Task<Manufacturer?> GetByIdAsync(ManufacturerId id, CancellationToken ct);
    void Add(Manufacturer manufacturer);
}

public interface ISupplierRepository
{
    Task<Supplier?> GetByIdAsync(SupplierId id, CancellationToken ct);
    void Add(Supplier supplier);
}

public interface IStoreRepository
{
    Task<Store?> GetByIdAsync(StoreId id, CancellationToken ct);
    Task<IReadOnlyList<Store>> GetAllAsync(CancellationToken ct);
    void Add(Store store);
}

public interface IInventoryItemRepository
{
    /// <summary>Tracked, with its test links.</summary>
    Task<InventoryItem?> GetByIdAsync(InventoryItemId id, CancellationToken ct);
    Task<IReadOnlyList<InventoryItem>> GetByIdsAsync(IEnumerable<InventoryItemId> ids, CancellationToken ct);
    Task<bool> CodeExistsAsync(string code, InventoryItemId? exceptId, CancellationToken ct);
    void Add(InventoryItem item);
}

public interface IPurchaseOrderRepository
{
    /// <summary>Tracked, with its lines.</summary>
    Task<PurchaseOrder?> GetByIdAsync(PurchaseOrderId id, CancellationToken ct);
    void Add(PurchaseOrder order);
}

public interface IGoodsReceiptRepository
{
    void Add(GoodsReceipt receipt);
}

public interface IStockLotRepository
{
    Task<StockLot?> GetByIdAsync(StockLotId id, CancellationToken ct);
    Task<IReadOnlyList<StockLot>> GetByIdsAsync(IEnumerable<StockLotId> ids, CancellationToken ct);
    /// <summary>The lot of an item in a store by lot number (case-insensitive), if it exists.</summary>
    Task<StockLot?> FindAsync(InventoryItemId itemId, StoreId storeId, string lotNumber, CancellationToken ct);
    void Add(StockLot lot);
}

public interface IStockMovementRepository
{
    void Add(StockMovement movement);
}

public interface IStockTransferRepository
{
    /// <summary>Tracked, with its lines.</summary>
    Task<StockTransfer?> GetByIdAsync(StockTransferId id, CancellationToken ct);
    void Add(StockTransfer transfer);
}
