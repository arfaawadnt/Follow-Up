using FollowUp.Application.Common.Abstractions.Persistence;
using FollowUp.Domain.Inventory;

namespace FollowUp.Application.Tests.Common;

// In-memory Inventory repositories for handler tests.

public sealed class FakeManufacturerRepository : IManufacturerRepository
{
    public readonly List<Manufacturer> Store = new();
    public Task<Manufacturer?> GetByIdAsync(ManufacturerId id, CancellationToken ct) => Task.FromResult(Store.FirstOrDefault(x => x.Id == id));
    public void Add(Manufacturer manufacturer) => Store.Add(manufacturer);
}

public sealed class FakeSupplierRepository : ISupplierRepository
{
    public readonly List<Supplier> Store = new();
    public Task<Supplier?> GetByIdAsync(SupplierId id, CancellationToken ct) => Task.FromResult(Store.FirstOrDefault(x => x.Id == id));
    public void Add(Supplier supplier) => Store.Add(supplier);
}

public sealed class FakeStoreRepository : IStoreRepository
{
    public readonly List<Store> Store = new();
    public Task<Store?> GetByIdAsync(StoreId id, CancellationToken ct) => Task.FromResult(Store.FirstOrDefault(x => x.Id == id));
    public Task<IReadOnlyList<Store>> GetAllAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<Store>>(Store.ToList());
    public void Add(Store store) => Store.Add(store);
}

public sealed class FakeInventoryItemRepository : IInventoryItemRepository
{
    public readonly List<InventoryItem> Store = new();
    public Task<InventoryItem?> GetByIdAsync(InventoryItemId id, CancellationToken ct) => Task.FromResult(Store.FirstOrDefault(x => x.Id == id));
    public Task<IReadOnlyList<InventoryItem>> GetByIdsAsync(IEnumerable<InventoryItemId> ids, CancellationToken ct)
    { var set = ids.ToHashSet(); return Task.FromResult<IReadOnlyList<InventoryItem>>(Store.Where(x => set.Contains(x.Id)).ToList()); }
    public Task<bool> CodeExistsAsync(string code, InventoryItemId? exceptId, CancellationToken ct) =>
        Task.FromResult(Store.Any(x => x.Code == code && (exceptId is null || x.Id != exceptId.Value)));
    public void Add(InventoryItem item) => Store.Add(item);
}

public sealed class FakePurchaseOrderRepository : IPurchaseOrderRepository
{
    public readonly List<PurchaseOrder> Store = new();
    public Task<PurchaseOrder?> GetByIdAsync(PurchaseOrderId id, CancellationToken ct) => Task.FromResult(Store.FirstOrDefault(x => x.Id == id));
    public void Add(PurchaseOrder order) => Store.Add(order);
}

public sealed class FakeGoodsReceiptRepository : IGoodsReceiptRepository
{
    public readonly List<GoodsReceipt> Store = new();
    public void Add(GoodsReceipt receipt) => Store.Add(receipt);
}

public sealed class FakeStockLotRepository : IStockLotRepository
{
    public readonly List<StockLot> Store = new();
    public Task<StockLot?> GetByIdAsync(StockLotId id, CancellationToken ct) => Task.FromResult(Store.FirstOrDefault(x => x.Id == id));
    public Task<IReadOnlyList<StockLot>> GetByIdsAsync(IEnumerable<StockLotId> ids, CancellationToken ct)
    { var set = ids.ToHashSet(); return Task.FromResult<IReadOnlyList<StockLot>>(Store.Where(x => set.Contains(x.Id)).ToList()); }
    public Task<StockLot?> FindAsync(InventoryItemId itemId, StoreId storeId, string lotNumber, CancellationToken ct) =>
        Task.FromResult(Store.FirstOrDefault(x => x.ItemId == itemId && x.StoreId == storeId && string.Equals(x.LotNumber, lotNumber.Trim(), StringComparison.OrdinalIgnoreCase)));
    public void Add(StockLot lot) => Store.Add(lot);
}

public sealed class FakeStockMovementRepository : IStockMovementRepository
{
    public readonly List<StockMovement> Store = new();
    public void Add(StockMovement movement) => Store.Add(movement);
}

public sealed class FakeStockTransferRepository : IStockTransferRepository
{
    public readonly List<StockTransfer> Store = new();
    public Task<StockTransfer?> GetByIdAsync(StockTransferId id, CancellationToken ct) => Task.FromResult(Store.FirstOrDefault(x => x.Id == id));
    public void Add(StockTransfer transfer) => Store.Add(transfer);
}
