using FluentAssertions;
using FollowUp.Application.Common.Abstractions;
using FollowUp.Application.Features.Inventory;
using FollowUp.Domain.Common;
using FollowUp.Domain.Identity;
using FollowUp.Domain.Inventory;
using FollowUp.Domain.Statistics;
using FollowUp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace FollowUp.IntegrationTests;

/// <summary>
/// Inventory module at the persistence layer and through the real read side: documents with their owned lines round-trip
/// (enumerations, Money, numeric(18,3) quantities), Serial is DB-generated, the (item, store, lot) key is unique, the
/// raw-SQL CHECKs hold against writes that bypass the domain (23514), and the computed reads — stock rows, alerts,
/// utilization — derive the right figures from real rows, scoped to the caller's branches.
/// </summary>
[Collection("integration")]
public sealed class InventoryPersistenceTests
{
    private readonly IntegrationFixture _fx;
    public InventoryPersistenceTests(IntegrationFixture fx) => _fx = fx;
    private static readonly DateOnly D = new(2026, 9, 17);
    private static string Tag() => Guid.NewGuid().ToString("N")[..8];

    [SkippableFact]
    public async Task Documents_round_trip_serials_are_generated_checks_hold_and_the_reads_compute_from_real_rows()
    {
        Skip.IfNot(_fx.DatabaseAvailable, "FOLLOWUP_DB not set.");
        var tag = Tag();
        Guid itemId, tubesId, mainId, gizaId, poId, lotAId;

        using (var scope = _fx.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
            var roche = Manufacturer.Create($"Roche {tag}", "CH", null); db.Manufacturers.Add(roche);
            var dist = Supplier.Create($"MedDist {tag}", "Ali", "0100", "a@b.c", null, null); db.Suppliers.Add(dist);
            var main = Store.Create($"Main {tag}", "Cairo", "Room 1"); var giza = Store.Create($"Giza {tag}", "Giza", null); db.Stores.AddRange(main, giza);
            var reagent = InventoryItem.Create($"REA-{tag}", "Glucose reagent", ItemKind.Chemical, roche.Id, "R-100", "mL", 500m, 2000m, 30, "2-8°C", null);
            reagent.SetTestLinks(new[] { ItemTestLink.Create($"GLU{tag[..4]}", 0, "Glucose", 0.25m) });
            var tubes = InventoryItem.Create($"TUB-{tag}", "EDTA tubes", ItemKind.Consumable, roche.Id, null, "pcs", 100m, 0m, 60, null, null);
            db.InventoryItems.AddRange(reagent, tubes);

            var po = PurchaseOrder.Create(dist.Id, main.Id, D, D.AddDays(7), "Q-1", "urgent",
                new[] { PurchaseOrderLine.Create(reagent.Id, 1000m, 2.5m, null), PurchaseOrderLine.Create(tubes.Id, 500m, 0.4m, "boxes of 100") });
            po.Submit(D);
            db.PurchaseOrders.Add(po);

            // Receive 600 mL (lot A, expiring in 20 days → "expiring") + 200 tubes; open the lots and write the ledger.
            var reagentLine = po.Lines.Single(l => l.ItemId == reagent.Id); var tubeLine = po.Lines.Single(l => l.ItemId == tubes.Id);
            var gr = GoodsReceipt.Create(po, D.AddDays(2), "DN-1", null, null, new[]
            {
                GoodsReceiptLine.Create(reagentLine.Id, reagent.Id, 600m, "LOT-A", D.AddDays(20), 2.5m, D.AddDays(2)),
                GoodsReceiptLine.Create(tubeLine.Id, tubes.Id, 200m, "T-1", null, 0.4m, D.AddDays(2)),
            });
            po.Receive(reagentLine.Id, 600m); po.Receive(tubeLine.Id, 200m);
            var lotA = StockLot.Open(reagent.Id, main.Id, "LOT-A", D.AddDays(20), 2.5m, D.AddDays(2)); lotA.Add(600m);
            var lotT = StockLot.Open(tubes.Id, main.Id, "T-1", null, 0.4m, D.AddDays(2)); lotT.Add(200m);
            db.GoodsReceipts.Add(gr); db.StockLots.AddRange(lotA, lotT);
            db.StockMovements.Add(StockMovement.Receipt(lotA, D.AddDays(2), 600m, gr.Id));
            db.StockMovements.Add(StockMovement.Receipt(lotT, D.AddDays(2), 200m, gr.Id));
            // Consume 100 mL against glucose over two days; the test statistics say 300 glucose tests were run → expected 75 mL.
            lotA.Take(60m); db.StockMovements.Add(StockMovement.Issue(lotA, D.AddDays(3), 60m, IssueReason.Consumption, $"GLU{tag[..4]}", null));
            lotA.Take(40m); db.StockMovements.Add(StockMovement.Issue(lotA, D.AddDays(4), 40m, IssueReason.Consumption, null, null));
            var s1 = TestStatistic.For(D.AddDays(3), $"GLU{tag[..4]}", 0, "CAI"); s1.SetCount(200); s1.SetIncome(new Money(4000m));
            var s2 = TestStatistic.For(D.AddDays(4), $"GLU{tag[..4]}", 0, "CAI"); s2.SetCount(100); s2.SetIncome(new Money(2000m));
            db.TestStatistics.AddRange(s1, s2);
            await db.SaveChangesAsync();
            itemId = reagent.Id.Value; tubesId = tubes.Id.Value; mainId = main.Id.Value; gizaId = giza.Id.Value; poId = po.Id.Value; lotAId = lotA.Id.Value;
        }

        using (var scope = _fx.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
            var po = await db.PurchaseOrders.AsNoTracking().SingleAsync(p => p.Id == new PurchaseOrderId(poId));
            po.Serial.Should().BeGreaterThan(0); po.Number.Should().Be($"PO-{po.Serial:D5}");
            po.Status.Should().BeSameAs(PurchaseOrderStatus.PartiallyReceived);
            po.Lines.Should().HaveCount(2, "owned lines load with the order"); po.Lines.Single(l => l.ItemId.Value == tubesId).Notes.Should().Be("boxes of 100");
            po.Total.Amount.Should().Be(2700m);
            var gr = await db.GoodsReceipts.AsNoTracking().SingleAsync(g => g.PurchaseOrderId == po.Id);
            gr.Serial.Should().BeGreaterThan(0); gr.Lines.Should().HaveCount(2); gr.Lines.Single(l => l.LotNumber == "LOT-A").ExpiryDate.Should().Be(D.AddDays(20));
            var item = await db.InventoryItems.AsNoTracking().SingleAsync(i => i.Id == new InventoryItemId(itemId));
            item.TestLinks.Should().ContainSingle(t => t.QuantityPerTest == 0.25m);
            (await db.StockMovements.AsNoTracking().Where(m => m.ItemId == item.Id).Select(m => m.Quantity).ToListAsync())
                .Should().BeEquivalentTo(new[] { 600m, -60m, -40m }, "a batched multi-row INSERT does not guarantee identity order");
            (await db.StockMovements.AsNoTracking().Where(m => m.ItemId == item.Id).Select(m => m.BalanceAfter).MinAsync()).Should().Be(500m, "the last consumption left 500 mL in the lot");

            // (item, store, lot number) is unique.
            db.StockLots.Add(StockLot.Open(item.Id, new StoreId(mainId), "LOT-A", null, 1m, D));
            await FluentActions.Awaiting(() => db.SaveChangesAsync()).Should().ThrowAsync<DbUpdateException>();
        }

        // The raw-SQL CHECKs refuse what the domain refuses.
        using (var scope = _fx.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
            foreach (var sql in new[]
            {
                $"UPDATE stock_lot SET quantity = -1 WHERE id = '{lotAId}'",
                $"UPDATE purchase_order_line SET received_quantity = ordered_quantity + 1 WHERE purchase_order_id = '{poId}'",
                $"UPDATE stock_movement SET quantity = 5 WHERE lot_id = '{lotAId}' AND type = 'Consumption'",
                $"UPDATE inventory_item SET expiry_warning_days = 800 WHERE id = '{itemId}'",
            })
            {
                var ex = await FluentActions.Awaiting(() => db.Database.ExecuteSqlRawAsync(sql)).Should().ThrowAsync<PostgresException>(sql);
                ex.Which.SqlState.Should().Be("23514", sql);
            }
        }

        // The read side: stock rows, lots, alerts and utilization — for a global caller and for a Giza-only caller.
        using (var scope = _fx.Services.CreateScope())
        {
            var q = scope.ServiceProvider.GetRequiredService<IInventoryQueries>();
            var today = D.AddDays(5);
            var stock = await q.StockAsync(OrgScope.Global, null, null, tag, false, today, CancellationToken.None);
            var reagentRow = stock.Single(r => r.ItemId == itemId);
            reagentRow.OnHand.Should().Be(500m); reagentRow.IsLow.Should().BeTrue("500 on hand equals the limit of 500"); reagentRow.IsOut.Should().BeFalse();
            reagentRow.ExpiringQuantity.Should().Be(500m, "LOT-A expires in 15 days, inside the 30-day window"); reagentRow.Value.Should().Be(1250m); reagentRow.LotCount.Should().Be(1);
            var tubesRow = stock.Single(r => r.ItemId == tubesId);
            tubesRow.OnHand.Should().Be(200m); tubesRow.IsLow.Should().BeFalse(); tubesRow.ExpiringQuantity.Should().Be(0m);

            var lots = await q.LotsAsync(OrgScope.Global, itemId, null, false, today, CancellationToken.None);
            lots.Should().ContainSingle().Which.Status.Should().Be("Expiring");

            var alerts = await q.AlertsAsync(OrgScope.Global, today, CancellationToken.None);
            alerts.Where(a => a.ItemId == itemId).Select(a => a.Kind).Should().BeEquivalentTo(new[] { "LowStock", "Expiring" });
            alerts.Where(a => a.ItemId == tubesId).Should().BeEmpty();

            // A caller scoped to Giza sees the items but none of the Cairo store's stock — and the Giza store is empty.
            var giza = OrgScope.Create(new[] { "Giza" }, new[] { "*" }, new[] { "*" }, new[] { "*" }, new[] { "*" }, new[] { "*" });
            (await q.StockAsync(giza, null, null, tag, false, today, CancellationToken.None)).Single(r => r.ItemId == itemId).OnHand.Should().Be(0m);
            (await q.LotsAsync(giza, itemId, null, false, today, CancellationToken.None)).Should().BeEmpty();
            (await q.StoresAsync(giza, false, CancellationToken.None)).Select(s => s.Id).Should().Contain(gizaId).And.NotContain(mainId);
            (await q.PurchaseOrderAsync(poId, giza, CancellationToken.None)).Should().BeNull("the order belongs to the Cairo store");
            var mine = await q.PurchaseOrderAsync(poId, OrgScope.Global, CancellationToken.None);
            mine!.Receipts.Should().HaveCount(1); mine.ReceivedQuantity.Should().Be(800m); mine.Lines.Single(l => l.ItemId == itemId).Outstanding.Should().Be(400m);

            // Utilization: 300 glucose tests × 0.25 mL = 75 mL expected against 100 mL actually issued.
            var util = await q.UtilizationAsync(OrgScope.Global, D, D.AddDays(5), null, itemId, CancellationToken.None);
            var row = util.Should().ContainSingle().Subject;
            row.TestsPerformed.Should().Be(300); row.Expected.Should().Be(75m); row.Actual.Should().Be(100m); row.Variance.Should().Be(25m); row.UtilizationPct.Should().Be(75.0m);
            // Narrowed to the Cairo store whose branch name has no Branch reference code → all branches count (no silent zero).
            (await q.UtilizationAsync(OrgScope.Global, D, D.AddDays(5), mainId, itemId, CancellationToken.None)).Single().Expected.Should().Be(75m);
        }

        // The alert runner writes one in-app summary per ViewInventory user and does not double-post the same day.
        using (var scope = _fx.Services.CreateScope())
        {
            var runner = scope.ServiceProvider.GetRequiredService<IInventoryAlertRunner>();
            var first = await runner.RunAsync(D.AddDays(5), manual: true, CancellationToken.None);
            first.LowStock.Should().BeGreaterThanOrEqualTo(1); first.Expiring.Should().BeGreaterThanOrEqualTo(1); first.Recipients.Should().BeGreaterThan(0, "the seeded admin holds every privilege");
            var second = await runner.RunAsync(D.AddDays(5), manual: true, CancellationToken.None);
            second.Recipients.Should().Be(0, "today's summary already exists for every recipient");
            var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
            (await db.SystemNotifications.AsNoTracking().Where(n => n.EventKey == "inventory.alerts").ToListAsync()).Should().NotBeEmpty()
                .And.OnlyContain(n => n.Title.Contains("Inventory alerts") || n.Title.Contains("تنبيهات"));
        }
    }
}
