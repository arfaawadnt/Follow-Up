using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace FollowUp.ApiTests;

/// <summary>
/// Inventory module over the wire: master data → a purchase order submitted and received (lot + expiry) → the stock read
/// shows the lot → a transfer to a second store is dispatched and confirmed short → a manual issue → the movement ledger,
/// the alerts and the utilization report answer; the state machine refuses an out-of-order step with 409.
/// </summary>
[Collection("api")]
public sealed class InventoryContractTests
{
    private readonly ApiFixture _fx;
    public InventoryContractTests(ApiFixture fx) => _fx = fx;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [SkippableFact]
    public async Task Purchase_receive_transfer_issue_and_report_round_trip()
    {
        Skip.IfNot(_fx.AuthReady, "admin token unavailable (FOLLOWUP_DB / seeded admin).");
        using var c = _fx.CreateAuthedClient();
        var tag = Guid.NewGuid().ToString("N")[..8];
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);

        var mfr = await PostIdAsync(c, "/api/v1/inventory/manufacturers", new { name = $"Mfr {tag}", country = "DE" });
        var sup = await PostIdAsync(c, "/api/v1/inventory/suppliers", new { name = $"Dist {tag}", contactPerson = "Ali", email = "ali@dist.test" });
        var main = await PostIdAsync(c, "/api/v1/inventory/stores", new { name = $"Main {tag}", branch = "Cairo", location = "Room 1" });
        var giza = await PostIdAsync(c, "/api/v1/inventory/stores", new { name = $"Giza {tag}", branch = "Giza" });
        var item = await PostIdAsync(c, "/api/v1/inventory/items", new
        {
            code = $"REA-{tag}",
            name = "Glucose reagent",
            kind = "Chemical",
            manufacturerId = mfr,
            catalogNumber = "R-1",
            unit = "mL",
            minStock = 100,
            reorderQuantity = 500,
            expiryWarningDays = 30,
            testLinks = new[] { new { testCode = "GLU", testType = 0, testName = "Glucose", quantityPerTest = 0.5 } },
        });
        (await c.PostAsJsonAsync("/api/v1/inventory/items", new { code = $"REA-{tag}", name = "Dup", kind = "Chemical", manufacturerId = mfr, unit = "mL", minStock = 0, reorderQuantity = 0, expiryWarningDays = 30 }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict, "duplicate item code");

        // Purchase order: created as a draft, then submitted; receiving a draft is a 409.
        var po = await PostIdAsync(c, "/api/v1/inventory/purchase-orders", new
        {
            supplierId = sup,
            storeId = main,
            orderDate = today,
            expectedDate = today.AddDays(5),
            reference = "Q-1",
            lines = new[] { new { itemId = item, orderedQuantity = 1000, unitPrice = 2.5, notes = (string?)null } },
        });
        var detail = await GetJsonAsync(c, $"/api/v1/inventory/purchase-orders/{po}");
        detail.GetProperty("status").GetString().Should().Be("Draft");
        detail.GetProperty("number").GetString().Should().StartWith("PO-");
        var lineId = detail.GetProperty("lines")[0].GetProperty("id").GetGuid();
        (await c.PostAsJsonAsync($"/api/v1/inventory/purchase-orders/{po}/receive", new { receivedDate = today, lines = new[] { new { orderLineId = lineId, quantity = 10, lotNumber = "L-1" } } }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await c.PostAsync($"/api/v1/inventory/purchase-orders/{po}/submit", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Receive 600 of 1000 into lot L-1 expiring in 6 months; over-receipt is a 409.
        (await c.PostAsJsonAsync($"/api/v1/inventory/purchase-orders/{po}/receive", new { receivedDate = today, deliveryNote = "DN-1", lines = new[] { new { orderLineId = lineId, quantity = 600, lotNumber = "L-1", expiryDate = today.AddMonths(6) } } }))
            .StatusCode.Should().Be(HttpStatusCode.Created);
        (await c.PostAsJsonAsync($"/api/v1/inventory/purchase-orders/{po}/receive", new { receivedDate = today, lines = new[] { new { orderLineId = lineId, quantity = 401, lotNumber = "L-2" } } }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict, "401 exceeds the outstanding 400");
        detail = await GetJsonAsync(c, $"/api/v1/inventory/purchase-orders/{po}");
        detail.GetProperty("status").GetString().Should().Be("PartiallyReceived");
        detail.GetProperty("receipts").GetArrayLength().Should().Be(1);

        // Stock + lots show the received quantity in the main store.
        var stock = await GetJsonAsync(c, $"/api/v1/inventory/stock?search=REA-{tag}");
        stock.EnumerateArray().Single().GetProperty("onHand").GetDecimal().Should().Be(600m);
        var lots = await GetJsonAsync(c, $"/api/v1/inventory/lots?itemId={item}");
        var lot = lots.EnumerateArray().Single();
        lot.GetProperty("lotNumber").GetString().Should().Be("L-1"); lot.GetProperty("status").GetString().Should().Be("Ok");
        var lotId = lot.GetProperty("id").GetGuid();

        // Transfer 200 to Giza, confirm 190 (one bottle broken).
        var tr = await PostIdAsync(c, "/api/v1/inventory/transfers", new { fromStoreId = main, toStoreId = giza, date = today, lines = new[] { new { lotId, quantity = 200 } } });
        var trDetail = await GetJsonAsync(c, $"/api/v1/inventory/transfers/{tr}");
        trDetail.GetProperty("status").GetString().Should().Be("InTransit");
        var trLine = trDetail.GetProperty("lines")[0].GetProperty("id").GetGuid();
        (await c.PostAsJsonAsync($"/api/v1/inventory/transfers/{tr}/receive", new { receivedDate = today, lines = new[] { new { lineId = trLine, receivedQuantity = 190 } }, notes = "one broken" }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await c.PostAsJsonAsync($"/api/v1/inventory/transfers/{tr}/cancel", new { notes = "" })).StatusCode.Should().Be(HttpStatusCode.Conflict, "already received");
        var gizaLots = await GetJsonAsync(c, $"/api/v1/inventory/lots?itemId={item}&storeId={giza}");
        gizaLots.EnumerateArray().Single().GetProperty("quantity").GetDecimal().Should().Be(190m);

        // Issue 50 mL for glucose; the ledger has receipt, transfer out, transfer in and the consumption.
        (await c.PostAsJsonAsync("/api/v1/inventory/issues", new { lotId, date = today, quantity = 50, reason = "Consumption", testCode = "GLU" })).StatusCode.Should().Be(HttpStatusCode.Created);
        (await c.PostAsJsonAsync("/api/v1/inventory/issues", new { lotId, date = today, quantity = 5000, reason = "Consumption" })).StatusCode.Should().Be(HttpStatusCode.Conflict, "not on hand");
        var moves = await GetJsonAsync(c, $"/api/v1/inventory/movements?from={today:yyyy-MM-dd}&to={today:yyyy-MM-dd}&itemId={item}");
        moves.EnumerateArray().Select(m => m.GetProperty("type").GetString()).Should().BeEquivalentTo(new[] { "Receipt", "TransferOut", "TransferIn", "Consumption" });
        (await GetJsonAsync(c, $"/api/v1/inventory/stock?search=REA-{tag}")).EnumerateArray().Single().GetProperty("onHand").GetDecimal().Should().Be(540m, "600 − 200 + 190 − 50");

        // Reports + alerts answer.
        (await c.GetAsync($"/api/v1/inventory/utilization?from={today.AddDays(-7):yyyy-MM-dd}&to={today:yyyy-MM-dd}&itemId={item}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await c.GetAsync("/api/v1/inventory/dashboard")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await c.GetAsync("/api/v1/inventory/alerts")).StatusCode.Should().Be(HttpStatusCode.OK);
        var run = await c.PostAsync("/api/v1/inventory/alerts/run", null);
        run.StatusCode.Should().Be(HttpStatusCode.OK);
        (await c.GetAsync($"/api/v1/inventory/receipts?from={today:yyyy-MM-dd}&to={today:yyyy-MM-dd}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static async Task<Guid> PostIdAsync(HttpClient c, string url, object body)
    {
        var r = await c.PostAsJsonAsync(url, body);
        r.StatusCode.Should().Be(HttpStatusCode.Created, $"POST {url} should create: {await r.Content.ReadAsStringAsync()}");
        return (await r.Content.ReadFromJsonAsync<IdResponse>())!.Id;
    }

    private static async Task<JsonElement> GetJsonAsync(HttpClient c, string url)
    {
        var r = await c.GetAsync(url);
        r.StatusCode.Should().Be(HttpStatusCode.OK, $"GET {url}");
        return JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    private sealed record IdResponse(Guid Id);
}
