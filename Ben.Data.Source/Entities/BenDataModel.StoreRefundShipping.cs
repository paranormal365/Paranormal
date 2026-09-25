namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// A package's shipping given back as part of a refund (store sellers, backlog 251, P8): its
    /// shipping and the tax on it, or what was left of them.
    /// </summary>
    /// <remarks>
    /// Counted onto the package's <c>ShippingRefunded</c> only when Stripe says the money has gone,
    /// the way a refund's items restock only then. The last refund of a package's shipping takes
    /// whatever is left, so the pennies add up to what was paid.
    /// </remarks>
    public class StoreRefundShipping
    {
        public Guid Id { get; set; }
        public Guid RefundId { get; set; }
        public Guid ParcelId { get; set; }
        public decimal Amount { get; set; }

        public virtual StoreRefund Refund { get; set; } = null!;
        public virtual StoreOrderParcel Parcel { get; set; } = null!;
    }
}
