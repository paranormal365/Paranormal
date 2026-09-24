namespace Ben.Data.Common.Enums;

/// <summary>One line of a store order's timeline (storefront).</summary>
/// <remarks>Explicit values: events are an append-only record and a renumbering would rewrite history.</remarks>
public enum StoreOrderEventKind
{
    Placed = 0,
    PaymentSucceeded = 1,
    PaymentFailed = 2,
    ReservationExpired = 3,
    Packed = 4,
    Shipped = 5,
    TrackingChanged = 6,
    Delivered = 7,
    Cancelled = 8,
    RefundRequested = 9,
    RefundSucceeded = 10,
    RefundFailed = 11,
    /// <summary>A refund made in the Stripe dashboard rather than on the admin order page.</summary>
    RefundFromStripe = 12,

    Restocked = 13,
    LetterQueued = 14,
    NoteAdded = 15,
    /// <summary>The buyer's account was deleted and the order's personal details were scrubbed.</summary>
    Anonymised = 16,

    TaxTransactionFailed = 17,
    PaymentProcessing = 18,
    AddressChanged = 19,
    AttentionRaised = 20,
    AttentionCleared = 21,
    /// <summary>Stripe accepted a refund and is still processing it.</summary>
    RefundPending = 22,
}
