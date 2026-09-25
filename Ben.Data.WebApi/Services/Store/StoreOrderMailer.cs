using System.Net;
using System.Text;
using Ben.Data.Common;
using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;
using Ben.Data.Common.Mail;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services.Mail;
using Ben.Service.Models.Store;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Ben.Data.WebApi.Services.Store;

/// <summary>
/// The store's letters (storefront S4.4): the receipt, the new-order alert and the way back to an
/// order. Every one goes through the outbox, in the caller's own transaction — a letter queued for
/// an order that then failed to save would tell somebody about an order that does not exist.
/// </summary>
/// <remarks>
/// <para><b>The link is the key for a guest.</b> A guest's order is opened by its private token
/// (<c>/store/orders/{id}?t=…</c>); a member's by signing in, so a member's letter never carries a
/// token that could outlive a password change.</para>
///
/// <para><b>Each letter records itself on the order</b> (a <c>LetterQueued</c> event naming its
/// kind), which is also how "at most one link letter per ten minutes" is enforced — a lookup form
/// must not become a way to fill somebody's inbox.</para>
/// </remarks>
public sealed class StoreOrderMailer(IOutboxEmailQueue queue, IOptions<SiteIdentity> site)
{
    public static readonly TimeSpan LinkLetterGap = TimeSpan.FromMinutes(10);

    private readonly SiteIdentity _site = site.Value;

    /// <summary>The order's page: the token for a guest, the plain address for a member (who signs in).</summary>
    public static string ViewPath(StoreOrder order)
        => order.BuyerAppUserId is null ? $"/store/orders/{order.Id}?t={order.AccessToken}" : $"/store/orders/{order.Id}";

    public static string AdminPath(StoreOrder order) => $"/admin/store/orders/{order.Id}";

    /// <summary>The receipt, once an order is paid.</summary>
    public async Task QueueConfirmationAsync(BenDataContext db, StoreOrder order, IReadOnlyList<StoreOrderItem> items, DateTime now, CancellationToken ct)
    {
        var url = _site.AbsoluteUrl(ViewPath(order));
        var body = new StringBuilder();
        body.Append($"<p>Hello {Safe(order.BuyerName)},</p>");
        body.Append($"<p>Thank you — we have your order <strong>{order.OrderNumber}</strong> and your payment. "
                  + "We will write again when it is on its way.</p>");
        body.Append(ItemsTable(items));
        body.Append(SummaryTable(order));
        body.Append($"<p>It is going to:<br>{AddressBlock(order)}</p>");
        body.Append("<p>Keep this email: its button is how you get back to your order.</p>");

        var supplied = new Dictionary<string, MailSuppliedValue>(StringComparer.OrdinalIgnoreCase)
        {
            ["OrderUrl"] = new(url),
            ["ItemsTable"] = new(ItemsTable(items), IsHtml: true),
            ["SummaryTable"] = new(SummaryTable(order), IsHtml: true),
            ["ShipTo"] = new(AddressBlock(order), IsHtml: true),
        };

        await QueueAsync(db, order, MailKinds.StoreOrderConfirmation, order.BuyerEmail,
            $"Your order {order.OrderNumber} from {_site.Name}",
            BenEmailLayout.Wrap(_site, "Thank you for your order", body.ToString(), "See your order", url),
            supplied, now, ct);
    }

    /// <summary>Tells each SuperAdmin a paid order is waiting.</summary>
    public async Task QueueNewOrderAlertAsync(BenDataContext db, StoreOrder order, IReadOnlyList<StoreOrderItem> items,
        IReadOnlyList<(string Email, string? Name)> admins, DateTime now, CancellationToken ct)
    {
        var url = _site.AbsoluteUrl(AdminPath(order));
        var table = ItemsTable(items);
        foreach (var (email, name) in admins)
        {
            var body = $"<p>Order <strong>{order.OrderNumber}</strong> has been paid and is waiting to be packed.</p>{table}";
            await queue.EnqueueAsync(db, new EmailMessage(
                email, $"New store order {order.OrderNumber}",
                BenEmailLayout.Wrap(_site, "A new store order", body, "Open the order", url),
                Kind: MailKinds.StoreOrderPlaced.Key,
                Payload: MailRows.For(MailKinds.StoreOrderPlaced,
                    new Dictionary<string, MailSuppliedValue>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["AdminOrderUrl"] = new(url),
                        ["ItemsTable"] = new(table, IsHtml: true),
                    },
                    MailRows.Person(email, name), order)), ct);
        }
        db.StoreOrderEvents.Add(Event(order, MailKinds.StoreOrderPlaced, now));
    }

    /// <summary>
    /// The way back to an order somebody asked to find. False when one went in the last ten
    /// minutes — nothing is queued, and the asker hears the same answer either way.
    /// </summary>
    public async Task<bool> QueueOrderLinkAsync(BenDataContext db, StoreOrder order, DateTime now, CancellationToken ct)
    {
        var key = MailKinds.StoreOrderLink.Key;
        var since = now - LinkLetterGap;
        if (await db.StoreOrderEvents.AnyAsync(e => e.OrderId == order.Id && e.Kind == StoreOrderEventKind.LetterQueued
                                                    && e.Note == key && e.OccurredUtc > since, ct))
            return false;

        var member = order.BuyerAppUserId is not null;
        var url = _site.AbsoluteUrl(member ? $"/login?returnUrl={Uri.EscapeDataString(ViewPath(order))}" : ViewPath(order));
        var body = member
            ? $"<p>Hello {Safe(order.BuyerName)},</p><p>You asked for a way back to order <strong>{order.OrderNumber}</strong>. "
              + "You placed it while signed in, so it is under My Orders — sign in to see it.</p>"
            : $"<p>Hello {Safe(order.BuyerName)},</p><p>You asked for a way back to order <strong>{order.OrderNumber}</strong>. "
              + "Here it is. Keep this email: the button is the only way back for an order placed without an account.</p>";

        await QueueAsync(db, order, MailKinds.StoreOrderLink, order.BuyerEmail, $"Your order {order.OrderNumber}",
            BenEmailLayout.Wrap(_site, "Your order", body, member ? "Sign in to see this order" : "See your order", url),
            new Dictionary<string, MailSuppliedValue>(StringComparer.OrdinalIgnoreCase) { ["OrderUrl"] = new(url) }, now, ct);
        return true;
    }

    /// <summary>
    /// "Your order is on its way" (S5.2): carrier, tracking and what is in the package. One letter per
    /// package (store sellers P7) — "package 1 of 2", with only that package's items. Outbox only.
    /// </summary>
    public async Task QueueShippedAsync(BenDataContext db, StoreOrder order, StoreOrderParcel parcel, IReadOnlyList<StoreOrderItem> items,
        int parcelCount, DateTime now, CancellationToken ct)
    {
        var url = _site.AbsoluteUrl(ViewPath(order));
        var tracking = parcel.TrackingUrl;
        var split = parcelCount > 1;
        var what = split
            ? $"Part of order <strong>{order.OrderNumber}</strong> — package {parcel.Number} of {parcelCount}, from {Safe(StoreParcelNames.ShipsFrom(parcel.SellerAppUserId, parcel.SellerName))} —"
            : $"Order <strong>{order.OrderNumber}</strong>";
        var body = new StringBuilder();
        body.Append($"<p>Hello {Safe(order.BuyerName)},</p>");
        body.Append(string.IsNullOrWhiteSpace(parcel.TrackingNumber)
            // "No tracking provided" (Ben, 09/24): not every parcel is tracked, and the letter says so plainly.
            ? $"<p>{what} has shipped with {Safe(parcel.Carrier)}. It was sent without "
              + "tracking, so there is no number to follow — it should reach you in the usual few days.</p>"
            : $"<p>{what} has shipped with {Safe(parcel.Carrier)}. "
              + $"The tracking number is <strong>{Safe(parcel.TrackingNumber)}</strong>.</p>");
        if (split) body.Append("<p>The rest of your order comes in its own package, with its own letter.</p>");
        body.Append(ItemsTable(items));
        body.Append($"<p>It is going to:<br>{AddressBlock(order)}</p>");

        var supplied = new Dictionary<string, MailSuppliedValue>(StringComparer.OrdinalIgnoreCase)
        {
            ["OrderUrl"] = new(url),
            ["Carrier"] = new(parcel.Carrier ?? ""),
            ["TrackingNumber"] = new(parcel.TrackingNumber ?? ""),
            ["TrackingUrl"] = new(tracking ?? url),
            ["ItemsTable"] = new(ItemsTable(items), IsHtml: true),
            ["Package"] = new(split ? $"Package {parcel.Number} of {parcelCount}" : ""),
        };
        await QueueAsync(db, order, MailKinds.StoreOrderShipped, order.BuyerEmail,
            split ? $"Part of your order {order.OrderNumber} is on its way (package {parcel.Number} of {parcelCount})" : $"Your order {order.OrderNumber} is on its way",
            BenEmailLayout.Wrap(_site, "Your order is on its way", body.ToString(),
                tracking is null ? "See your order" : "Track it", tracking ?? url),
            supplied, now, ct);
    }

    /// <summary>"A refund on your order" (S5.2) — sent only once Stripe says the money has gone back.</summary>
    /// <param name="lines">What was refunded by item, if the refund was by item; empty for a refund by amount.</param>
    public async Task QueueRefundedAsync(BenDataContext db, StoreOrder order, decimal amount, string reason,
        IReadOnlyList<(string Product, int Quantity)> lines, DateTime now, CancellationToken ct)
    {
        var url = _site.AbsoluteUrl(ViewPath(order));
        var refundLines = lines.Count == 0 ? "" : ItemsRefunded(lines);
        var body = new StringBuilder();
        body.Append($"<p>Hello {Safe(order.BuyerName)},</p>");
        body.Append($"<p>We have refunded <strong>{Usd(amount)}</strong> on order <strong>{order.OrderNumber}</strong>: {Safe(reason)}.</p>");
        body.Append(refundLines);
        body.Append("<p>It goes back to the card you paid with. Banks usually show it within 5–10 business days.</p>");

        var supplied = new Dictionary<string, MailSuppliedValue>(StringComparer.OrdinalIgnoreCase)
        {
            ["OrderUrl"] = new(url),
            ["RefundAmount"] = new(Usd(amount)),
            ["RefundReason"] = new(reason),
            ["RefundLines"] = new(refundLines, IsHtml: true),
        };
        await QueueAsync(db, order, MailKinds.StoreOrderRefunded, order.BuyerEmail,
            $"A refund on your order {order.OrderNumber}",
            BenEmailLayout.Wrap(_site, "A refund on your order", body.ToString(), "See your order", url),
            supplied, now, ct);
    }

    /// <summary>The morning's low stock (S5.6), one letter per SuperAdmin.</summary>
    public async Task QueueLowStockAsync(BenDataContext db, IReadOnlyList<(string Product, string? Variant, string Sku, int Left)> low,
        IReadOnlyList<(string Email, string? Name)> admins, CancellationToken ct)
    {
        var url = _site.AbsoluteUrl("/admin/store/stock?low=true");
        var rows = new StringBuilder();
        foreach (var (product, variant, sku, left) in low)
            rows.Append($"<tr><td style=\"{Cell}\">{Safe(product)}{(string.IsNullOrWhiteSpace(variant) ? "" : $" — {Safe(variant)}")}"
                      + $" <span style=\"color:#6b7280;\">{Safe(sku)}</span></td>"
                      + $"<td align=\"right\" style=\"{Cell}\">{left} left</td></tr>");
        var table = Table(rows.ToString());
        foreach (var (email, name) in admins)
        {
            await queue.EnqueueAsync(db, new EmailMessage(
                email, $"Store stock: {low.Count} running low",
                BenEmailLayout.Wrap(_site, "Store stock is running low",
                    "<p>These are at or under the store's low-stock number:</p>" + table, "Open the stock page", url),
                Kind: MailKinds.StoreLowStock.Key,
                Payload: MailRows.For(MailKinds.StoreLowStock,
                    new Dictionary<string, MailSuppliedValue>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["AdminStockUrl"] = new(url),
                        ["StockTable"] = new(table, IsHtml: true),
                    },
                    MailRows.Person(email, name))), ct);
        }
    }

    /// <summary>
    /// A package for a seller to send (store sellers P7): what goes in it and where it's going, and
    /// the way to their packages page. Written to the seller, never to the buyer.
    /// </summary>
    public async Task QueueSellerParcelAsync(BenDataContext db, StoreOrder order, StoreOrderParcel parcel, IReadOnlyList<StoreOrderItem> items,
        string sellerEmail, string? sellerName, DateTime now, CancellationToken ct)
    {
        var url = _site.AbsoluteUrl("/store/selling/packages");
        var table = ItemsTable(items);
        var shipTo = AddressBlock(order);
        var body = $"<p>Hello {Safe(sellerName ?? "there")},</p>"
                 + $"<p>Order <strong>{order.OrderNumber}</strong> is paid, and package {parcel.Number} is yours to send:</p>"
                 + table + $"<p>It is going to:<br>{shipTo}</p>"
                 + "<p>Buy the label as you usually do, then mark it shipped with its carrier and tracking number "
                 + $"(or \"No tracking provided\"). You're credited {Usd(parcel.SellerShippingCredit)} for the label.</p>";
        await queue.EnqueueAsync(db, new EmailMessage(
            sellerEmail, $"Order {order.OrderNumber}: a package for you to ship",
            BenEmailLayout.Wrap(_site, "A package for you to ship", body, "Open my packages", url),
            Kind: MailKinds.StoreSellerParcelToShip.Key,
            Payload: MailRows.For(MailKinds.StoreSellerParcelToShip,
                new Dictionary<string, MailSuppliedValue>(StringComparer.OrdinalIgnoreCase)
                {
                    ["SellerPackagesUrl"] = new(url),
                    ["ItemsTable"] = new(table, IsHtml: true),
                    ["ShipTo"] = new(shipTo, IsHtml: true),
                },
                MailRows.Person(sellerEmail, sellerName), order)), ct);
        db.StoreOrderEvents.Add(new StoreOrderEvent
        {
            Id = Guid.NewGuid(), OrderId = order.Id, ParcelId = parcel.Id, Kind = StoreOrderEventKind.LetterQueued,
            Note = MailKinds.StoreSellerParcelToShip.Key, OccurredUtc = now,
        });
    }

    private static string ItemsRefunded(IReadOnlyList<(string Product, int Quantity)> lines)
    {
        var rows = new StringBuilder();
        foreach (var (product, quantity) in lines)
            rows.Append($"<tr><td style=\"{Cell}\">{quantity} × {Safe(product)}</td></tr>");
        return Table(rows.ToString());
    }

    private static string Table(string rows)
        => "<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" "
         + "style=\"margin:0 0 16px 0;font-family:Arial,Helvetica,sans-serif;font-size:15px;color:#374151;\">" + rows + "</table>";

    private async Task QueueAsync(BenDataContext db, StoreOrder order, MailKindInfo kind, string to, string subject, string html,
        IReadOnlyDictionary<string, MailSuppliedValue> supplied, DateTime now, CancellationToken ct)
    {
        await queue.EnqueueAsync(db, new EmailMessage(to, subject, html, ReplyTo: null, Kind: kind.Key,
            Payload: MailRows.For(kind, supplied, MailRows.Person(order.BuyerEmail, order.BuyerName), order)), ct);
        db.StoreOrderEvents.Add(Event(order, kind, now));
    }

    private static StoreOrderEvent Event(StoreOrder order, MailKindInfo kind, DateTime now) => new()
    {
        Id = Guid.NewGuid(), OrderId = order.Id, Kind = StoreOrderEventKind.LetterQueued, Note = kind.Key, OccurredUtc = now,
    };

    // ── the pieces every store letter draws ──────────────────────────────────

    public static string Usd(decimal amount) => StoreMoney.Format(amount);

    private static string Safe(string? s) => WebUtility.HtmlEncode(s ?? "");

    private const string Cell = "padding:8px 0;border-bottom:1px solid #e5e7eb;";

    public static string ItemsTable(IReadOnlyList<StoreOrderItem> items)
    {
        var rows = new StringBuilder();
        foreach (var i in items)
        {
            var name = Safe(i.ProductName) + (string.IsNullOrWhiteSpace(i.VariantName) ? "" : $" — {Safe(i.VariantName)}");
            rows.Append($"<tr><td style=\"{Cell}\">{i.Quantity} × {name}</td>"
                      + $"<td align=\"right\" style=\"{Cell}\">{Usd(i.LineTotal)}</td></tr>");
        }
        return "<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" "
             + "style=\"margin:0 0 16px 0;font-family:Arial,Helvetica,sans-serif;font-size:15px;color:#374151;\">"
             + rows + "</table>";
    }

    public static string SummaryTable(StoreOrder o)
    {
        static string Row(string what, string amount, bool strong = false)
            => $"<tr><td style=\"padding:4px 0;{(strong ? "font-weight:bold;color:#111827;" : "")}\">{what}</td>"
             + $"<td align=\"right\" style=\"padding:4px 0;{(strong ? "font-weight:bold;color:#111827;" : "")}\">{amount}</td></tr>";

        var sb = new StringBuilder("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" "
                                 + "style=\"margin:0 0 16px 0;font-family:Arial,Helvetica,sans-serif;font-size:15px;color:#374151;\">");
        sb.Append(Row("Products", Usd(o.Subtotal)));
        if (o.DiscountAmount > 0) sb.Append(Row($"Discount{(o.CouponCode is { } c ? $" ({Safe(c)})" : "")}", "−" + Usd(o.DiscountAmount)));
        sb.Append(Row("Shipping", o.ShippingAmount == 0 ? "Free" : Usd(o.ShippingAmount)));
        sb.Append(Row("Sales tax", Usd(o.TaxAmount)));
        sb.Append(Row("Total", Usd(o.Total), strong: true));
        return sb.Append("</table>").ToString();
    }

    public static string AddressBlock(StoreOrder o)
        => string.Join("<br>", new[]
        {
            o.ShipName, o.ShipStreet1, o.ShipStreet2, $"{o.ShipCity}, {o.ShipState} {o.ShipZip}",
        }.Where(l => !string.IsNullOrWhiteSpace(l)).Select(Safe));
}
