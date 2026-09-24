using System.Collections.Concurrent;
using Ben.Data.Common.Constants;
using Ben.Data.Source.Context;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Store;

/// <summary>
/// The store's bells (storefront S4.5): the buyer's "your order is paid", and what the SuperAdmins
/// must hear — a new order, an order needing attention, a refund Stripe refused, and the two
/// configuration faults (sales tax, Link) at most once an hour each.
/// </summary>
/// <remarks>
/// <para><b>Never throws.</b> Every alert runs after the money has been recorded; a bell that
/// cannot be rung is logged, and the order it was about stands.</para>
///
/// <para><b>Admins: every SuperAdmin</b>, by bell and by letter (Ben, 09/24 — no separate alert
/// address). The sender is a SuperAdmin, never an invented id: a message's author is resolved to a
/// name on every screen that shows it.</para>
/// </remarks>
public sealed class StoreAlerts(
    IDbContextFactory<BenDataContext> dbFactory, PlatformMessageService messages, StoreOrderMailer mailer,
    ILogger<StoreAlerts> log, TimeProvider? clock = null)
{
    private static readonly ConcurrentDictionary<string, DateTime> LastSent = new();
    public static readonly TimeSpan ConfigurationAlertGap = TimeSpan.FromHours(1);

    private DateTime Now => (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;

    public static Task<List<Guid>> SuperAdminIdsAsync(BenDataContext db, CancellationToken ct)
        => (from userRole in db.Set<IdentityUserRole<Guid>>()
            join role in db.Set<IdentityRole<Guid>>() on userRole.RoleId equals role.Id
            where role.Name == RoleNames.SuperAdmin
            select userRole.UserId).Distinct().ToListAsync(ct);

    /// <summary>A paid order: the buyer's bell, and every SuperAdmin's bell and letter.</summary>
    public async Task OrderPaidAsync(Guid orderId, CancellationToken ct = default)
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var order = await db.StoreOrders.Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == orderId, ct);
            if (order is null) return;
            var admins = await SuperAdminIdsAsync(db, ct);

            if (order.BuyerAppUserId is { } buyer && admins.Count > 0)
                await messages.SendAsync($"Your order {order.OrderNumber} is paid",
                    $"Thank you — we have your payment for order {order.OrderNumber}. We will tell you when it ships. "
                  + $"See it under My Orders: {StoreOrderMailer.ViewPath(order)}",
                    [buyer], admins[0], ct);

            if (admins.Count == 0) { log.LogWarning("Store order {Number} was paid and there is no SuperAdmin to tell.", order.OrderNumber); return; }

            await messages.SendAsync($"New store order {order.OrderNumber}",
                $"Order {order.OrderNumber} ({StoreOrderMailer.Usd(order.Total)}) has been paid and is waiting to be packed: {StoreOrderMailer.AdminPath(order)}",
                admins, admins[0], ct);

            var emails = await db.AppUsers.AsNoTracking().Where(u => admins.Contains(u.Id) && u.Email != null)
                .Select(u => new { u.Email, u.DisplayName }).ToListAsync(ct);
            await mailer.QueueNewOrderAlertAsync(db, order, order.Items.ToList(),
                emails.Select(e => (e.Email!, e.DisplayName)).ToList(), Now, ct);
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogError(ex, "The alerts for paid store order {OrderId} could not be sent.", orderId);
        }
    }

    /// <summary>An order a person must look at before anything else happens to it.</summary>
    public Task OrderNeedsAttentionAsync(int orderNumber, Guid orderId, string reason, CancellationToken ct = default)
        => ToAdminsAsync($"Store order {orderNumber} needs attention",
            $"{reason} Open the order: /admin/store/orders/{orderId}", ct);

    /// <summary>A refund Stripe refused after accepting it.</summary>
    public Task RefundFailedAsync(int orderNumber, Guid orderId, string reason, CancellationToken ct = default)
        => ToAdminsAsync($"A refund on order {orderNumber} failed",
            $"A refund on order {orderNumber} failed at Stripe: {reason}. Nothing was restocked. /admin/store/orders/{orderId}", ct);

    /// <summary>Stripe Tax refused the store's own setup — once an hour at most.</summary>
    public Task TaxConfigurationAsync(string problem, CancellationToken ct = default)
        => HourlyAsync("tax", "The store can't work out sales tax",
            $"Stripe Tax refused a calculation, so checkouts are paused with a sentence: {problem} "
          + "Check the Stripe dashboard's tax settings and the store's ship-from address.", ct);

    /// <summary>Link is switched on here but not in the Stripe dashboard — once an hour at most.</summary>
    public Task LinkNotActivatedAsync(CancellationToken ct = default)
        => HourlyAsync("link", "Link isn't activated in the Stripe dashboard",
            "Link isn't activated in the Stripe dashboard — cards only. Activate it there, or turn Link off on the store settings page.", ct);

    private async Task HourlyAsync(string key, string subject, string body, CancellationToken ct)
    {
        var now = Now;
        if (LastSent.TryGetValue(key, out var last) && now - last < ConfigurationAlertGap) return;
        LastSent[key] = now;
        await ToAdminsAsync(subject, body, ct);
    }

    /// <summary>Forgets the hourly limits — tests only.</summary>
    internal static void ResetHourlyLimits() => LastSent.Clear();

    private async Task ToAdminsAsync(string subject, string body, CancellationToken ct)
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var admins = await SuperAdminIdsAsync(db, ct);
            if (admins.Count == 0) { log.LogWarning("Store alert with no SuperAdmin to tell: {Subject}", subject); return; }
            await messages.SendAsync(subject, body, admins, admins[0], ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogError(ex, "The store alert '{Subject}' could not be sent.", subject);
        }
    }
}
