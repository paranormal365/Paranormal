using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Service.Models.Store;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Store;

/// <summary>The order list's filters, as the page and the CSV export both ask them.</summary>
/// <param name="NeedsAction">Paid or packed (waiting to go out), or flagged for attention.</param>
/// <param name="RefundAttention">A refund on it failed, or never reached Stripe.</param>
/// <param name="IncludeUnpaid">Checkouts that were never paid and are finished (expired, abandoned) — hidden unless asked.</param>
/// <param name="Attention">Flagged for attention only — what the dashboard's tile counts.</param>
public sealed record StoreOrderFilter(
    StoreOrderStatus? Status = null, string? Q = null, string? Coupon = null, DateTime? From = null, DateTime? To = null,
    bool NeedsAction = false, bool RefundAttention = false, bool IncludeUnpaid = false, bool Attention = false);

/// <summary>
/// What the order desk reads (storefront S5.4): the filtered list, one order with its history, what
/// may be done with it, and the CSV rows.
/// </summary>
public static class StoreOrderDesk
{
    public static IQueryable<StoreOrder> Filtered(BenDataContext db, StoreOrderFilter f, DateTime now)
    {
        var q = db.StoreOrders.AsNoTracking();
        if (f.Status is { } status) q = q.Where(o => o.Status == status);
        else if (!f.IncludeUnpaid) q = q.Where(o => o.PaidUtc != null || o.Status == StoreOrderStatus.PendingPayment);

        if (!string.IsNullOrWhiteSpace(f.Q))
        {
            var term = f.Q.Trim().TrimStart('#');
            var lower = term.ToLower();
            var upper = term.ToUpper();
            int? number = int.TryParse(term, out var n) ? n : null;
            q = q.Where(o => (number != null && o.OrderNumber == number)
                          || o.BuyerEmailNormalized.Contains(upper)
                          || o.BuyerName.ToLower().Contains(lower)
                          || o.Parcels.Any(x => x.TrackingNumber != null && x.TrackingNumber.ToLower().Contains(lower))
                          || o.Items.Any(i => i.Sku.ToLower().Contains(lower) || i.ProductName.ToLower().Contains(lower)));
        }
        if (!string.IsNullOrWhiteSpace(f.Coupon))
        {
            var code = f.Coupon.Trim().ToUpper();
            q = q.Where(o => o.CouponCode != null && o.CouponCode.ToUpper() == code);
        }
        if (f.From is { } from) q = q.Where(o => o.PlacedUtc >= from);
        if (f.To is { } to) q = q.Where(o => o.PlacedUtc < to.Date.AddDays(1));
        if (f.NeedsAction)
            q = q.Where(o => o.NeedsAttention || o.Status == StoreOrderStatus.Paid || o.Status == StoreOrderStatus.Packed
                          || o.Status == StoreOrderStatus.PartiallyShipped);
        if (f.Attention) q = q.Where(o => o.NeedsAttention);
        if (f.RefundAttention)
        {
            var stale = now - StoreRefundService.StaleAfter;
            q = q.Where(o => o.Refunds.Any(r => r.Status == StoreRefundStatus.Failed
                                             || (r.Status == StoreRefundStatus.Pending && r.StripeRefundId == null && r.DateCreated < stale)));
        }
        return q;
    }

    public static async Task<List<StoreOrderListRecord>> ListAsync(BenDataContext db, StoreOrderFilter f, DateTime now, CancellationToken ct)
    {
        var stale = now - StoreRefundService.StaleAfter;
        var rows = await Filtered(db, f, now)
            .Select(o => new
            {
                Order = o,
                Items = o.Items.Sum(i => i.Quantity),
                RefundAttention = o.Refunds.Any(r => r.Status == StoreRefundStatus.Failed
                                                  || (r.Status == StoreRefundStatus.Pending && r.StripeRefundId == null && r.DateCreated < stale)),
            })
            .ToListAsync(ct);
        return rows.OrderByDescending(r => r.Order.PlacedUtc)
            .Select(r => new StoreOrderListRecord(
                r.Order.Id, r.Order.OrderNumber, r.Order.PlacedUtc, r.Order.Status, StoreOrderViews.PaymentStatus(r.Order),
                r.Order.BuyerName, r.Order.BuyerEmail, r.Order.BuyerAppUserId is null, r.Order.Total, r.Order.RefundedAmount,
                r.Items, r.Order.ShipState, r.Order.CouponCode, r.Order.NeedsAttention, r.Order.AttentionReason, r.RefundAttention))
            .ToList();
    }

    /// <summary>Paid through Stripe for real — not a demo seed, not the test checkout's pretend payment.</summary>
    public static bool RealPayment(StoreOrder o)
        => o.StripePaymentIntentId is { Length: > 0 } pi && !pi.StartsWith("seed_", StringComparison.Ordinal);

    public static StoreOrderAbilities Abilities(StoreOrder o, decimal refundable)
    {
        // Packing and shipping are per package (store sellers P7); the order can "pack" or "ship" when any of its packages can.
        var waiting = o.Status is StoreOrderStatus.Paid or StoreOrderStatus.Packed;
        var parcels = ParcelsFor(o);
        return new StoreOrderAbilities(
            CanPack: parcels.Any(x => x.CanPack),
            CanShip: parcels.Any(x => x.CanShip),
            CanCorrectTracking: parcels.Any(x => x.CanCorrectTracking),
            CanDeliver: parcels.Any(x => x.CanDeliver),
            CanCancel: waiting && RealPayment(o),
            CanRefund: o.PaidUtc is not null && refundable > 0 && RealPayment(o),
            CanEditAddress: waiting,
            CanClearAttention: o.NeedsAttention,
            CanResendConfirmation: o.PaidUtc is not null,
            CanResendShipped: parcels.Any(x => x.CanResendShipped),
            CanRelease: o.Status == StoreOrderStatus.PendingPayment && o.ReservationReleasedUtc is null);
    }

    private static readonly StoreOrderStatus[] Fulfilling =
        [StoreOrderStatus.Paid, StoreOrderStatus.Packed, StoreOrderStatus.PartiallyShipped, StoreOrderStatus.Shipped];

    /// <summary>Each package of the order, with what may be done to it now. Callers load <c>Parcels</c> and <c>Items</c>.</summary>
    public static IReadOnlyList<StoreOrderParcelAdminRecord> ParcelsFor(StoreOrder o)
    {
        var sendable = Fulfilling.Contains(o.Status) && !o.NeedsAttention;
        return o.Parcels.OrderBy(x => x.Number).Select(x => new StoreOrderParcelAdminRecord(
            x.Id, x.Number, x.SellerAppUserId, StoreParcelNames.ShipsFrom(x.SellerAppUserId, x.SellerName), x.Status,
            x.ShippingAmount, x.ShippingTaxAmount, x.SellerShippingCredit, x.Carrier, x.TrackingNumber, x.TrackingUrl,
            x.PackedUtc, x.ShippedUtc, x.DeliveredUtc, o.Items.Where(i => i.ParcelId == x.Id).Select(i => i.Id).ToList(),
            CanPack: sendable && x.Status == StoreParcelStatus.Waiting,
            CanShip: sendable && x.Status is StoreParcelStatus.Waiting or StoreParcelStatus.Packed,
            CanCorrectTracking: x.Status == StoreParcelStatus.Shipped,
            CanDeliver: x.Status == StoreParcelStatus.Shipped,
            CanResendShipped: x.Carrier is not null)).ToList();
    }

    /// <summary>The payment in Stripe's dashboard, test or live by the key in use; null when there is none to open.</summary>
    public static string? DashboardUrl(StoreOrder o, string? secretKey)
        => RealPayment(o) && !o.StripePaymentIntentId!.StartsWith("pi_fake_", StringComparison.Ordinal)
            ? $"https://dashboard.stripe.com/{(secretKey?.StartsWith("sk_live_", StringComparison.Ordinal) == true ? "" : "test/")}payments/{o.StripePaymentIntentId}"
            : null;

    public static async Task<StoreOrderDetailAdminRecord?> DetailAsync(BenDataContext db, Guid id, string? secretKey, DateTime now, CancellationToken ct)
    {
        var o = await db.StoreOrders.AsNoTracking()
            .Include(x => x.Items).Include(x => x.Parcels).Include(x => x.Refunds).ThenInclude(r => r.Items).Include(x => x.Events)
            .AsSplitQuery().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (o is null) return null;

        var people = o.Events.Where(e => e.ActorAppUserId != null).Select(e => e.ActorAppUserId!.Value)
            .Concat(o.Refunds.Where(r => r.RequestedByAppUserId != null).Select(r => r.RequestedByAppUserId!.Value)).Distinct().ToList();
        var names = await db.AppUsers.AsNoTracking().Where(u => people.Contains(u.Id))
            .Select(u => new { u.Id, Name = u.DisplayName ?? u.Email }).ToDictionaryAsync(u => u.Id, u => u.Name, ct);

        var refundable = Math.Max(0, StoreRefundService.Remaining(o, o.Refunds));
        var items = o.Items.OrderBy(i => i.DateCreated).Select(i => new StoreOrderItemAdminRecord(
            i.Id, i.ProductId, i.VariantId, i.ProductName, i.VariantName, i.Sku, i.ImageUploadFileId, i.UnitPrice, i.Quantity,
            i.LineTotal, i.LineDiscount, i.TaxAmount, i.QuantityRefunded, i.QuantityRestocked,
            Math.Max(0, StoreRefundService.RefundableUnits(i, o.Refunds)))).ToList();
        var refunds = o.Refunds.OrderByDescending(r => r.DateCreated).Select(r => new StoreRefundRecord(
            r.Id, r.Amount, r.TaxReversed, r.Reason, r.Restock, r.Status, r.Attempt, r.StripeRefundId, r.StripeTaxReversalId,
            r.FailureReason, r.RequestedByAppUserId is { } who ? names.GetValueOrDefault(who) : null, r.DateCreated, r.CompletedUtc,
            StoreRefundService.IsRetryable(r, now),
            r.Status == StoreRefundStatus.Pending && r.StripeRefundId is not null,
            r.Status == StoreRefundStatus.Succeeded && r.StripeTaxReversalId is null && o.StripeTaxTransactionId is not null,
            r.Items.Select(i => new StoreRefundLine(i.OrderItemId, i.Quantity)).ToList())).ToList();
        var events = o.Events.OrderByDescending(e => e.OccurredUtc).Select(e => new StoreOrderEventRecord(
            e.Kind, e.FromStatus, e.ToStatus, e.Note, e.Amount, e.OccurredUtc,
            e.ActorAppUserId is { } actor ? names.GetValueOrDefault(actor) : null)).ToList();

        return new StoreOrderDetailAdminRecord(
            o.Id, o.OrderNumber, o.Status, StoreOrderViews.PaymentStatus(o), o.PlacedUtc, o.PaidUtc, o.PackedUtc, o.ShippedUtc,
            o.DeliveredUtc, o.CancelledUtc, o.CancellationReason, o.BuyerAppUserId, o.BuyerName, o.BuyerEmail,
            StoreOrderViews.Shipping(o), StoreOrderViews.Billing(o), o.BuyerNotes, items, StoreOrderViews.Totals(o),
            o.RefundedAmount, refundable, o.CouponCode, StoreOrderViews.SoleShipment(o).Carrier, StoreOrderViews.SoleShipment(o).Number,
            StoreOrderViews.SoleShipment(o).Url,
            o.NeedsAttention, o.AttentionReason, o.StripePaymentIntentId, o.StripeTaxTransactionId, DashboardUrl(o, secretKey),
            refunds, events, Abilities(o, refundable), o.ReservationExpiresUtc, ParcelsFor(o));
    }

    /// <summary>One row per order item — what a spreadsheet of sales wants.</summary>
    public static async Task<List<string>> ExportAsync(BenDataContext db, StoreOrderFilter f, DateTime now, CancellationToken ct)
    {
        var orders = await Filtered(db, f, now).Include(o => o.Items).Include(o => o.Parcels).AsSplitQuery().ToListAsync(ct);
        var lines = new List<string>
        {
            StoreCsv.Line("OrderNumber", "Placed", "Status", "BuyerEmail", "ShipState", "ShipZip", "Sku", "Product", "Quantity",
                "UnitPrice", "LineDiscount", "LineTax", "Shipping", "ShippingTax", "OrderTotal", "Refunded", "Coupon",
                "PaymentIntent", "TaxTransaction", "Package", "ShipsFrom", "PackageStatus", "Carrier", "Tracking"),
        };
        foreach (var o in orders.OrderBy(o => o.OrderNumber))
            foreach (var i in o.Items.OrderBy(i => i.DateCreated))
            {
                var parcel = o.Parcels.FirstOrDefault(x => x.Id == i.ParcelId);
                lines.Add(StoreCsv.Line(o.OrderNumber, o.PlacedUtc.ToString("yyyy-MM-dd HH:mm"), o.Status, o.BuyerEmail, o.ShipState, o.ShipZip,
                    i.Sku, i.ProductName + (string.IsNullOrWhiteSpace(i.VariantName) ? "" : $" — {i.VariantName}"), i.Quantity,
                    i.UnitPrice, i.LineDiscount, i.TaxAmount, o.ShippingAmount, o.ShippingTaxAmount, o.Total, o.RefundedAmount,
                    o.CouponCode, o.StripePaymentIntentId, o.StripeTaxTransactionId, parcel?.Number,
                    parcel is null ? null : StoreParcelNames.ShipsFrom(parcel.SellerAppUserId, parcel.SellerName), parcel?.Status,
                    parcel?.Carrier, parcel?.TrackingNumber));
            }
        return lines;
    }

    public static async Task<List<string>> RefundsExportAsync(BenDataContext db, DateTime? from, DateTime? to, CancellationToken ct)
    {
        var q = db.StoreRefunds.AsNoTracking().Include(r => r.Order).AsQueryable();
        if (from is { } f) q = q.Where(r => r.DateCreated >= f);
        if (to is { } t) q = q.Where(r => r.DateCreated < t.Date.AddDays(1));
        var rows = await q.ToListAsync(ct);
        var lines = new List<string>
        {
            StoreCsv.Line("OrderNumber", "Requested", "Completed", "Status", "Amount", "TaxReversed", "Reason", "Restocked",
                "StripeRefund", "TaxReversal", "FailureReason"),
        };
        lines.AddRange(rows.OrderBy(r => r.DateCreated).Select(r => StoreCsv.Line(
            r.Order.OrderNumber, r.DateCreated.ToString("yyyy-MM-dd HH:mm"), r.CompletedUtc?.ToString("yyyy-MM-dd HH:mm"), r.Status,
            r.Amount, r.TaxReversed, r.Reason, r.Restock ? "yes" : "no", r.StripeRefundId, r.StripeTaxReversalId, r.FailureReason)));
        return lines;
    }
}
