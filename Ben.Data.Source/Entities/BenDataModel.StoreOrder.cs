using Ben.Data.Common.Enums;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// A store order, from checkout to delivery or refund (storefront).
    /// </summary>
    /// <remarks>
    /// <para><b>The row IS the money record.</b> Totals are frozen at checkout; refunds and events
    /// are append-only children. The order number is the invoice number. Store money is not on the
    /// group billing ledger.</para>
    ///
    /// <para><b>Created before money moves.</b> Checkout writes the order as
    /// <see cref="StoreOrderStatus.PendingPayment"/> with stock and any coupon reserved, then creates
    /// the PaymentIntent; the webhook marks it Paid. A checkout that is never paid is released by
    /// the expiry job.</para>
    ///
    /// <para><b>Guests and members alike.</b> <see cref="BuyerAppUserId"/> is null for a guest, who
    /// reaches the order through <see cref="AccessToken"/> in the emailed link. The address is a
    /// snapshot; deleting the buyer's account scrubs it once the order is finished.</para>
    ///
    /// <para><b>Tax.</b> <see cref="TaxAmount"/> is Stripe's exclusive tax INCLUDING tax on
    /// shipping; <see cref="ShippingTaxAmount"/> is that part, for the invoice line.</para>
    /// </remarks>
    public class StoreOrder
    {
        public Guid Id { get; set; }
        public int OrderNumber { get; set; }
        public StoreOrderStatus Status { get; set; }
        public Guid? StoreCartId { get; set; }

        public Guid? BuyerAppUserId { get; set; }
        public string BuyerEmail { get; set; } = string.Empty;
        public string BuyerEmailNormalized { get; set; } = string.Empty;
        public string BuyerName { get; set; } = string.Empty;

        public string ShipName { get; set; } = string.Empty;
        public string ShipPhone { get; set; } = string.Empty;
        public string ShipStreet1 { get; set; } = string.Empty;
        public string? ShipStreet2 { get; set; }
        public string ShipCity { get; set; } = string.Empty;
        public string ShipState { get; set; } = string.Empty;
        public string ShipZip { get; set; } = string.Empty;
        public string ShipCountry { get; set; } = "US";

        public bool BillingSameAsShipping { get; set; } = true;
        public string? BillName { get; set; }
        public string? BillCompany { get; set; }
        public string? BillStreet1 { get; set; }
        public string? BillStreet2 { get; set; }
        public string? BillCity { get; set; }
        public string? BillState { get; set; }
        public string? BillZip { get; set; }
        public string? BillCountry { get; set; }

        public decimal Subtotal { get; set; }
        public decimal DiscountAmount { get; set; }
        public decimal ShippingAmount { get; set; }
        public decimal TaxAmount { get; set; }
        public decimal ShippingTaxAmount { get; set; }

        /// <summary>Subtotal − Discount + Shipping + Tax. Never negative (a CHECK).</summary>
        public decimal Total { get; set; }

        public decimal RefundedAmount { get; set; }
        public string Currency { get; set; } = "usd";

        public Guid? CouponId { get; set; }
        public string? CouponCode { get; set; }

        /// <summary>SHA-256 over the lines, coupon, address and email — a repeat checkout with the same fingerprint reuses this order.</summary>
        public string CartFingerprint { get; set; } = string.Empty;

        /// <summary>The visitor's address when the checkout was placed; the open-checkout cap counts it. Scrubbed with the address.</summary>
        public string? PlacedFromIp { get; set; }

        public string? StripePaymentIntentId { get; set; }
        public string? StripeChargeId { get; set; }
        public string? StripeTaxCalculationId { get; set; }
        public string? StripeTaxTransactionId { get; set; }

        /// <summary>The guest's key to this order (64 hex); re-minted if the buyer email is corrected.</summary>
        public string AccessToken { get; set; } = string.Empty;

        public DateTime? ReservationExpiresUtc { get; set; }
        public DateTime? ReservationReleasedUtc { get; set; }

        public DateTime PlacedUtc { get; set; }
        public DateTime? PaidUtc { get; set; }
        public DateTime? PackedUtc { get; set; }
        public DateTime? ShippedUtc { get; set; }
        public DateTime? DeliveredUtc { get; set; }
        public DateTime? CancelledUtc { get; set; }
        public string? CancellationReason { get; set; }

        public string? Carrier { get; set; }
        public string? TrackingNumber { get; set; }
        public string? TrackingUrl { get; set; }

        public string? BuyerNotes { get; set; }
        public string? AdminNotes { get; set; }

        /// <summary>Set when a payment or tax anomaly needs a person; packing is refused while it is.</summary>
        public bool NeedsAttention { get; set; }
        public string? AttentionReason { get; set; }

        /// <summary>How many times the tax-transaction commit has been tried.</summary>
        public int TaxCommitAttempts { get; set; }

        /// <summary>Set when the buyer's account was deleted while the order was still in flight.</summary>
        public DateTime? PendingAnonymisationSinceUtc { get; set; }

        /// <summary>Its packages, one per seller (store sellers P5).</summary>
        public virtual ICollection<StoreOrderParcel> Parcels { get; set; } = [];

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }

        public virtual StoreCart? Cart { get; set; }
        public virtual AppUser? BuyerAppUser { get; set; }
        public virtual StoreCoupon? Coupon { get; set; }
        public virtual ICollection<StoreOrderItem> Items { get; set; } = [];
        public virtual ICollection<StoreOrderEvent> Events { get; set; } = [];
        public virtual ICollection<StoreRefund> Refunds { get; set; } = [];
    }
}
