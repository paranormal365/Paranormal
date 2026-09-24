using Ben.Data.Common.Enums;

namespace Ben.Service.Models.Store;

// An order as its buyer sees it (storefront S4.1): My Orders, the order page, the invoice.
// Nothing here carries an admin note, a Stripe id or anything about another buyer.

public sealed record StoreOrderItemView(
    Guid Id, Guid? ProductId, string? ProductSlug, string ProductName, string? VariantName, string Sku,
    Guid? ImageUploadFileId, decimal UnitPrice, decimal? CompareAtPrice, int Quantity, decimal LineDiscount,
    decimal LineTotal, decimal TaxAmount, int QuantityRefunded);

public sealed record StoreOrderAddressView(
    string Name, string? Phone, string? Company, string Street1, string? Street2, string City, string State,
    string Zip, string Country);

public sealed record StoreOrderEventView(StoreOrderEventKind Kind, string? Note, decimal? Amount, DateTime OccurredUtc);

public sealed record StoreRefundView(DateTime DateCreated, decimal Amount, string Reason, StoreRefundStatus Status);

/// <param name="PaymentStatus">"Paid", "Awaiting payment", "Refunded", "Partly refunded" — in words.</param>
/// <param name="Billing">Null when billing was the same as shipping.</param>
public sealed record StoreOrderView(
    Guid Id, int OrderNumber, StoreOrderStatus Status, string PaymentStatus, DateTime PlacedUtc, DateTime? PaidUtc,
    string BuyerEmail, StoreOrderAddressView Shipping, StoreOrderAddressView? Billing,
    IReadOnlyList<StoreOrderItemView> Items, StoreCheckoutTotals Totals, decimal RefundedAmount, string? CouponCode,
    string? Carrier, string? TrackingNumber, string? TrackingUrl, DateTime? ShippedUtc, DateTime? DeliveredUtc,
    IReadOnlyList<StoreRefundView> Refunds, IReadOnlyList<StoreOrderEventView> Events, int ReturnsWindowDays,
    string? SupportEmail);

public sealed record StoreOrderSummaryView(
    Guid Id, int OrderNumber, DateTime PlacedUtc, decimal Total, StoreOrderStatus Status, string PaymentStatus,
    int ItemCount);

/// <summary>Finding an order again from its number and email: the answer is always the same, and a match gets a letter.</summary>
public sealed record GuestOrderLookupRequest(string OrderNumber, string Email);

public sealed record StoreInvoiceParty(
    string Name, string? Company, string Street1, string? Street2, string City, string State, string Zip, string? Email);

public sealed record StoreInvoiceLine(
    string Description, string Sku, int Quantity, decimal UnitPrice, decimal Discount, decimal Tax, decimal Subtotal);

public sealed record StoreInvoiceRecord(
    int OrderNumber, DateTime PlacedUtc, DateTime? PaidUtc, StoreInvoiceParty BillFrom, StoreInvoiceParty BillTo,
    StoreInvoiceParty ShipTo, IReadOnlyList<StoreInvoiceLine> Lines, decimal Subtotal, decimal Discount,
    string? CouponCode, decimal Shipping, decimal Tax, decimal Total, decimal Refunded, decimal Balance,
    string? Carrier, string? TrackingNumber, int ReturnsWindowDays, string? SupportEmail);
