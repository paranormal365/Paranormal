using Ben.Data.Common.Enums;

namespace Ben.Service.Models.Store;

// The admin's order desk (storefront S5.1): the list, one order with everything that happened to it,
// and what the admin can ask of it. Money is the server's; the page adds nothing up.

/// <param name="HasRefundAttention">A refund on it failed, or is stuck waiting to reach Stripe.</param>
public sealed record StoreOrderListRecord(
    Guid Id, int OrderNumber, DateTime PlacedUtc, StoreOrderStatus Status, string PaymentStatus, string BuyerName,
    string BuyerEmail, bool IsGuest, decimal Total, decimal RefundedAmount, int ItemCount, string ShipState,
    string? CouponCode, bool NeedsAttention, string? AttentionReason, bool HasRefundAttention);

/// <param name="Refundable">Units of this line that can still be refunded (not refunded, not in a pending refund).</param>
public sealed record StoreOrderItemAdminRecord(
    Guid Id, Guid ProductId, Guid VariantId, string ProductName, string? VariantName, string Sku, Guid? ImageUploadFileId,
    decimal UnitPrice, int Quantity, decimal LineTotal, decimal LineDiscount, decimal TaxAmount,
    int QuantityRefunded, int QuantityRestocked, int Refundable);

public sealed record StoreOrderEventRecord(
    StoreOrderEventKind Kind, StoreOrderStatus? FromStatus, StoreOrderStatus? ToStatus, string? Note, decimal? Amount,
    DateTime OccurredUtc, string? ActorName);

/// <summary>A line of a refund by item: which order line, and how many of it.</summary>
public sealed record StoreRefundLine(Guid OrderItemId, int Quantity);

/// <param name="IsRetryable">Failed, or Pending and never reached Stripe for over two minutes.</param>
/// <param name="IsAwaitingStripe">Accepted by Stripe and still going through — it finishes when Stripe says so.</param>
/// <param name="TaxReversalMissing">Refunded, the order's tax is filed, and the matching reversal is not.</param>
public sealed record StoreRefundRecord(
    Guid Id, decimal Amount, decimal TaxReversed, string Reason, bool Restock, StoreRefundStatus Status, int Attempt,
    string? StripeRefundId, string? StripeTaxReversalId, string? FailureReason, string? RequestedByName,
    DateTime DateCreated, DateTime? CompletedUtc, bool IsRetryable, bool IsAwaitingStripe, bool TaxReversalMissing,
    IReadOnlyList<StoreRefundLine> Items);

/// <summary>A refund to make: an amount, or items (which may go back on the shelf).</summary>
/// <param name="Amount">For a refund by amount; null when refunding items.</param>
/// <param name="ShippingForParcels">Packages whose shipping (and its tax) to give back too — what is left of it (store sellers P8).</param>
public sealed record StoreRefundRequest(decimal? Amount, IReadOnlyList<StoreRefundLine>? Items, string Reason, bool Restock,
    IReadOnlyList<Guid>? ShippingForParcels = null);

/// <summary>How an order went out (Ben, 09/24): a carrier from the list and its tracking number — or none.</summary>
/// <param name="TrackingNumber">Required unless <paramref name="NoTracking"/>; the link is built from it and the carrier.</param>
/// <param name="TrackingUrl">Only for "Other" (https) — the known carriers' links are built from the number.</param>
/// <param name="NoTracking">"No tracking provided": shipped with this carrier, without a tracking number.</param>
public sealed record StoreShipmentInfo(string Carrier, string? TrackingNumber, string? TrackingUrl, string? Note, bool NoTracking = false);

public sealed record CancelStoreOrderRequest(string Reason, bool Restock);

public sealed record AddStoreOrderNoteRequest(string Note);

/// <param name="BuyerEmail">A new email for the buyer; null or the same keeps it.</param>
public sealed record ChangeStoreOrderAddressRequest(StoreAddressInput Shipping, string? BuyerEmail);

/// <param name="Kind"><c>confirmation</c> or <c>shipped</c>.</param>
public sealed record ResendStoreOrderLetterRequest(string Kind);

/// <summary>What the order desk may ask of this order now — the page shows exactly these buttons.</summary>
public sealed record StoreOrderAbilities(
    bool CanPack, bool CanShip, bool CanCorrectTracking, bool CanDeliver, bool CanCancel, bool CanRefund,
    bool CanEditAddress, bool CanClearAttention, bool CanResendConfirmation, bool CanResendShipped, bool CanRelease);

/// <param name="Refundable">What can still be refunded: the total less refunds made and refunds going through.</param>
/// <param name="StripeDashboardUrl">The payment in Stripe's dashboard (live or test by the key); null when not paid through Stripe.</param>
public sealed record StoreOrderDetailAdminRecord(
    Guid Id, int OrderNumber, StoreOrderStatus Status, string PaymentStatus,
    DateTime PlacedUtc, DateTime? PaidUtc, DateTime? PackedUtc, DateTime? ShippedUtc, DateTime? DeliveredUtc,
    DateTime? CancelledUtc, string? CancellationReason,
    Guid? BuyerAppUserId, string BuyerName, string BuyerEmail, StoreOrderAddressView Shipping, StoreOrderAddressView? Billing,
    string? BuyerNotes, IReadOnlyList<StoreOrderItemAdminRecord> Items, StoreCheckoutTotals Totals,
    decimal RefundedAmount, decimal Refundable, string? CouponCode,
    string? Carrier, string? TrackingNumber, string? TrackingUrl,
    bool NeedsAttention, string? AttentionReason,
    string? StripePaymentIntentId, string? StripeTaxTransactionId, string? StripeDashboardUrl,
    IReadOnlyList<StoreRefundRecord> Refunds, IReadOnlyList<StoreOrderEventRecord> Events,
    StoreOrderAbilities Can, DateTime? ReservationExpiresUtc, IReadOnlyList<StoreOrderParcelAdminRecord>? Parcels = null);

/// <summary>
/// One package of an order on the order desk (store sellers, backlog 251, P7): whose it is, what it
/// holds and where it stands, and what may be done to it now.
/// </summary>
/// <param name="SellerShippingCredit">What its seller is credited for the label — the flat rate, even when it shipped free.</param>
public sealed record StoreOrderParcelAdminRecord(
    Guid Id, int Number, Guid? SellerAppUserId, string ShipsFrom, StoreParcelStatus Status, decimal Shipping, decimal ShippingTax,
    decimal SellerShippingCredit, string? Carrier, string? TrackingNumber, string? TrackingUrl, DateTime? PackedUtc,
    DateTime? ShippedUtc, DateTime? DeliveredUtc, IReadOnlyList<Guid> ItemIds,
    bool CanPack, bool CanShip, bool CanCorrectTracking, bool CanDeliver, bool CanResendShipped)
{
    public string Title => $"Package {Number} · Ships from {ShipsFrom}";
}

/// <summary>The order list's filters as the page asks them (and the address it keeps them in).</summary>
public sealed record StoreOrderListQuery(
    StoreOrderStatus? Status = null, string? Q = null, string? Coupon = null, DateTime? From = null, DateTime? To = null,
    bool NeedsAction = false, bool RefundAttention = false, bool IncludeUnpaid = false, bool Attention = false)
{
    /// <summary>The query string both the list and the CSV take.</summary>
    public string ToQueryString() => Parameters() is { Length: > 0 } p ? "?" + p : "";

    /// <summary>The same, without its "?" — for an address that always writes one.</summary>
    public string Parameters()
    {
        var parts = new List<string>();
        if (Status is { } s) parts.Add($"status={s}");
        if (!string.IsNullOrWhiteSpace(Q)) parts.Add($"q={Uri.EscapeDataString(Q.Trim())}");
        if (!string.IsNullOrWhiteSpace(Coupon)) parts.Add($"coupon={Uri.EscapeDataString(Coupon.Trim())}");
        if (From is { } f) parts.Add($"from={f:yyyy-MM-dd}");
        if (To is { } t) parts.Add($"to={t:yyyy-MM-dd}");
        if (NeedsAction) parts.Add("needsAction=true");
        if (RefundAttention) parts.Add("refunds=attention");
        if (IncludeUnpaid) parts.Add("unpaid=true");
        if (Attention) parts.Add("attention=true");
        return string.Join("&", parts);
    }
}

/// <summary>What asking for a refund came to.</summary>
/// <param name="Message">For a refund Stripe accepted but is still processing — said to the admin.</param>
public sealed record StoreRefundResult(StoreRefundRecord Refund, string? Message);

/// <summary>The order desk's sentences, shared by the services that say them and the tests that look for them.</summary>
public static class StoreOrderDeskSentences
{
    /// <summary>A package that has shipped can't be cancelled — refund its items instead (store sellers P8).</summary>
    public const string PackageAlreadyGone = "That package has already gone — refund its items instead.";

    /// <summary>Cancelling the only package left is cancelling the order.</summary>
    public const string LastPackageCancelsOrder = "That's the only package left — cancel the whole order instead.";

    /// <summary>A package of an order that isn't paid, or is already finished (store sellers P7).</summary>
    public const string NotReadyToFulfil = "This order isn't waiting to be sent — it isn't paid, or it's cancelled or refunded.";

    public static string OnlyPaidCanBePacked(string status) => $"Only a paid order can be packed; this one is {status}.";
    public static string NeedsAttentionFirst(string reason) => $"This order needs attention first — {reason}";
    public static string OnlyPaidOrPackedCanShip(string status) => $"Only a paid or packed order can be shipped; this one is {status}.";
    public const string PickACarrier = "Pick a carrier from the list.";
    public const string NeedsTracking = "Enter the tracking number — or choose \"No tracking provided\".";
    public const string NoTrackingLabel = "No tracking provided";
    public const string TrackingLinkHttps = "A tracking link must start with https://.";
    public const string TrackingOnlyWhenShipped = "Tracking can be corrected only on a shipped order.";
    public const string OnlyShippedCanBeDelivered = "Only a shipped order can be marked delivered.";
    public const string AlreadyOnItsWay = "It's already on its way — refund it instead.";
    public static string CannotCancel(string status) => $"A {status} order can't be cancelled.";
    public const string AddressAfterShipping = "The address can't change once it has shipped.";
    public const string NoteEmpty = "Write something in the note.";
    public const string NothingToClear = "This order isn't marked as needing attention.";
    public const string ShippedLetterNeedsCarrier = "There's no shipment to tell the buyer about yet.";
    public const string ConfirmationNeedsPayment = "There's no receipt to send — this order isn't paid.";
    public const string UnknownLetter = "Choose the receipt or the shipped letter.";
    public const string OnlyOpenCheckoutsRelease = "Only a checkout that is still waiting for payment can be released.";
    public const string StillProcessing = "Its payment is still going through at Stripe — it can't be released now.";

    public const string OnlyPaidRefunded = "Only a paid order can be refunded.";
    public const string RefundNeedsReason = "A refund needs a reason — a row that does not say why is one nobody can answer for later.";
    public static string MoreThanRemaining(decimal remaining) => $"That is more than the {StoreMoney.Format(remaining)} still refundable on this order.";
    public static string OnlyNLeft(int n, string product) => $"Only {n} of {product} can still be refunded.";
    public const string AmountCannotRestock = "A refund by amount can't restock — choose the items instead.";
    public const string AmountOrItems = "Give an amount or choose items — one or the other.";
    public const string AmountPositive = "A refund must be for more than $0.00.";
    public const string NotPaidThroughStripe = "This order was not paid through Stripe, so it cannot be refunded here.";
    public static string StripeRefused(string reason) => $"Stripe refused the refund: {reason}";
    public const string RefundPending = "Stripe has accepted the refund and is still processing it — the order updates when it goes through.";
    public const string NotRetryable = "Only a failed refund, or one that never reached Stripe, can be tried again.";
    public const string NoTaxToReverse = "There's no sales tax filing to reverse for this refund.";
}
