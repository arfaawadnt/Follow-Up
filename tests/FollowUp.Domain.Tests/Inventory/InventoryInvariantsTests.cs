using FluentAssertions;
using FollowUp.Domain.Common;
using FollowUp.Domain.Inventory;
using Xunit;

namespace FollowUp.Domain.Tests.Inventory;

/// <summary>Inventory module domain invariants — the rules the DB CHECKs mirror and the stock figures rely on.</summary>
public class InventoryInvariantsTests
{
    private static readonly DateOnly D = new(2026, 9, 17);
    private static InventoryItem Item(string code = "REA-1") =>
        InventoryItem.Create(code, "Reagent", ItemKind.Chemical, ManufacturerId.New(), null, "mL", 100m, 500m, 30, null, null);
    private static StockLot Lot(decimal qty, DateOnly? expiry = null)
    {
        var lot = StockLot.Open(InventoryItemId.New(), StoreId.New(), "L-1", expiry, 4m, D);
        if (qty > 0) lot.Add(qty);
        return lot;
    }

    [Fact]
    public void Enumerations_expose_the_agreed_fixed_values()
    {
        Enumeration.GetAll<ItemKind>().Select(e => e.Name).Should().BeEquivalentTo(new[] { "Chemical", "Consumable", "Kit", "Control", "Other" });
        Enumeration.GetAll<PurchaseOrderStatus>().Select(e => e.Name).Should().BeEquivalentTo(new[] { "Draft", "Ordered", "PartiallyReceived", "Received", "Closed", "Cancelled" });
        Enumeration.GetAll<TransferStatus>().Select(e => e.Name).Should().BeEquivalentTo(new[] { "InTransit", "Received", "Cancelled" });
        Enumeration.GetAll<StockMovementType>().Select(e => e.Name).Should().BeEquivalentTo(new[] { "Receipt", "Consumption", "Disposal", "TransferOut", "TransferIn", "Adjustment", "ReturnToSupplier" });
        IssueReason.Expired.MovementType.Should().BeSameAs(StockMovementType.Disposal);
        IssueReason.ReturnToSupplier.MovementType.Should().BeSameAs(StockMovementType.ReturnToSupplier);
        IssueReason.Other.MovementType.Should().BeSameAs(StockMovementType.Consumption);
    }

    [Fact]
    public void An_item_upper_cases_its_code_requires_a_manufacturer_and_links_each_test_once()
    {
        var item = Item("rea-1");
        item.Code.Should().Be("REA-1");
        FluentActions.Invoking(() => InventoryItem.Create("X", "X", ItemKind.Kit, new ManufacturerId(Guid.Empty), null, "mL", 0, 0, 30, null, null))
            .Should().Throw<DomainException>().WithMessage("*Manufacturer*");
        FluentActions.Invoking(() => item.SetTestLinks(new[] { ItemTestLink.Create("glu", 0, "Glucose", 1m), ItemTestLink.Create("GLU", 0, "Glucose", 2m) }))
            .Should().Throw<DomainException>().WithMessage("*only once*");
        item.SetTestLinks(new[] { ItemTestLink.Create("glu", 0, "Glucose", 1m), ItemTestLink.Create("GLU", 1, "Glucose (type 1)", 2m) });
        item.TestLinks.Should().HaveCount(2, "the same code under another test type is a different catalogue test");
        FluentActions.Invoking(() => ItemTestLink.Create("GLU", 0, null, 0m)).Should().Throw<DomainException>().WithMessage("*greater than zero*");
        FluentActions.Invoking(() => item.Update("X", ItemKind.Kit, item.ManufacturerId, null, "mL", -1m, 0, 30, null, null)).Should().Throw<DomainException>();
    }

    [Fact]
    public void A_purchase_order_walks_Draft_Ordered_Partially_Received_and_refuses_over_receipt()
    {
        var itemA = InventoryItemId.New(); var itemB = InventoryItemId.New();
        var po = PurchaseOrder.Create(SupplierId.New(), StoreId.New(), D, D.AddDays(5), null, null,
            new[] { PurchaseOrderLine.Create(itemA, 10m, 2m, null), PurchaseOrderLine.Create(itemB, 4m, 1.5m, null) });
        po.Status.Should().BeSameAs(PurchaseOrderStatus.Draft); po.Total.Amount.Should().Be(26m);
        FluentActions.Invoking(() => PurchaseOrder.Create(SupplierId.New(), StoreId.New(), D, D.AddDays(-1), null, null, new[] { PurchaseOrderLine.Create(itemA, 1m, 1m, null) }))
            .Should().Throw<DomainException>().WithMessage("*Expected delivery date*");
        FluentActions.Invoking(() => PurchaseOrder.Create(SupplierId.New(), StoreId.New(), D, null, null, null, new[] { PurchaseOrderLine.Create(itemA, 1m, 1m, null), PurchaseOrderLine.Create(itemA, 2m, 1m, null) }))
            .Should().Throw<DomainException>().WithMessage("*only once*");

        var lineA = po.Lines.Single(l => l.ItemId == itemA);
        FluentActions.Invoking(() => po.Receive(lineA.Id, 1m)).Should().Throw<DomainException>("a draft receives nothing");
        po.Submit(D); po.Status.Should().BeSameAs(PurchaseOrderStatus.Ordered);
        FluentActions.Invoking(() => po.UpdateDraft(po.SupplierId, po.StoreId, D, null, null, null, po.Lines.ToList())).Should().Throw<DomainException>();

        po.Receive(lineA.Id, 6m); po.Status.Should().BeSameAs(PurchaseOrderStatus.PartiallyReceived); lineA.Outstanding.Should().Be(4m);
        FluentActions.Invoking(() => po.Receive(lineA.Id, 4.001m)).Should().Throw<DomainException>().WithMessage("*exceeds the outstanding*");
        FluentActions.Invoking(() => po.Cancel(D)).Should().Throw<DomainException>("goods were already received");
        po.Receive(lineA.Id, 4m); po.Status.Should().BeSameAs(PurchaseOrderStatus.PartiallyReceived, "line B is still open");
        po.Receive(po.Lines.Single(l => l.ItemId == itemB).Id, 4m); po.Status.Should().BeSameAs(PurchaseOrderStatus.Received);
        FluentActions.Invoking(() => po.Close(D)).Should().Throw<DomainException>("a fully received order has nothing to close");
    }

    [Fact]
    public void A_goods_receipt_refuses_an_expired_lot_and_a_date_before_the_order()
    {
        var po = PurchaseOrder.Create(SupplierId.New(), StoreId.New(), D, null, null, null, new[] { PurchaseOrderLine.Create(InventoryItemId.New(), 10m, 2m, null) });
        var line = po.Lines.Single();
        FluentActions.Invoking(() => GoodsReceiptLine.Create(line.Id, line.ItemId, 5m, "L-1", D.AddDays(-1), 2m, D)).Should().Throw<DomainException>().WithMessage("*expired*");
        var ok = GoodsReceiptLine.Create(line.Id, line.ItemId, 5m, " L-1 ", D.AddMonths(3), 2m, D);
        ok.LotNumber.Should().Be("L-1");
        FluentActions.Invoking(() => GoodsReceipt.Create(po, D.AddDays(-1), null, null, null, new[] { ok })).Should().Throw<DomainException>().WithMessage("*before the order date*");
        FluentActions.Invoking(() => GoodsReceipt.Create(po, D, null, null, null, new[] { ok, GoodsReceiptLine.Create(line.Id, line.ItemId, 1m, "L-1", D.AddMonths(3), 2m, D) }))
            .Should().Throw<DomainException>().WithMessage("*twice*");
        GoodsReceipt.Create(po, D, "DN", "INV", null, new[] { ok }).Lines.Should().HaveCount(1);
    }

    [Fact]
    public void A_lot_never_goes_negative_and_a_count_returns_the_signed_difference()
    {
        var lot = Lot(10m, D.AddDays(10));
        FluentActions.Invoking(() => lot.Take(10.5m)).Should().Throw<DomainException>().WithMessage("*Only 10*");
        lot.Take(2.5m); lot.Quantity.Should().Be(7.5m);
        lot.SetCounted(9m).Should().Be(1.5m); lot.Quantity.Should().Be(9m);
        FluentActions.Invoking(() => lot.SetCounted(9m)).Should().Throw<DomainException>().WithMessage("*nothing to adjust*");
        lot.Add(1m, unitCost: 5m); lot.UnitCost.Amount.Should().Be(5m); lot.Value.Amount.Should().Be(50m);
        lot.IsExpired(D).Should().BeFalse(); lot.ExpiresWithin(D, 30).Should().BeTrue(); lot.ExpiresWithin(D, 5).Should().BeFalse(); lot.IsExpired(D.AddDays(11)).Should().BeTrue();
    }

    [Fact]
    public void A_movement_sign_follows_its_type_and_reads_the_balance_after_the_change()
    {
        var lot = Lot(10m);
        lot.Take(4m);
        var issue = StockMovement.Issue(lot, D, 4m, IssueReason.Consumption, "glu", "run");
        issue.Quantity.Should().Be(-4m); issue.BalanceAfter.Should().Be(6m); issue.Type.Should().BeSameAs(StockMovementType.Consumption); issue.TestCode.Should().Be("GLU");
        FluentActions.Invoking(() => StockMovement.Receipt(lot, D, -1m, GoodsReceiptId.New())).Should().Throw<DomainException>().WithMessage("*adds stock*");
        FluentActions.Invoking(() => StockMovement.Adjustment(lot, D, 0m, "Count", null)).Should().Throw<DomainException>().WithMessage("*must change*");
        StockMovement.Adjustment(lot, D, -1m, "Count", null).Quantity.Should().Be(-1m);
        StockMovement.Adjustment(lot, D, 2m, "Count", null).Quantity.Should().Be(2m);
    }

    [Fact]
    public void A_transfer_needs_two_different_stores_and_the_destination_cannot_confirm_more_than_was_sent()
    {
        var store = StoreId.New();
        var lot = StockLot.Open(InventoryItemId.New(), store, "L-1", D.AddMonths(2), 3m, D); lot.Add(50m);
        FluentActions.Invoking(() => StockTransfer.Create(store, store, D, null, new[] { StockTransferLine.FromLot(lot, 5m) })).Should().Throw<DomainException>().WithMessage("*must differ*");
        var t = StockTransfer.Create(store, StoreId.New(), D, null, new[] { StockTransferLine.FromLot(lot, 5m) });
        t.Status.Should().BeSameAs(TransferStatus.InTransit);
        var line = t.Lines.Single(); line.LotNumber.Should().Be("L-1"); line.ExpiryDate.Should().Be(D.AddMonths(2)); line.UnitCost.Amount.Should().Be(3m);
        FluentActions.Invoking(() => t.Receive(D, new Dictionary<StockTransferLineId, decimal> { [line.Id] = 6m }, null)).Should().Throw<DomainException>().WithMessage("*only 5*");
        FluentActions.Invoking(() => t.Receive(D.AddDays(-1), new Dictionary<StockTransferLineId, decimal>(), null)).Should().Throw<DomainException>().WithMessage("*before the transfer date*");
        t.Receive(D, new Dictionary<StockTransferLineId, decimal> { [line.Id] = 4m }, "one broken");
        line.ReceivedQuantity.Should().Be(4m); line.Shortfall.Should().Be(1m); t.Status.Should().BeSameAs(TransferStatus.Received);
        FluentActions.Invoking(() => t.Cancel(null)).Should().Throw<DomainException>();
    }

    [Fact]
    public void Master_data_names_are_required_and_bounded()
    {
        FluentActions.Invoking(() => Manufacturer.Create(" ", null, null)).Should().Throw<DomainException>();
        FluentActions.Invoking(() => Supplier.Create("D", null, null, "not-an-email", null, null)).Should().Throw<DomainException>().WithMessage("*Email*");
        FluentActions.Invoking(() => Store.Create("Main", "", null)).Should().Throw<DomainException>().WithMessage("*Branch*");
        var s = Store.Create(" Main ", " Cairo ", null); s.Name.Should().Be("Main"); s.Branch.Should().Be("Cairo");
    }
}
