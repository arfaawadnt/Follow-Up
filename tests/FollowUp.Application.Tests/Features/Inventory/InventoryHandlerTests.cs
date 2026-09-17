using FluentAssertions;
using FollowUp.Application.Common.Abstractions;
using FollowUp.Application.Common.Exceptions;
using FollowUp.Application.Features.Inventory;
using FollowUp.Application.Tests.Common;
using FollowUp.Domain.Identity;
using FollowUp.Domain.Inventory;

namespace FollowUp.Application.Tests.Features.Inventory;

/// <summary>
/// Inventory handlers: a purchase order is received line by line into lots with the counts validated against the order,
/// a transfer takes stock out of the source at once and lands only what the destination confirms, a manual issue refuses
/// more than the lot holds, store scope is enforced on every stock write, and the validators reject the shapes the domain
/// forbids before a handler runs.
/// </summary>
public class InventoryHandlerTests
{
    private static readonly DateOnly D = new(2026, 9, 17);
    private static readonly FakeClock Clock = new(new DateTimeOffset(2026, 9, 17, 8, 0, 0, TimeSpan.Zero));

    private sealed class World
    {
        public readonly FakeManufacturerRepository Manufacturers = new();
        public readonly FakeSupplierRepository Suppliers = new();
        public readonly FakeStoreRepository Stores = new();
        public readonly FakeInventoryItemRepository Items = new();
        public readonly FakePurchaseOrderRepository Orders = new();
        public readonly FakeGoodsReceiptRepository Receipts = new();
        public readonly FakeStockLotRepository Lots = new();
        public readonly FakeStockMovementRepository Movements = new();
        public readonly FakeStockTransferRepository Transfers = new();
        public readonly Manufacturer Roche; public readonly Supplier Dist; public readonly Store Main; public readonly Store Giza; public readonly InventoryItem Reagent; public readonly InventoryItem Tubes;

        public World()
        {
            Roche = Manufacturer.Create("Roche", "CH", null); Manufacturers.Store.Add(Roche);
            Dist = Supplier.Create("MedDist", "Ali", null, null, null, null); Suppliers.Store.Add(Dist);
            Main = Store.Create("Main store", "Cairo", null); Stores.Store.Add(Main);
            Giza = Store.Create("Giza store", "Giza", null); Stores.Store.Add(Giza);
            Reagent = InventoryItem.Create("REA-1", "Glucose reagent", ItemKind.Chemical, Roche.Id, "R-100", "mL", 500m, 2000m, 30, "2-8°C", null); Items.Store.Add(Reagent);
            Tubes = InventoryItem.Create("TUB-1", "EDTA tubes", ItemKind.Consumable, Roche.Id, null, "pcs", 100m, 1000m, 60, null, null); Items.Store.Add(Tubes);
        }

        public StockLot Lot(InventoryItem item, Store store, string lot, decimal qty, DateOnly? expiry = null)
        {
            var l = StockLot.Open(item.Id, store.Id, lot, expiry, 10m, D); l.Add(qty); Lots.Store.Add(l); return l;
        }
    }

    private static FakeCurrentUser CairoOnly() => new() { Scope = OrgScope.Create(new[] { "Cairo" }, new[] { "*" }, new[] { "*" }, new[] { "*" }, new[] { "*" }, new[] { "*" }) };

    // ---- Purchase order → goods receipt ----

    [Fact]
    public async Task A_submitted_order_is_received_into_lots_with_the_counts_validated_against_the_order()
    {
        var w = new World(); var user = new FakeCurrentUser();
        var create = new CreatePurchaseOrderHandler(w.Orders, w.Suppliers, w.Stores, w.Items, user, Clock);
        var poId = await create.Handle(new CreatePurchaseOrderCommand(w.Dist.Id.Value, w.Main.Id.Value, D, D.AddDays(7), "Q-77", null,
            new[] { new PurchaseOrderLineInput(w.Reagent.Id.Value, 1000m, 2.5m, null), new PurchaseOrderLineInput(w.Tubes.Id.Value, 500m, 0.4m, null) }, Submit: true), CancellationToken.None);
        var po = w.Orders.Store.Single(); po.Status.Should().BeSameAs(PurchaseOrderStatus.Ordered); po.Total.Amount.Should().Be(2700m);
        var reagentLine = po.Lines.Single(l => l.ItemId == w.Reagent.Id); var tubeLine = po.Lines.Single(l => l.ItemId == w.Tubes.Id);

        var receive = new ReceivePurchaseOrderHandler(w.Orders, w.Receipts, w.Stores, w.Lots, w.Movements, user);
        var grId = await receive.Handle(new ReceivePurchaseOrderCommand(poId, D.AddDays(3), "DN-1", "INV-9", null, new[]
        {
            new ReceiptLineInput(reagentLine.Id.Value, 600m, "L-A", D.AddMonths(6), null),   // partial
            new ReceiptLineInput(reagentLine.Id.Value, 400m, "L-B", D.AddMonths(9), 2.4m),   // second lot completes the line
            new ReceiptLineInput(tubeLine.Id.Value, 200m, "T-1", null, null),                 // partial
        }), CancellationToken.None);

        po.Status.Should().BeSameAs(PurchaseOrderStatus.PartiallyReceived);
        reagentLine.ReceivedQuantity.Should().Be(1000m); reagentLine.IsFullyReceived.Should().BeTrue(); tubeLine.Outstanding.Should().Be(300m);
        var gr = w.Receipts.Store.Single(); gr.Id.Value.Should().Be(grId); gr.Lines.Should().HaveCount(3); gr.StoreId.Should().Be(w.Main.Id);
        w.Lots.Store.Should().HaveCount(3);
        var lotB = w.Lots.Store.Single(l => l.LotNumber == "L-B"); lotB.Quantity.Should().Be(400m); lotB.UnitCost.Amount.Should().Be(2.4m, "an explicit unit cost wins over the order price");
        w.Lots.Store.Single(l => l.LotNumber == "L-A").UnitCost.Amount.Should().Be(2.5m, "defaults to the order line price");
        w.Movements.Store.Should().HaveCount(3).And.OnlyContain(m => m.Type == StockMovementType.Receipt && m.Quantity > 0 && m.ReferenceId == grId);
        w.Movements.Store.Single(m => m.LotNumber == "T-1").BalanceAfter.Should().Be(200m);

        // Over-receiving the outstanding 300 tubes is refused as a conflict and nothing is booked.
        var lotsBefore = w.Lots.Store.Count; var movesBefore = w.Movements.Store.Count;
        await FluentActions.Awaiting(() => receive.Handle(new ReceivePurchaseOrderCommand(poId, D.AddDays(5), null, null, null,
                new[] { new ReceiptLineInput(tubeLine.Id.Value, 301m, "T-2", null, null) }), CancellationToken.None))
            .Should().ThrowAsync<ConflictException>().WithMessage("*exceeds the outstanding*");
        w.Lots.Store.Should().HaveCount(lotsBefore); w.Movements.Store.Should().HaveCount(movesBefore);

        // The remaining 300 complete the order.
        await receive.Handle(new ReceivePurchaseOrderCommand(poId, D.AddDays(5), null, null, null, new[] { new ReceiptLineInput(tubeLine.Id.Value, 300m, "T-1", null, null) }), CancellationToken.None);
        po.Status.Should().BeSameAs(PurchaseOrderStatus.Received);
        w.Lots.Store.Single(l => l.LotNumber == "T-1").Quantity.Should().Be(500m, "the same lot number tops up the existing lot");
    }

    [Fact]
    public async Task A_draft_order_cannot_receive_goods_and_an_expired_lot_is_refused()
    {
        var w = new World(); var user = new FakeCurrentUser();
        var poId = await new CreatePurchaseOrderHandler(w.Orders, w.Suppliers, w.Stores, w.Items, user, Clock).Handle(new CreatePurchaseOrderCommand(w.Dist.Id.Value, w.Main.Id.Value, D, null, null, null,
            new[] { new PurchaseOrderLineInput(w.Reagent.Id.Value, 10m, 1m, null) }), CancellationToken.None);
        var line = w.Orders.Store.Single().Lines.Single();
        var receive = new ReceivePurchaseOrderHandler(w.Orders, w.Receipts, w.Stores, w.Lots, w.Movements, user);
        await FluentActions.Awaiting(() => receive.Handle(new ReceivePurchaseOrderCommand(poId, D, null, null, null, new[] { new ReceiptLineInput(line.Id.Value, 10m, "L", null, null) }), CancellationToken.None))
            .Should().ThrowAsync<ConflictException>().WithMessage("*Draft*");

        await new ChangePurchaseOrderStatusHandler(w.Orders, w.Stores, user, Clock).Handle(new ChangePurchaseOrderStatusCommand(poId, ChangePurchaseOrderStatusCommand.Submit), CancellationToken.None);
        new ReceivePurchaseOrderValidator().Validate(new ReceivePurchaseOrderCommand(poId, D, null, null, null, new[] { new ReceiptLineInput(line.Id.Value, 10m, "L", D.AddDays(-1), null) }))
            .IsValid.Should().BeFalse("an already-expired lot cannot be received");
        // Cancelling an ordered PO with nothing received is allowed; cancelling after a receipt is a conflict.
        await receive.Handle(new ReceivePurchaseOrderCommand(poId, D, null, null, null, new[] { new ReceiptLineInput(line.Id.Value, 4m, "L", null, null) }), CancellationToken.None);
        await FluentActions.Awaiting(() => new ChangePurchaseOrderStatusHandler(w.Orders, w.Stores, user, Clock).Handle(new ChangePurchaseOrderStatusCommand(poId, ChangePurchaseOrderStatusCommand.Cancel), CancellationToken.None))
            .Should().ThrowAsync<ConflictException>();
        await new ChangePurchaseOrderStatusHandler(w.Orders, w.Stores, user, Clock).Handle(new ChangePurchaseOrderStatusCommand(poId, ChangePurchaseOrderStatusCommand.Close), CancellationToken.None);
        w.Orders.Store.Single().Status.Should().BeSameAs(PurchaseOrderStatus.Closed);
    }

    // ---- Transfers ----

    [Fact]
    public async Task A_transfer_leaves_the_source_at_once_and_lands_only_what_the_destination_confirms()
    {
        var w = new World(); var user = new FakeCurrentUser();
        var lotA = w.Lot(w.Reagent, w.Main, "L-A", 600m, D.AddMonths(6)); var lotT = w.Lot(w.Tubes, w.Main, "T-1", 500m);
        var create = new CreateStockTransferHandler(w.Transfers, w.Stores, w.Lots, w.Movements, user);
        var id = await create.Handle(new CreateStockTransferCommand(w.Main.Id.Value, w.Giza.Id.Value, D, "weekly", new[] { new TransferLineInput(lotA.Id.Value, 200m), new TransferLineInput(lotT.Id.Value, 100m) }), CancellationToken.None);

        lotA.Quantity.Should().Be(400m); lotT.Quantity.Should().Be(400m);
        var t = w.Transfers.Store.Single(); t.Status.Should().BeSameAs(TransferStatus.InTransit);
        w.Movements.Store.Should().HaveCount(2).And.OnlyContain(m => m.Type == StockMovementType.TransferOut && m.Quantity < 0 && m.StoreId == w.Main.Id);

        // More than the lot holds → 409, nothing moved.
        await FluentActions.Awaiting(() => create.Handle(new CreateStockTransferCommand(w.Main.Id.Value, w.Giza.Id.Value, D, null, new[] { new TransferLineInput(lotA.Id.Value, 900m) }), CancellationToken.None))
            .Should().ThrowAsync<ConflictException>().WithMessage("*on hand*");
        w.Transfers.Store.Should().HaveCount(1);

        // Destination confirms: the reagent arrived short (180 of 200), the tubes in full (line omitted = fully received).
        var reagentLine = t.Lines.Single(l => l.ItemId == w.Reagent.Id);
        await new ReceiveStockTransferHandler(w.Transfers, w.Stores, w.Lots, w.Movements, user)
            .Handle(new ReceiveStockTransferCommand(id, D.AddDays(1), new[] { new TransferReceiveInput(reagentLine.Id.Value, 180m) }, "one bottle broken"), CancellationToken.None);
        t.Status.Should().BeSameAs(TransferStatus.Received); reagentLine.Shortfall.Should().Be(20m);
        var gizaReagent = w.Lots.Store.Single(l => l.StoreId == w.Giza.Id && l.ItemId == w.Reagent.Id);
        gizaReagent.Quantity.Should().Be(180m); gizaReagent.LotNumber.Should().Be("L-A"); gizaReagent.ExpiryDate.Should().Be(D.AddMonths(6));
        w.Lots.Store.Single(l => l.StoreId == w.Giza.Id && l.ItemId == w.Tubes.Id).Quantity.Should().Be(100m);
        w.Movements.Store.Where(m => m.Type == StockMovementType.TransferIn).Select(m => m.Quantity).Should().BeEquivalentTo(new[] { 180m, 100m });

        await FluentActions.Awaiting(() => new CancelStockTransferHandler(w.Transfers, w.Stores, w.Lots, w.Movements, user, Clock).Handle(new CancelStockTransferCommand(id, null), CancellationToken.None))
            .Should().ThrowAsync<ConflictException>("only an in-transit transfer can be cancelled");
    }

    [Fact]
    public async Task Cancelling_an_in_transit_transfer_returns_the_stock_to_the_source()
    {
        var w = new World(); var user = new FakeCurrentUser();
        var lot = w.Lot(w.Reagent, w.Main, "L-A", 100m);
        var id = await new CreateStockTransferHandler(w.Transfers, w.Stores, w.Lots, w.Movements, user)
            .Handle(new CreateStockTransferCommand(w.Main.Id.Value, w.Giza.Id.Value, D, null, new[] { new TransferLineInput(lot.Id.Value, 30m) }), CancellationToken.None);
        lot.Quantity.Should().Be(70m);
        await new CancelStockTransferHandler(w.Transfers, w.Stores, w.Lots, w.Movements, user, Clock).Handle(new CancelStockTransferCommand(id, "truck broke down"), CancellationToken.None);
        lot.Quantity.Should().Be(100m);
        w.Transfers.Store.Single().Status.Should().BeSameAs(TransferStatus.Cancelled);
        w.Movements.Store.Last().Type.Should().BeSameAs(StockMovementType.TransferIn);
    }

    // ---- Issues, adjustments, scope ----

    [Fact]
    public async Task Issuing_stock_refuses_more_than_the_lot_holds_and_records_a_negative_consumption()
    {
        var w = new World(); var user = new FakeCurrentUser();
        var lot = w.Lot(w.Reagent, w.Main, "L-A", 50m);
        var handler = new IssueStockHandler(w.Lots, w.Movements, w.Stores, user);
        await FluentActions.Awaiting(() => handler.Handle(new IssueStockCommand(lot.Id.Value, D, 60m, "Consumption", null, null), CancellationToken.None))
            .Should().ThrowAsync<ConflictException>().WithMessage("*Only 50*");
        lot.Quantity.Should().Be(50m);

        var id = await handler.Handle(new IssueStockCommand(lot.Id.Value, D, 12.5m, "Consumption", "GLU", "morning run"), CancellationToken.None);
        lot.Quantity.Should().Be(37.5m);
        var m = w.Movements.Store.Single(x => x.Id.Value == id);
        m.Type.Should().BeSameAs(StockMovementType.Consumption); m.Quantity.Should().Be(-12.5m); m.BalanceAfter.Should().Be(37.5m); m.TestCode.Should().Be("GLU");

        var expired = await handler.Handle(new IssueStockCommand(lot.Id.Value, D, 7.5m, "Expired", null, null), CancellationToken.None);
        w.Movements.Store.Single(x => x.Id.Value == expired).Type.Should().BeSameAs(StockMovementType.Disposal);

        var adj = await new AdjustStockHandler(w.Lots, w.Movements, w.Stores, user).Handle(new AdjustStockCommand(lot.Id.Value, D, 25m, "Count", null), CancellationToken.None);
        lot.Quantity.Should().Be(25m);
        w.Movements.Store.Single(x => x.Id.Value == adj).Quantity.Should().Be(-5m, "counted 25 against 30 on hand");
    }

    [Fact]
    public async Task Stock_writes_on_a_store_outside_the_callers_branch_scope_are_forbidden()
    {
        var w = new World(); var cairo = CairoOnly();
        var gizaLot = w.Lot(w.Reagent, w.Giza, "G-1", 100m);
        await FluentActions.Awaiting(() => new IssueStockHandler(w.Lots, w.Movements, w.Stores, cairo).Handle(new IssueStockCommand(gizaLot.Id.Value, D, 1m, "Consumption", null, null), CancellationToken.None))
            .Should().ThrowAsync<ForbiddenException>();
        await FluentActions.Awaiting(() => new CreatePurchaseOrderHandler(w.Orders, w.Suppliers, w.Stores, w.Items, cairo, Clock).Handle(new CreatePurchaseOrderCommand(w.Dist.Id.Value, w.Giza.Id.Value, D, null, null, null,
                new[] { new PurchaseOrderLineInput(w.Reagent.Id.Value, 1m, 1m, null) }), CancellationToken.None))
            .Should().ThrowAsync<ForbiddenException>();
        // A transfer INTO Giza from Cairo is also refused: both stores must be in scope.
        var mainLot = w.Lot(w.Reagent, w.Main, "M-1", 100m);
        await FluentActions.Awaiting(() => new CreateStockTransferHandler(w.Transfers, w.Stores, w.Lots, w.Movements, cairo).Handle(new CreateStockTransferCommand(w.Main.Id.Value, w.Giza.Id.Value, D, null,
                new[] { new TransferLineInput(mainLot.Id.Value, 1m) }), CancellationToken.None))
            .Should().ThrowAsync<ForbiddenException>();
        await FluentActions.Awaiting(() => new CreateStoreHandler(w.Stores, cairo).Handle(new CreateStoreCommand("Alex store", "Alexandria", null), CancellationToken.None))
            .Should().ThrowAsync<ForbiddenException>();
        w.Movements.Store.Should().BeEmpty(); w.Orders.Store.Should().BeEmpty(); w.Transfers.Store.Should().BeEmpty();
    }

    // ---- Items ----

    [Fact]
    public async Task Creating_an_item_stores_its_test_links_and_refuses_a_duplicate_code()
    {
        var w = new World();
        var handler = new CreateInventoryItemHandler(w.Items, w.Manufacturers);
        var id = await handler.Handle(new CreateInventoryItemCommand("hba1c-kit", "HbA1c kit", "Kit", w.Roche.Id.Value, "K-1", "test", 50m, 200m, 45, null, null,
            new[] { new ItemTestLinkInput("hba1c", 0, "HbA1c", 1m), new ItemTestLinkInput("GLU", 0, "Glucose", 0.5m) }), CancellationToken.None);
        var item = w.Items.Store.Single(i => i.Id.Value == id);
        item.Code.Should().Be("HBA1C-KIT"); item.TestLinks.Should().HaveCount(2); item.TestLinks.Single(t => t.TestCode == "HBA1C").QuantityPerTest.Should().Be(1m);
        await FluentActions.Awaiting(() => handler.Handle(new CreateInventoryItemCommand("HBA1C-KIT", "Dup", "Kit", w.Roche.Id.Value, null, "test", 0, 0, 30, null, null, null), CancellationToken.None))
            .Should().ThrowAsync<ConflictException>().WithMessage("*already exists*");
    }

    // ---- Validators ----

    [Fact]
    public void Validators_reject_the_shapes_the_domain_forbids()
    {
        var po = new CreatePurchaseOrderValidator();
        po.Validate(new CreatePurchaseOrderCommand(Guid.NewGuid(), Guid.NewGuid(), D, null, null, null, Array.Empty<PurchaseOrderLineInput>())).IsValid.Should().BeFalse("no lines");
        var item = Guid.NewGuid();
        po.Validate(new CreatePurchaseOrderCommand(Guid.NewGuid(), Guid.NewGuid(), D, null, null, null, new[] { new PurchaseOrderLineInput(item, 1m, 1m, null), new PurchaseOrderLineInput(item, 2m, 1m, null) })).IsValid.Should().BeFalse("duplicate item");
        po.Validate(new CreatePurchaseOrderCommand(Guid.NewGuid(), Guid.NewGuid(), D, D.AddDays(-1), null, null, new[] { new PurchaseOrderLineInput(item, 1m, 1m, null) })).IsValid.Should().BeFalse("expected before order date");
        po.Validate(new CreatePurchaseOrderCommand(Guid.NewGuid(), Guid.NewGuid(), D, D, null, null, new[] { new PurchaseOrderLineInput(item, 1m, 0m, null) })).IsValid.Should().BeTrue();

        var issue = new IssueStockValidator();
        issue.Validate(new IssueStockCommand(Guid.NewGuid(), D, 1m, "Lost", null, null)).IsValid.Should().BeFalse("unknown reason");
        issue.Validate(new IssueStockCommand(Guid.NewGuid(), D, 0m, "Consumption", null, null)).IsValid.Should().BeFalse("zero quantity");
        issue.Validate(new IssueStockCommand(Guid.NewGuid(), D, 1m, "Damaged", null, null)).IsValid.Should().BeTrue();

        var tr = new CreateStockTransferValidator(); var store = Guid.NewGuid(); var lot = Guid.NewGuid();
        tr.Validate(new CreateStockTransferCommand(store, store, D, null, new[] { new TransferLineInput(lot, 1m) })).IsValid.Should().BeFalse("same store");
        tr.Validate(new CreateStockTransferCommand(store, Guid.NewGuid(), D, null, new[] { new TransferLineInput(lot, 1m), new TransferLineInput(lot, 2m) })).IsValid.Should().BeFalse("duplicate lot");

        var it = new CreateInventoryItemValidator();
        it.Validate(new CreateInventoryItemCommand("A", "A", "Gas", Guid.NewGuid(), null, "mL", 0, 0, 30, null, null, null)).IsValid.Should().BeFalse("unknown kind");
        it.Validate(new CreateInventoryItemCommand("A", "A", "Chemical", Guid.NewGuid(), null, "mL", 0, 0, 30, null, null,
            new[] { new ItemTestLinkInput("GLU", 0, null, 1m), new ItemTestLinkInput("glu", 0, null, 2m) })).IsValid.Should().BeFalse("duplicate test link");
        it.Validate(new CreateInventoryItemCommand("A", "A", "Chemical", Guid.NewGuid(), null, "mL", 0, 0, 731, null, null, null)).IsValid.Should().BeFalse("warning window too long");
    }
}
