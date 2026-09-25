using Ben.Data.Common.Enums;
using Ben.Data.Source.Entities;
using Ben.Service.Models.Store;

namespace Ben.Data.WebApi.Services.Store;

/// <summary>An order as its buyer sees it — the order page, My Orders, the invoice (storefront S4.9).</summary>
/// <remarks>Nothing here carries an admin note, a Stripe id, the attention reason or the access token.</remarks>
public static class StoreOrderViews
{
    public static string PaymentStatus(StoreOrder o) => o switch
    {
        { PaidUtc: null, Status: StoreOrderStatus.Cancelled } => "Not paid — cancelled",
        { PaidUtc: null } => "Awaiting payment",
        { Status: StoreOrderStatus.Refunded } => "Refunded",
        { RefundedAmount: > 0 } => "Partly refunded",
        _ => "Paid",
    };

    public static bool IsFinal(StoreOrder o)
        => o.Status is StoreOrderStatus.Cancelled or StoreOrderStatus.Refunded || o.PaidUtc is not null;

    public static StoreCheckoutTotals Totals(StoreOrder o)
        => new(o.Subtotal, o.DiscountAmount, o.ShippingAmount, o.TaxAmount, o.Total, o.ShippingTaxAmount,
            o.Parcels.Count == 0 ? null : Parcels(o).Select(x => x.AsRow()).ToList());

    /// <summary>
    /// The order's carrier and tracking when it went as one package — what the single-shipment
    /// fields still say; for an order in several packages each package says its own (store sellers P7).
    /// </summary>
    public static (string? Carrier, string? Number, string? Url) SoleShipment(StoreOrder o)
        => o.Parcels.Where(x => x.Status != StoreParcelStatus.Cancelled).ToList() is [var only]
            ? (only.Carrier, only.TrackingNumber, only.TrackingUrl) : (null, null, null);

    /// <summary>The order's packages as the buyer sees them (store sellers P6). Callers load <c>Parcels</c> with the order.</summary>
    public static IReadOnlyList<StoreOrderParcelView> Parcels(StoreOrder o) => o.Parcels.OrderBy(x => x.Number).Select(x => new StoreOrderParcelView(
        x.Number, StoreParcelNames.ShipsFrom(x.SellerAppUserId, x.SellerName), x.Status, x.ShippingAmount, x.Carrier, x.TrackingNumber,
        x.TrackingUrl, x.ShippedUtc, x.DeliveredUtc, o.Items.Where(i => i.ParcelId == x.Id).Select(i => i.Id).ToList())).ToList();

    public static StoreOrderAddressView Shipping(StoreOrder o)
        => new(o.ShipName, o.ShipPhone, null, o.ShipStreet1, o.ShipStreet2, o.ShipCity, o.ShipState, o.ShipZip, o.ShipCountry);

    public static StoreOrderAddressView? Billing(StoreOrder o)
        => o.BillingSameAsShipping || o.BillStreet1 is null ? null
         : new(o.BillName ?? o.ShipName, null, o.BillCompany, o.BillStreet1, o.BillStreet2, o.BillCity ?? "", o.BillState ?? "", o.BillZip ?? "", o.BillCountry ?? "US");

    public static StoreOrderView View(StoreOrder o, IReadOnlyDictionary<Guid, string> productSlugs, int returnsWindowDays, string? supportEmail)
        => new(o.Id, o.OrderNumber, o.Status, PaymentStatus(o), o.PlacedUtc, o.PaidUtc, o.BuyerEmail, Shipping(o), Billing(o),
            o.Items.OrderBy(i => i.DateCreated).ThenBy(i => i.Sku).Select(i => new StoreOrderItemView(
                i.Id, i.ProductId, productSlugs.GetValueOrDefault(i.ProductId), i.ProductName, i.VariantName, i.Sku, i.ImageUploadFileId,
                i.UnitPrice, i.CompareAtPrice, i.Quantity, i.LineDiscount, i.LineTotal, i.TaxAmount, i.QuantityRefunded)).ToList(),
            Totals(o), o.RefundedAmount, o.CouponCode, SoleShipment(o).Carrier, SoleShipment(o).Number, SoleShipment(o).Url, o.ShippedUtc, o.DeliveredUtc,
            o.Refunds.OrderBy(r => r.DateCreated).Select(r => new StoreRefundView(r.DateCreated, r.Amount, r.Reason, r.Status)).ToList(),
            o.Events.Where(e => BuyerSees(e.Kind)).OrderBy(e => e.OccurredUtc)
                .Select(e => new StoreOrderEventView(e.Kind, BuyerNote(e), e.Amount, e.OccurredUtc)).ToList(),
            returnsWindowDays, supportEmail, Parcels(o));

    /// <summary>The events a buyer is told about; attention, letters and notes are the store's own business.</summary>
    private static bool BuyerSees(StoreOrderEventKind k) => k is StoreOrderEventKind.Placed or StoreOrderEventKind.PaymentSucceeded
        or StoreOrderEventKind.Packed or StoreOrderEventKind.Shipped or StoreOrderEventKind.Delivered or StoreOrderEventKind.Cancelled
        or StoreOrderEventKind.RefundSucceeded or StoreOrderEventKind.RefundFromStripe or StoreOrderEventKind.ReservationExpired;

    private static string? BuyerNote(StoreOrderEvent e) => e.Kind switch
    {
        StoreOrderEventKind.Shipped or StoreOrderEventKind.Cancelled or StoreOrderEventKind.ReservationExpired => e.Note,
        _ => null,
    };

    public static StoreOrderSummaryView Summary(StoreOrder o)
        => new(o.Id, o.OrderNumber, o.PlacedUtc, o.Total, o.Status, PaymentStatus(o), o.Items.Sum(i => i.Quantity));

    public static StoreInvoiceRecord Invoice(StoreOrder o, StoreSettingsSnapshot s, string siteName, string? supportEmail)
    {
        var from = new StoreInvoiceParty(siteName, null, s.ShipFrom.Street, null, s.ShipFrom.City, s.ShipFrom.State, s.ShipFrom.Zip, supportEmail);
        var shipTo = new StoreInvoiceParty(o.ShipName, null, o.ShipStreet1, o.ShipStreet2, o.ShipCity, o.ShipState, o.ShipZip, null);
        var billTo = Billing(o) is { } b
            ? new StoreInvoiceParty(b.Name, b.Company, b.Street1, b.Street2, b.City, b.State, b.Zip, o.BuyerEmail)
            : shipTo with { Email = o.BuyerEmail };
        var lines = o.Items.OrderBy(i => i.DateCreated).ThenBy(i => i.Sku).Select(i => new StoreInvoiceLine(
            i.ProductName + (string.IsNullOrWhiteSpace(i.VariantName) ? "" : $" — {i.VariantName}"), i.Sku, i.Quantity, i.UnitPrice,
            i.LineDiscount, i.TaxAmount, i.LineTotal - i.LineDiscount)).ToList();
        return new StoreInvoiceRecord(o.OrderNumber, o.PlacedUtc, o.PaidUtc, from, billTo, shipTo, lines, o.Subtotal, o.DiscountAmount,
            o.CouponCode, o.ShippingAmount, o.TaxAmount, o.Total, o.RefundedAmount, o.Total - o.RefundedAmount,
            SoleShipment(o).Carrier, SoleShipment(o).Number, s.ReturnsWindowDays, supportEmail, Parcels(o));
    }
}
