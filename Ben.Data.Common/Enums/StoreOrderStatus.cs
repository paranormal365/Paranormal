namespace Ben.Data.Common.Enums;

/// <summary>Where a store order is in its life (storefront).</summary>
/// <remarks>
/// Stored as int with explicit values: a reorder of this list must never re-read every order in the
/// database as a different status. Payment status (unpaid / paid / partially refunded / refunded) is
/// derived from the money columns, never stored beside this.
/// </remarks>
public enum StoreOrderStatus
{
    /// <summary>Placed at checkout: stock and any coupon are held while the buyer pays.</summary>
    PendingPayment = 0,

    /// <summary>Stripe confirmed the payment; waiting to be packed.</summary>
    Paid = 1,

    Packed = 2,
    /// <summary>Handed to a carrier; the buyer has the tracking link.</summary>
    Shipped = 3,

    Delivered = 4,
    /// <summary>Never paid (checkout abandoned or expired), or cancelled with a full refund before it shipped.</summary>
    Cancelled = 5,

    /// <summary>Paid, then refunded to a zero balance.</summary>
    Refunded = 6,
}
