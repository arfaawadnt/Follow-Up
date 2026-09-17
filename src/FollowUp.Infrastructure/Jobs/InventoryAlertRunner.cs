using FollowUp.Application.Common.Abstractions;
using FollowUp.Application.Features.Inventory;
using FollowUp.Domain.Identity;
using FollowUp.Domain.Notifications;
using FollowUp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FollowUp.Infrastructure.Jobs;

/// <summary>
/// The inventory alert pass. Evaluates the stock limits and expiry dates with the same read the Stock page uses (global
/// scope), then writes ONE in-app summary per recipient per day: every active user whose role grants ViewInventory, with
/// the alert list narrowed to the stores their role's Branch scope covers (store-less low-stock alerts are global and go
/// to everyone). A user who already has today's summary is skipped, so the manual "Check alerts now" button and the
/// 06:00 job never double-post. Runs as the system principal.
/// </summary>
internal sealed class InventoryAlertRunner : IInventoryAlertRunner
{
    public const string EventKey = "inventory.alerts";

    private readonly FollowUpDbContext _db;
    private readonly IInventoryQueries _queries;
    private readonly INotificationRecipients _recipients;
    private readonly IClock _clock;
    private readonly ILogger<InventoryAlertRunner> _logger;

    public InventoryAlertRunner(FollowUpDbContext db, IInventoryQueries queries, INotificationRecipients recipients, IClock clock, ILogger<InventoryAlertRunner> logger)
    {
        _db = db; _queries = queries; _recipients = recipients; _clock = clock; _logger = logger;
    }

    public async Task<InventoryAlertResult> RunAsync(DateOnly today, bool manual, CancellationToken ct)
    {
        var all = await _queries.AlertsAsync(OrgScope.Global, today, ct);
        var low = all.Count(a => a.Kind == "LowStock"); var outOf = all.Count(a => a.Kind == "OutOfStock");
        var expiring = all.Count(a => a.Kind == "Expiring"); var expired = all.Count(a => a.Kind == "Expired");
        var recipients = 0;

        if (all.Count > 0)
        {
            var storeBranches = await _db.Stores.AsNoTracking().ToDictionaryAsync(s => s.Id.Value, s => s.Branch, ct);
            // "Today" for the once-a-day rule is the clock's Cairo day (the same clock stamps CreatedAt), not the evaluation date.
            var cairoNow = _clock.CairoNow;
            var dayStart = new DateTimeOffset(cairoNow.Date, cairoNow.Offset).ToUniversalTime(); // timestamptz parameters must be UTC
            var users = await _recipients.ForPrivilegeAsync(Privileges.ViewInventory, ct);
            var userIds = users.Select(u => new AppUserId(u.UserId)).ToList();
            var alreadyToday = (await _db.SystemNotifications.AsNoTracking()
                .Where(n => n.EventKey == EventKey && n.CreatedAt >= dayStart && userIds.Contains(n.RecipientUserId))
                .Select(n => n.RecipientUserId).ToListAsync(ct)).ToHashSet();

            foreach (var u in users)
            {
                var uid = new AppUserId(u.UserId);
                if (alreadyToday.Contains(uid)) continue;
                var mine = all.Where(a => a.StoreId is null || (storeBranches.TryGetValue(a.StoreId.Value, out var b) && StoreScope.IsVisible(u.Scope, b))).ToList();
                if (mine.Count == 0) continue;
                var ar = string.Equals(u.Language, "ar", StringComparison.OrdinalIgnoreCase);
                var title = ar ? $"تنبيهات المخزون — {mine.Count}" : $"Inventory alerts — {mine.Count}";
                var body = Summary(mine, ar);
                _db.SystemNotifications.Add(SystemNotification.Create(uid, EventKey, title, body, _clock.UtcNow));
                recipients++;
            }
            await _db.SaveChangesAsync(ct);
        }

        _logger.LogInformation("Inventory alerts ({Mode}) for {Day}: low={Low} out={Out} expiring={Expiring} expired={Expired} → {Recipients} recipient(s)",
            manual ? "manual" : "scheduled", today, low, outOf, expiring, expired, recipients);
        return new InventoryAlertResult(low, outOf, expiring, expired, recipients);
    }

    private static string Summary(List<InventoryAlertDto> alerts, bool ar)
    {
        int C(string k) => alerts.Count(a => a.Kind == k);
        var head = ar
            ? $"منتهي الصلاحية: {C("Expired")} · نفد المخزون: {C("OutOfStock")} · مخزون منخفض: {C("LowStock")} · يقارب الانتهاء: {C("Expiring")}"
            : $"Expired: {C("Expired")} · Out of stock: {C("OutOfStock")} · Low stock: {C("LowStock")} · Expiring soon: {C("Expiring")}";
        var lines = alerts.Take(8).Select(a => "• " + a.Message);
        var more = alerts.Count > 8 ? (ar ? $"\n… و{alerts.Count - 8} تنبيهات أخرى في صفحة المخزون" : $"\n… and {alerts.Count - 8} more on the Stock page") : "";
        return head + "\n" + string.Join("\n", lines) + more;
    }
}
