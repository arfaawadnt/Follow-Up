using FollowUp.Domain.Common;

namespace FollowUp.Domain.Inventory;

// =====================================================================================================================
// Inventory module — chemicals and consumables from purchase order to consumption. Master data (manufacturers,
// distributors, stores, items with their test links) plus four ledgers: purchase orders and their goods receipts,
// stock lots (one row per item × store × lot number, carrying the expiry date), the immutable stock-movement ledger
// (every quantity change, signed) and store-to-store transfers. Quantities are decimal(18,3) in the item's own unit;
// prices are Money. Stores carry a Branch — the org-scope dimension every stock row is scoped on.
// =====================================================================================================================

// ---- Enumerations (persisted by stable Name) ----

/// <summary>What kind of stock an item is — reporting grouping only, no behaviour differs.</summary>
public sealed class ItemKind : Enumeration
{
    public static readonly ItemKind Chemical = new(1, nameof(Chemical));
    public static readonly ItemKind Consumable = new(2, nameof(Consumable));
    public static readonly ItemKind Kit = new(3, nameof(Kit));
    public static readonly ItemKind Control = new(4, nameof(Control));
    public static readonly ItemKind Other = new(5, nameof(Other));
    private ItemKind(int id, string name) : base(id, name) { }
}

/// <summary>Life cycle of a purchase order: Draft (editable) → Ordered (sent to the distributor) → PartiallyReceived /
/// Received as goods arrive; Closed accepts a short delivery; Cancelled only while nothing has been received.</summary>
public sealed class PurchaseOrderStatus : Enumeration
{
    public static readonly PurchaseOrderStatus Draft = new(1, nameof(Draft));
    public static readonly PurchaseOrderStatus Ordered = new(2, nameof(Ordered));
    public static readonly PurchaseOrderStatus PartiallyReceived = new(3, nameof(PartiallyReceived));
    public static readonly PurchaseOrderStatus Received = new(4, nameof(Received));
    public static readonly PurchaseOrderStatus Closed = new(5, nameof(Closed));
    public static readonly PurchaseOrderStatus Cancelled = new(6, nameof(Cancelled));
    private PurchaseOrderStatus(int id, string name) : base(id, name) { }
}

/// <summary>A transfer leaves the source store the moment it is created (InTransit) and lands in the destination when
/// the receiving store confirms the counts (Received). Cancelling an in-transit transfer returns the stock to the source.</summary>
public sealed class TransferStatus : Enumeration
{
    public static readonly TransferStatus InTransit = new(1, nameof(InTransit));
    public static readonly TransferStatus Received = new(2, nameof(Received));
    public static readonly TransferStatus Cancelled = new(3, nameof(Cancelled));
    private TransferStatus(int id, string name) : base(id, name) { }
}

/// <summary>Every quantity change in a store is one movement of one of these types. The sign of the quantity is fixed
/// by the type (Receipt / TransferIn add, Consumption / Disposal / TransferOut / ReturnToSupplier remove, Adjustment
/// goes either way) — the DB CHECK mirrors <see cref="StockMovement"/>'s factories.</summary>
public sealed class StockMovementType : Enumeration
{
    public static readonly StockMovementType Receipt = new(1, nameof(Receipt));
    public static readonly StockMovementType Consumption = new(2, nameof(Consumption));
    public static readonly StockMovementType Disposal = new(3, nameof(Disposal));
    public static readonly StockMovementType TransferOut = new(4, nameof(TransferOut));
    public static readonly StockMovementType TransferIn = new(5, nameof(TransferIn));
    public static readonly StockMovementType Adjustment = new(6, nameof(Adjustment));
    public static readonly StockMovementType ReturnToSupplier = new(7, nameof(ReturnToSupplier));
    private StockMovementType(int id, string name) : base(id, name) { }

    public bool AddsStock => this == Receipt || this == TransferIn;
    public bool RemovesStock => this == Consumption || this == Disposal || this == TransferOut || this == ReturnToSupplier;
}

/// <summary>Why stock is issued by hand (the movement type follows: Consumption → Consumption, Damaged / Expired →
/// Disposal, ReturnToSupplier → ReturnToSupplier, Other → Consumption with the note).</summary>
public sealed class IssueReason : Enumeration
{
    public static readonly IssueReason Consumption = new(1, nameof(Consumption));
    public static readonly IssueReason Damaged = new(2, nameof(Damaged));
    public static readonly IssueReason Expired = new(3, nameof(Expired));
    public static readonly IssueReason ReturnToSupplier = new(4, nameof(ReturnToSupplier));
    public static readonly IssueReason Other = new(5, nameof(Other));
    private IssueReason(int id, string name) : base(id, name) { }

    public StockMovementType MovementType =>
        this == Damaged || this == Expired ? StockMovementType.Disposal
        : this == ReturnToSupplier ? StockMovementType.ReturnToSupplier
        : StockMovementType.Consumption;
}

// ---- Strongly-typed ids ----

public readonly record struct ManufacturerId(Guid Value) { public static ManufacturerId New() => new(Guid.NewGuid()); public override string ToString() => Value.ToString(); }
public readonly record struct SupplierId(Guid Value) { public static SupplierId New() => new(Guid.NewGuid()); public override string ToString() => Value.ToString(); }
public readonly record struct StoreId(Guid Value) { public static StoreId New() => new(Guid.NewGuid()); public override string ToString() => Value.ToString(); }
public readonly record struct InventoryItemId(Guid Value) { public static InventoryItemId New() => new(Guid.NewGuid()); public override string ToString() => Value.ToString(); }
public readonly record struct ItemTestLinkId(Guid Value) { public static ItemTestLinkId New() => new(Guid.NewGuid()); public override string ToString() => Value.ToString(); }
public readonly record struct PurchaseOrderId(Guid Value) { public static PurchaseOrderId New() => new(Guid.NewGuid()); public override string ToString() => Value.ToString(); }
public readonly record struct PurchaseOrderLineId(Guid Value) { public static PurchaseOrderLineId New() => new(Guid.NewGuid()); public override string ToString() => Value.ToString(); }
public readonly record struct GoodsReceiptId(Guid Value) { public static GoodsReceiptId New() => new(Guid.NewGuid()); public override string ToString() => Value.ToString(); }
public readonly record struct GoodsReceiptLineId(Guid Value) { public static GoodsReceiptLineId New() => new(Guid.NewGuid()); public override string ToString() => Value.ToString(); }
public readonly record struct StockLotId(Guid Value) { public static StockLotId New() => new(Guid.NewGuid()); public override string ToString() => Value.ToString(); }
public readonly record struct StockMovementId(Guid Value) { public static StockMovementId New() => new(Guid.NewGuid()); public override string ToString() => Value.ToString(); }
public readonly record struct StockTransferId(Guid Value) { public static StockTransferId New() => new(Guid.NewGuid()); public override string ToString() => Value.ToString(); }
public readonly record struct StockTransferLineId(Guid Value) { public static StockTransferLineId New() => new(Guid.NewGuid()); public override string ToString() => Value.ToString(); }

// ---- Shared guards ----

internal static class InventoryGuards
{
    public static string Required(string? value, string field, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new DomainException($"{field} is required.");
        var v = value.Trim();
        if (v.Length > max) throw new DomainException($"{field} must be at most {max} characters.");
        return v;
    }

    public static string? Optional(string? value, int max, string field = "Text")
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var v = value.Trim();
        if (v.Length > max) throw new DomainException($"{field} must be at most {max} characters.");
        return v;
    }

    /// <summary>Quantities are kept at three decimals (the unit may be mL, g or pieces).</summary>
    public static decimal Quantity(decimal value, string field, bool allowZero = false)
    {
        var q = decimal.Round(value, 3, MidpointRounding.ToEven);
        if (q < 0 || (!allowZero && q == 0)) throw new DomainException(allowZero ? $"{field} cannot be negative." : $"{field} must be greater than zero.");
        return q;
    }

    public static Money NonNegative(decimal amount, string field)
    {
        if (amount < 0) throw new DomainException($"{field} cannot be negative.");
        return new Money(amount);
    }

    public static string TestCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) throw new DomainException("Test code is required.");
        var c = code.Trim().ToUpperInvariant();
        if (c.Length > 32) throw new DomainException("Test code must be at most 32 characters.");
        return c;
    }
}

// ---- Master data ----

/// <summary>The company that makes an item (every item names its manufacturer). Deactivated, never hard-deleted.</summary>
public sealed class Manufacturer : AggregateRoot<ManufacturerId>, IAuditable
{
    private Manufacturer() { } // EF
    private Manufacturer(ManufacturerId id) : base(id) { }

    public string Name { get; private set; } = null!;
    public string? Country { get; private set; }
    public string? Notes { get; private set; }
    public bool IsActive { get; private set; } = true;

    public DateTimeOffset CreatedAt { get; private set; }
    public string CreatedBy { get; private set; } = null!;
    public DateTimeOffset? UpdatedAt { get; private set; }
    public string? UpdatedBy { get; private set; }

    public static Manufacturer Create(string name, string? country, string? notes)
    {
        var m = new Manufacturer(ManufacturerId.New());
        m.Update(name, country, notes);
        return m;
    }

    public void Update(string name, string? country, string? notes)
    {
        Name = InventoryGuards.Required(name, "Manufacturer name", 150);
        Country = InventoryGuards.Optional(country, 80, "Country");
        Notes = InventoryGuards.Optional(notes, 500, "Notes");
    }

    public void Activate(bool active) => IsActive = active;
}

/// <summary>A distributor purchase orders are placed with. Deactivated, never hard-deleted (orders keep resolving).</summary>
public sealed class Supplier : AggregateRoot<SupplierId>, IAuditable
{
    private Supplier() { } // EF
    private Supplier(SupplierId id) : base(id) { }

    public string Name { get; private set; } = null!;
    public string? ContactPerson { get; private set; }
    public string? Phone { get; private set; }
    public string? Email { get; private set; }
    public string? Address { get; private set; }
    public string? Notes { get; private set; }
    public bool IsActive { get; private set; } = true;

    public DateTimeOffset CreatedAt { get; private set; }
    public string CreatedBy { get; private set; } = null!;
    public DateTimeOffset? UpdatedAt { get; private set; }
    public string? UpdatedBy { get; private set; }

    public static Supplier Create(string name, string? contactPerson, string? phone, string? email, string? address, string? notes)
    {
        var s = new Supplier(SupplierId.New());
        s.Update(name, contactPerson, phone, email, address, notes);
        return s;
    }

    public void Update(string name, string? contactPerson, string? phone, string? email, string? address, string? notes)
    {
        Name = InventoryGuards.Required(name, "Distributor name", 150);
        ContactPerson = InventoryGuards.Optional(contactPerson, 120, "Contact person");
        Phone = InventoryGuards.Optional(phone, 40, "Phone");
        Email = InventoryGuards.Optional(email, 150, "Email");
        if (Email is not null && !Email.Contains('@')) throw new DomainException("Email is not valid.");
        Address = InventoryGuards.Optional(address, 300, "Address");
        Notes = InventoryGuards.Optional(notes, 500, "Notes");
    }

    public void Activate(bool active) => IsActive = active;
}

/// <summary>
/// A physical store (main store, a branch's reagent room, a fridge…). Its <see cref="Branch"/> is the Branch reference
/// name labs and reps already carry, and the org-scope dimension every stock row of the store is visible on.
/// Deactivated, never hard-deleted (its movement history stays readable).
/// </summary>
public sealed class Store : AggregateRoot<StoreId>, IAuditable
{
    private Store() { } // EF
    private Store(StoreId id) : base(id) { }

    public string Name { get; private set; } = null!;
    public string Branch { get; private set; } = null!;
    public string? Location { get; private set; }
    public bool IsActive { get; private set; } = true;

    public DateTimeOffset CreatedAt { get; private set; }
    public string CreatedBy { get; private set; } = null!;
    public DateTimeOffset? UpdatedAt { get; private set; }
    public string? UpdatedBy { get; private set; }

    public static Store Create(string name, string branch, string? location)
    {
        var s = new Store(StoreId.New());
        s.Update(name, branch, location);
        return s;
    }

    public void Update(string name, string branch, string? location)
    {
        Name = InventoryGuards.Required(name, "Store name", 120);
        Branch = InventoryGuards.Required(branch, "Branch", 100);
        Location = InventoryGuards.Optional(location, 200, "Location");
    }

    public void Activate(bool active) => IsActive = active;
}

/// <summary>A test the item is consumed by: <see cref="QuantityPerTest"/> of the item (in its unit) is used up per test
/// performed. Drives the utilization report (expected consumption = tests performed × quantity per test).</summary>
public sealed class ItemTestLink
{
    private ItemTestLink() { } // EF
    private ItemTestLink(ItemTestLinkId id, string testCode, int testType, string testName, decimal quantityPerTest)
    {
        Id = id; TestCode = testCode; TestType = testType; TestName = testName; QuantityPerTest = quantityPerTest;
    }

    public ItemTestLinkId Id { get; private set; }
    public string TestCode { get; private set; } = null!;
    /// <summary>Oracle test_type — with the code, the catalogue's natural key.</summary>
    public int TestType { get; private set; }
    /// <summary>Display snapshot of the catalogue name at link time.</summary>
    public string TestName { get; private set; } = null!;
    public decimal QuantityPerTest { get; private set; }

    public static ItemTestLink Create(string testCode, int testType, string? testName, decimal quantityPerTest)
    {
        var code = InventoryGuards.TestCode(testCode);
        var name = string.IsNullOrWhiteSpace(testName) ? code : InventoryGuards.Optional(testName, 200, "Test name")!;
        return new ItemTestLink(ItemTestLinkId.New(), code, testType, name, InventoryGuards.Quantity(quantityPerTest, "Quantity per test"));
    }
}

/// <summary>
/// A stock item (chemical, consumable, kit…). Carries its manufacturer, catalogue number, unit, the stock limit that
/// raises the low-stock alert (<see cref="MinStock"/>, total on hand across the stores), the suggested reorder quantity,
/// the number of days before expiry a lot is flagged, and the tests it is consumed by. Deactivated, never hard-deleted.
/// </summary>
public sealed class InventoryItem : AggregateRoot<InventoryItemId>, IAuditable
{
    public const int DefaultExpiryWarningDays = 30;
    private readonly List<ItemTestLink> _testLinks = new();

    private InventoryItem() { } // EF
    private InventoryItem(InventoryItemId id) : base(id) { }

    public string Code { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public ItemKind Kind { get; private set; } = null!;
    public ManufacturerId ManufacturerId { get; private set; }
    public string? CatalogNumber { get; private set; }
    /// <summary>The unit every quantity of this item is expressed in (mL, g, test, box…).</summary>
    public string Unit { get; private set; } = null!;
    /// <summary>Low-stock threshold on the total quantity on hand (0 = no alert).</summary>
    public decimal MinStock { get; private set; }
    /// <summary>Suggested quantity to order when the item runs low (0 = none suggested).</summary>
    public decimal ReorderQuantity { get; private set; }
    /// <summary>Lots expiring within this many days are flagged "expiring".</summary>
    public int ExpiryWarningDays { get; private set; } = DefaultExpiryWarningDays;
    public string? StorageConditions { get; private set; }
    public string? Notes { get; private set; }
    public bool IsActive { get; private set; } = true;
    public IReadOnlyCollection<ItemTestLink> TestLinks => _testLinks.AsReadOnly();

    public DateTimeOffset CreatedAt { get; private set; }
    public string CreatedBy { get; private set; } = null!;
    public DateTimeOffset? UpdatedAt { get; private set; }
    public string? UpdatedBy { get; private set; }

    public static InventoryItem Create(string code, string name, ItemKind kind, ManufacturerId manufacturerId, string? catalogNumber, string unit,
        decimal minStock, decimal reorderQuantity, int expiryWarningDays, string? storageConditions, string? notes)
    {
        var item = new InventoryItem(InventoryItemId.New());
        item.Code = InventoryGuards.Required(code, "Item code", 40).ToUpperInvariant();
        item.Update(name, kind, manufacturerId, catalogNumber, unit, minStock, reorderQuantity, expiryWarningDays, storageConditions, notes);
        return item;
    }

    public void Update(string name, ItemKind kind, ManufacturerId manufacturerId, string? catalogNumber, string unit,
        decimal minStock, decimal reorderQuantity, int expiryWarningDays, string? storageConditions, string? notes)
    {
        Name = InventoryGuards.Required(name, "Item name", 200);
        Kind = kind ?? throw new DomainException("Item kind is required.");
        if (manufacturerId.Value == Guid.Empty) throw new DomainException("Manufacturer is required.");
        ManufacturerId = manufacturerId;
        CatalogNumber = InventoryGuards.Optional(catalogNumber, 80, "Catalogue number");
        Unit = InventoryGuards.Required(unit, "Unit", 20);
        MinStock = InventoryGuards.Quantity(minStock, "Minimum stock", allowZero: true);
        ReorderQuantity = InventoryGuards.Quantity(reorderQuantity, "Reorder quantity", allowZero: true);
        if (expiryWarningDays < 0 || expiryWarningDays > 730) throw new DomainException("Expiry warning days must be between 0 and 730.");
        ExpiryWarningDays = expiryWarningDays;
        StorageConditions = InventoryGuards.Optional(storageConditions, 200, "Storage conditions");
        Notes = InventoryGuards.Optional(notes, 1000, "Notes");
    }

    /// <summary>Replaces the test links. A test (code + type) may appear once.</summary>
    public void SetTestLinks(IEnumerable<ItemTestLink> links)
    {
        var list = links.ToList();
        if (list.Select(l => (l.TestCode, l.TestType)).Distinct().Count() != list.Count)
            throw new DomainException("A test can be linked to the item only once.");
        _testLinks.Clear();
        _testLinks.AddRange(list);
    }

    public void Activate(bool active) => IsActive = active;
}

// ---- Purchasing ----

/// <summary>One item on a purchase order. <see cref="ReceivedQuantity"/> accumulates over the order's goods receipts and
/// can never exceed <see cref="OrderedQuantity"/> — the receiving step validates the counts against what was ordered.</summary>
public sealed class PurchaseOrderLine
{
    private PurchaseOrderLine() { } // EF
    private PurchaseOrderLine(PurchaseOrderLineId id, InventoryItemId itemId, decimal orderedQuantity, Money unitPrice, string? notes)
    {
        Id = id; ItemId = itemId; OrderedQuantity = orderedQuantity; UnitPrice = unitPrice; Notes = notes;
    }

    public PurchaseOrderLineId Id { get; private set; }
    public InventoryItemId ItemId { get; private set; }
    public decimal OrderedQuantity { get; private set; }
    public Money UnitPrice { get; private set; }
    public decimal ReceivedQuantity { get; private set; }
    public string? Notes { get; private set; }

    public decimal Outstanding => OrderedQuantity - ReceivedQuantity;
    public bool IsFullyReceived => ReceivedQuantity >= OrderedQuantity;
    public Money LineTotal => UnitPrice * OrderedQuantity;

    public static PurchaseOrderLine Create(InventoryItemId itemId, decimal orderedQuantity, decimal unitPrice, string? notes)
    {
        if (itemId.Value == Guid.Empty) throw new DomainException("Item is required.");
        return new PurchaseOrderLine(PurchaseOrderLineId.New(), itemId, InventoryGuards.Quantity(orderedQuantity, "Ordered quantity"),
            InventoryGuards.NonNegative(unitPrice, "Unit price"), InventoryGuards.Optional(notes, 300, "Line notes"));
    }

    internal void Receive(decimal quantity)
    {
        var q = InventoryGuards.Quantity(quantity, "Received quantity");
        if (q > Outstanding)
            throw new DomainException($"Received quantity {q:0.###} exceeds the outstanding {Outstanding:0.###} on the order line.");
        ReceivedQuantity += q;
    }
}

/// <summary>
/// An order placed with a distributor for delivery to one store. Editable while Draft; Submit sends it (Ordered); each
/// goods receipt records what arrived against the lines and moves the order to PartiallyReceived / Received; Close
/// accepts a short delivery; Cancel is possible only while nothing has been received.
/// </summary>
public sealed class PurchaseOrder : AggregateRoot<PurchaseOrderId>, IAuditable
{
    private readonly List<PurchaseOrderLine> _lines = new();

    private PurchaseOrder() { } // EF
    private PurchaseOrder(PurchaseOrderId id) : base(id) { }

    /// <summary>DB-generated identity — the human PO number is PO-{Serial:D5}.</summary>
    public long Serial { get; private set; }
    public SupplierId SupplierId { get; private set; }
    public StoreId StoreId { get; private set; }
    public DateOnly OrderDate { get; private set; }
    public DateOnly? ExpectedDate { get; private set; }
    public PurchaseOrderStatus Status { get; private set; } = null!;
    /// <summary>The distributor's quotation / reference number.</summary>
    public string? Reference { get; private set; }
    public string? Notes { get; private set; }
    public DateOnly? OrderedOn { get; private set; }
    public DateOnly? ClosedOn { get; private set; }
    public IReadOnlyCollection<PurchaseOrderLine> Lines => _lines.AsReadOnly();

    public DateTimeOffset CreatedAt { get; private set; }
    public string CreatedBy { get; private set; } = null!;
    public DateTimeOffset? UpdatedAt { get; private set; }
    public string? UpdatedBy { get; private set; }

    public string Number => $"PO-{Serial:D5}";
    public Money Total => _lines.Aggregate(Money.Zero, (sum, l) => sum + l.LineTotal);
    public bool IsOpen => Status == PurchaseOrderStatus.Ordered || Status == PurchaseOrderStatus.PartiallyReceived;
    public bool IsEditable => Status == PurchaseOrderStatus.Draft;

    public static PurchaseOrder Create(SupplierId supplierId, StoreId storeId, DateOnly orderDate, DateOnly? expectedDate, string? reference, string? notes,
        IEnumerable<PurchaseOrderLine> lines)
    {
        var po = new PurchaseOrder(PurchaseOrderId.New()) { Status = PurchaseOrderStatus.Draft };
        po.UpdateDraft(supplierId, storeId, orderDate, expectedDate, reference, notes, lines);
        return po;
    }

    public void UpdateDraft(SupplierId supplierId, StoreId storeId, DateOnly orderDate, DateOnly? expectedDate, string? reference, string? notes,
        IEnumerable<PurchaseOrderLine> lines)
    {
        if (!IsEditable) throw new DomainException("Only a draft purchase order can be edited.");
        if (supplierId.Value == Guid.Empty) throw new DomainException("Distributor is required.");
        if (storeId.Value == Guid.Empty) throw new DomainException("Destination store is required.");
        if (expectedDate is { } exp && exp < orderDate) throw new DomainException("Expected delivery date cannot be before the order date.");
        var list = lines.ToList();
        if (list.Count == 0) throw new DomainException("A purchase order needs at least one line.");
        if (list.Select(l => l.ItemId).Distinct().Count() != list.Count) throw new DomainException("An item can appear on a purchase order only once.");
        SupplierId = supplierId; StoreId = storeId; OrderDate = orderDate; ExpectedDate = expectedDate;
        Reference = InventoryGuards.Optional(reference, 80, "Reference");
        Notes = InventoryGuards.Optional(notes, 1000, "Notes");
        _lines.Clear();
        _lines.AddRange(list);
    }

    public void Submit(DateOnly on)
    {
        if (Status != PurchaseOrderStatus.Draft) throw new DomainException("Only a draft purchase order can be submitted.");
        Status = PurchaseOrderStatus.Ordered;
        OrderedOn = on;
    }

    /// <summary>Records a delivery against one line (the goods receipt calls this per line) and rolls the status forward.</summary>
    public void Receive(PurchaseOrderLineId lineId, decimal quantity)
    {
        if (!IsOpen) throw new DomainException($"A {Status.Name} purchase order cannot receive goods.");
        var line = _lines.FirstOrDefault(l => l.Id == lineId) ?? throw new DomainException("The order line does not belong to this purchase order.");
        line.Receive(quantity);
        Status = _lines.All(l => l.IsFullyReceived) ? PurchaseOrderStatus.Received : PurchaseOrderStatus.PartiallyReceived;
    }

    /// <summary>Accepts a short delivery: no more goods are expected on the outstanding quantities.</summary>
    public void Close(DateOnly on)
    {
        if (!IsOpen) throw new DomainException("Only an ordered or partially received purchase order can be closed.");
        Status = PurchaseOrderStatus.Closed;
        ClosedOn = on;
    }

    public void Cancel(DateOnly on)
    {
        if (Status != PurchaseOrderStatus.Draft && Status != PurchaseOrderStatus.Ordered)
            throw new DomainException("Only a draft or an ordered purchase order with nothing received can be cancelled.");
        if (_lines.Any(l => l.ReceivedQuantity > 0)) throw new DomainException("Goods were already received against this order; close it instead.");
        Status = PurchaseOrderStatus.Cancelled;
        ClosedOn = on;
    }
}

/// <summary>One received quantity of one order line, identified by the distributor's lot number and its expiry.</summary>
public sealed class GoodsReceiptLine
{
    private GoodsReceiptLine() { } // EF
    private GoodsReceiptLine(GoodsReceiptLineId id, PurchaseOrderLineId orderLineId, InventoryItemId itemId, decimal quantity, string lotNumber, DateOnly? expiryDate, Money unitCost)
    {
        Id = id; OrderLineId = orderLineId; ItemId = itemId; Quantity = quantity; LotNumber = lotNumber; ExpiryDate = expiryDate; UnitCost = unitCost;
    }

    public GoodsReceiptLineId Id { get; private set; }
    public PurchaseOrderLineId OrderLineId { get; private set; }
    public InventoryItemId ItemId { get; private set; }
    public decimal Quantity { get; private set; }
    public string LotNumber { get; private set; } = null!;
    public DateOnly? ExpiryDate { get; private set; }
    public Money UnitCost { get; private set; }

    public static GoodsReceiptLine Create(PurchaseOrderLineId orderLineId, InventoryItemId itemId, decimal quantity, string lotNumber, DateOnly? expiryDate, decimal unitCost, DateOnly receivedDate)
    {
        var lot = InventoryGuards.Required(lotNumber, "Lot number", 64);
        if (expiryDate is { } exp && exp < receivedDate) throw new DomainException($"Lot {lot} is already expired ({exp:yyyy-MM-dd}); it cannot be received into stock.");
        return new GoodsReceiptLine(GoodsReceiptLineId.New(), orderLineId, itemId, InventoryGuards.Quantity(quantity, "Received quantity"),
            lot, expiryDate, InventoryGuards.NonNegative(unitCost, "Unit cost"));
    }
}

/// <summary>
/// A delivery received against a purchase order into its store: the document behind the stock lots and Receipt
/// movements it created. Immutable once recorded — a wrong receipt is corrected with an Adjustment movement.
/// </summary>
public sealed class GoodsReceipt : AggregateRoot<GoodsReceiptId>, IAuditable
{
    private readonly List<GoodsReceiptLine> _lines = new();

    private GoodsReceipt() { } // EF
    private GoodsReceipt(GoodsReceiptId id) : base(id) { }

    public long Serial { get; private set; }
    public PurchaseOrderId PurchaseOrderId { get; private set; }
    public StoreId StoreId { get; private set; }
    public SupplierId SupplierId { get; private set; }
    public DateOnly ReceivedDate { get; private set; }
    public string? DeliveryNote { get; private set; }
    public string? InvoiceNumber { get; private set; }
    public string? Notes { get; private set; }
    public IReadOnlyCollection<GoodsReceiptLine> Lines => _lines.AsReadOnly();

    public DateTimeOffset CreatedAt { get; private set; }
    public string CreatedBy { get; private set; } = null!;
    public DateTimeOffset? UpdatedAt { get; private set; }
    public string? UpdatedBy { get; private set; }

    public string Number => $"GR-{Serial:D5}";

    public static GoodsReceipt Create(PurchaseOrder order, DateOnly receivedDate, string? deliveryNote, string? invoiceNumber, string? notes, IEnumerable<GoodsReceiptLine> lines)
    {
        var list = lines.ToList();
        if (list.Count == 0) throw new DomainException("A goods receipt needs at least one line.");
        if (receivedDate < order.OrderDate) throw new DomainException("The receipt date cannot be before the order date.");
        if (list.Select(l => (l.OrderLineId, l.LotNumber)).Distinct().Count() != list.Count)
            throw new DomainException("The same lot of the same order line appears twice on the receipt.");
        var gr = new GoodsReceipt(GoodsReceiptId.New())
        {
            PurchaseOrderId = order.Id,
            StoreId = order.StoreId,
            SupplierId = order.SupplierId,
            ReceivedDate = receivedDate,
            DeliveryNote = InventoryGuards.Optional(deliveryNote, 80, "Delivery note"),
            InvoiceNumber = InventoryGuards.Optional(invoiceNumber, 80, "Invoice number"),
            Notes = InventoryGuards.Optional(notes, 1000, "Notes"),
        };
        gr._lines.AddRange(list);
        return gr;
    }
}

// ---- Stock ----

/// <summary>
/// The quantity of one item in one store from one lot (distributor lot number + expiry). Created by a goods receipt or an
/// incoming transfer; every issue, transfer and adjustment changes <see cref="Quantity"/> through this aggregate, and the
/// caller writes the matching <see cref="StockMovement"/>. Unique per (item, store, lot number).
/// </summary>
public sealed class StockLot : AggregateRoot<StockLotId>, IAuditable
{
    private StockLot() { } // EF
    private StockLot(StockLotId id) : base(id) { }

    public InventoryItemId ItemId { get; private set; }
    public StoreId StoreId { get; private set; }
    public string LotNumber { get; private set; } = null!;
    public DateOnly? ExpiryDate { get; private set; }
    public decimal Quantity { get; private set; }
    /// <summary>Last known unit cost (from the receipt, or carried by a transfer) — values the stock on hand.</summary>
    public Money UnitCost { get; private set; }
    public DateOnly FirstReceivedOn { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public string CreatedBy { get; private set; } = null!;
    public DateTimeOffset? UpdatedAt { get; private set; }
    public string? UpdatedBy { get; private set; }

    public Money Value => UnitCost * Quantity;
    public bool IsExpired(DateOnly today) => ExpiryDate is { } e && e < today;
    public bool ExpiresWithin(DateOnly today, int days) => ExpiryDate is { } e && e >= today && e <= today.AddDays(days);

    public static StockLot Open(InventoryItemId itemId, StoreId storeId, string lotNumber, DateOnly? expiryDate, decimal unitCost, DateOnly receivedOn)
    {
        if (itemId.Value == Guid.Empty) throw new DomainException("Item is required.");
        if (storeId.Value == Guid.Empty) throw new DomainException("Store is required.");
        return new StockLot(StockLotId.New())
        {
            ItemId = itemId,
            StoreId = storeId,
            LotNumber = InventoryGuards.Required(lotNumber, "Lot number", 64),
            ExpiryDate = expiryDate,
            Quantity = 0m,
            UnitCost = InventoryGuards.NonNegative(unitCost, "Unit cost"),
            FirstReceivedOn = receivedOn,
        };
    }

    /// <summary>Adds stock; a positive unit cost refreshes the lot's cost, and an expiry date fills a missing one.</summary>
    public void Add(decimal quantity, decimal? unitCost = null, DateOnly? expiryDate = null)
    {
        Quantity += InventoryGuards.Quantity(quantity, "Quantity");
        if (unitCost is { } c && c > 0) UnitCost = new Money(c);
        if (ExpiryDate is null && expiryDate is not null) ExpiryDate = expiryDate;
    }

    public void Take(decimal quantity)
    {
        var q = InventoryGuards.Quantity(quantity, "Quantity");
        if (q > Quantity) throw new DomainException($"Only {Quantity:0.###} of lot {LotNumber} is on hand; {q:0.###} requested.");
        Quantity -= q;
    }

    /// <summary>A physical count: sets the on-hand quantity and returns the signed difference to post as an adjustment.</summary>
    public decimal SetCounted(decimal countedQuantity)
    {
        var q = InventoryGuards.Quantity(countedQuantity, "Counted quantity", allowZero: true);
        var delta = q - Quantity;
        if (delta == 0) throw new DomainException("The counted quantity equals the quantity on hand; nothing to adjust.");
        Quantity = q;
        return delta;
    }
}

/// <summary>
/// One entry of the immutable stock ledger: a signed quantity change of one lot in one store, with the balance of the lot
/// after it, the document it came from (purchase-order receipt, transfer, manual issue / adjustment) and, for a
/// consumption, the test it was used for. Never updated or deleted — corrections are new Adjustment movements.
/// </summary>
public sealed class StockMovement : AggregateRoot<StockMovementId>, IAuditable
{
    private StockMovement() { } // EF
    private StockMovement(StockMovementId id) : base(id) { }

    public long Serial { get; private set; }
    public DateOnly Date { get; private set; }
    public StockMovementType Type { get; private set; } = null!;
    public InventoryItemId ItemId { get; private set; }
    public StoreId StoreId { get; private set; }
    public StockLotId LotId { get; private set; }
    public string LotNumber { get; private set; } = null!;
    public DateOnly? ExpiryDate { get; private set; }
    /// <summary>Signed: positive into the store, negative out of it.</summary>
    public decimal Quantity { get; private set; }
    /// <summary>The lot's quantity on hand after this movement.</summary>
    public decimal BalanceAfter { get; private set; }
    public Money UnitCost { get; private set; }
    /// <summary>The source document: GoodsReceipt / StockTransfer / Manual.</summary>
    public string ReferenceKind { get; private set; } = null!;
    public Guid? ReferenceId { get; private set; }
    public string? Reason { get; private set; }
    public string? TestCode { get; private set; }
    public string? Notes { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public string CreatedBy { get; private set; } = null!;
    public DateTimeOffset? UpdatedAt { get; private set; }
    public string? UpdatedBy { get; private set; }

    public const string RefGoodsReceipt = "GoodsReceipt";
    public const string RefTransfer = "StockTransfer";
    public const string RefManual = "Manual";

    private static StockMovement Build(StockMovementType type, StockLot lot, DateOnly date, decimal signedQuantity, string referenceKind, Guid? referenceId,
        string? reason, string? testCode, string? notes)
    {
        if (signedQuantity == 0) throw new DomainException("A movement must change the quantity.");
        if (type.AddsStock && signedQuantity < 0) throw new DomainException($"A {type.Name} movement adds stock.");
        if (type.RemovesStock && signedQuantity > 0) throw new DomainException($"A {type.Name} movement removes stock.");
        return new StockMovement(StockMovementId.New())
        {
            Date = date,
            Type = type,
            ItemId = lot.ItemId,
            StoreId = lot.StoreId,
            LotId = lot.Id,
            LotNumber = lot.LotNumber,
            ExpiryDate = lot.ExpiryDate,
            Quantity = decimal.Round(signedQuantity, 3, MidpointRounding.ToEven),
            BalanceAfter = lot.Quantity,
            UnitCost = lot.UnitCost,
            ReferenceKind = referenceKind,
            ReferenceId = referenceId,
            Reason = InventoryGuards.Optional(reason, 40, "Reason"),
            TestCode = testCode is null ? null : InventoryGuards.TestCode(testCode),
            Notes = InventoryGuards.Optional(notes, 500, "Notes"),
        };
    }

    /// <summary>Call AFTER the lot was changed, so BalanceAfter reads the new on-hand quantity.</summary>
    public static StockMovement Receipt(StockLot lot, DateOnly date, decimal quantity, GoodsReceiptId receiptId, string? notes = null) =>
        Build(StockMovementType.Receipt, lot, date, quantity, RefGoodsReceipt, receiptId.Value, null, null, notes);

    public static StockMovement Issue(StockLot lot, DateOnly date, decimal quantity, IssueReason reason, string? testCode, string? notes) =>
        Build(reason.MovementType, lot, date, -quantity, RefManual, null, reason.Name, testCode, notes);

    public static StockMovement TransferOut(StockLot lot, DateOnly date, decimal quantity, StockTransferId transferId, string? notes = null) =>
        Build(StockMovementType.TransferOut, lot, date, -quantity, RefTransfer, transferId.Value, null, null, notes);

    public static StockMovement TransferIn(StockLot lot, DateOnly date, decimal quantity, StockTransferId transferId, string? notes = null) =>
        Build(StockMovementType.TransferIn, lot, date, quantity, RefTransfer, transferId.Value, null, null, notes);

    public static StockMovement Adjustment(StockLot lot, DateOnly date, decimal signedDelta, string reason, string? notes) =>
        Build(StockMovementType.Adjustment, lot, date, signedDelta, RefManual, null, InventoryGuards.Required(reason, "Reason", 40), null, notes);
}

/// <summary>One lot moved by a transfer: the quantity dispatched from the source lot and, once the destination confirms,
/// the quantity actually received (a shortfall stays visible on the transfer — the destination gets what arrived).</summary>
public sealed class StockTransferLine
{
    private StockTransferLine() { } // EF
    private StockTransferLine(StockTransferLineId id, InventoryItemId itemId, StockLotId sourceLotId, string lotNumber, DateOnly? expiryDate, decimal quantity, Money unitCost)
    {
        Id = id; ItemId = itemId; SourceLotId = sourceLotId; LotNumber = lotNumber; ExpiryDate = expiryDate; Quantity = quantity; UnitCost = unitCost;
    }

    public StockTransferLineId Id { get; private set; }
    public InventoryItemId ItemId { get; private set; }
    public StockLotId SourceLotId { get; private set; }
    public string LotNumber { get; private set; } = null!;
    public DateOnly? ExpiryDate { get; private set; }
    /// <summary>Quantity dispatched from the source store.</summary>
    public decimal Quantity { get; private set; }
    /// <summary>Quantity the destination confirmed; null until received.</summary>
    public decimal? ReceivedQuantity { get; private set; }
    public Money UnitCost { get; private set; }

    public decimal Shortfall => ReceivedQuantity is { } r ? Quantity - r : 0m;

    public static StockTransferLine FromLot(StockLot lot, decimal quantity) =>
        new(StockTransferLineId.New(), lot.ItemId, lot.Id, lot.LotNumber, lot.ExpiryDate, InventoryGuards.Quantity(quantity, "Transfer quantity"), lot.UnitCost);

    internal void Receive(decimal receivedQuantity)
    {
        var q = InventoryGuards.Quantity(receivedQuantity, "Received quantity", allowZero: true);
        if (q > Quantity) throw new DomainException($"Received {q:0.###} of lot {LotNumber} but only {Quantity:0.###} was dispatched.");
        ReceivedQuantity = q;
    }
}

/// <summary>
/// Stock moved from one store to another. Creating the transfer takes the lots out of the source (TransferOut movements,
/// status InTransit); the destination store then confirms the counts line by line (TransferIn movements for what
/// arrived, status Received). Cancelling while in transit puts the stock back into the source.
/// </summary>
public sealed class StockTransfer : AggregateRoot<StockTransferId>, IAuditable
{
    private readonly List<StockTransferLine> _lines = new();

    private StockTransfer() { } // EF
    private StockTransfer(StockTransferId id) : base(id) { }

    public long Serial { get; private set; }
    public StoreId FromStoreId { get; private set; }
    public StoreId ToStoreId { get; private set; }
    public DateOnly Date { get; private set; }
    public TransferStatus Status { get; private set; } = null!;
    public string? Notes { get; private set; }
    public DateOnly? ReceivedDate { get; private set; }
    public string? ReceiveNotes { get; private set; }
    public IReadOnlyCollection<StockTransferLine> Lines => _lines.AsReadOnly();

    public DateTimeOffset CreatedAt { get; private set; }
    public string CreatedBy { get; private set; } = null!;
    public DateTimeOffset? UpdatedAt { get; private set; }
    public string? UpdatedBy { get; private set; }

    public string Number => $"TR-{Serial:D5}";

    public static StockTransfer Create(StoreId fromStoreId, StoreId toStoreId, DateOnly date, string? notes, IEnumerable<StockTransferLine> lines)
    {
        if (fromStoreId.Value == Guid.Empty || toStoreId.Value == Guid.Empty) throw new DomainException("Both stores are required.");
        if (fromStoreId == toStoreId) throw new DomainException("The source and destination stores must differ.");
        var list = lines.ToList();
        if (list.Count == 0) throw new DomainException("A transfer needs at least one line.");
        if (list.Select(l => l.SourceLotId).Distinct().Count() != list.Count) throw new DomainException("A lot can appear on a transfer only once.");
        var t = new StockTransfer(StockTransferId.New())
        {
            FromStoreId = fromStoreId,
            ToStoreId = toStoreId,
            Date = date,
            Status = TransferStatus.InTransit,
            Notes = InventoryGuards.Optional(notes, 1000, "Notes"),
        };
        t._lines.AddRange(list);
        return t;
    }

    /// <summary>The destination confirms what arrived, line by line (a line left out counts as fully received).</summary>
    public void Receive(DateOnly receivedDate, IReadOnlyDictionary<StockTransferLineId, decimal> receivedQuantities, string? notes)
    {
        if (Status != TransferStatus.InTransit) throw new DomainException($"A {Status.Name} transfer cannot be received.");
        if (receivedDate < Date) throw new DomainException("The receipt date cannot be before the transfer date.");
        foreach (var key in receivedQuantities.Keys)
            if (_lines.All(l => l.Id != key)) throw new DomainException("A received line does not belong to this transfer.");
        foreach (var line in _lines)
            line.Receive(receivedQuantities.TryGetValue(line.Id, out var q) ? q : line.Quantity);
        Status = TransferStatus.Received;
        ReceivedDate = receivedDate;
        ReceiveNotes = InventoryGuards.Optional(notes, 1000, "Notes");
    }

    public void Cancel(string? notes)
    {
        if (Status != TransferStatus.InTransit) throw new DomainException("Only an in-transit transfer can be cancelled.");
        Status = TransferStatus.Cancelled;
        ReceiveNotes = InventoryGuards.Optional(notes, 1000, "Notes");
    }
}
