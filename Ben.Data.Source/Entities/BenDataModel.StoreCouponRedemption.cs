
namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// A discount code held by, then used on, an order (storefront).
    /// </summary>
    /// <remarks>
    /// Written at PLACEMENT inside the stock transaction and deleted if the checkout is abandoned, so
    /// the per-buyer limit counts checkouts in progress too. The key to the coupon is Restrict: a
    /// redemption is a financial record, like the subscription coupon's.
    /// </remarks>
    public class StoreCouponRedemption
    {
        public Guid Id { get; set; }
        public Guid CouponId { get; set; }
        public Guid OrderId { get; set; }
        public string BuyerEmailNormalized { get; set; } = string.Empty;
        public Guid? BuyerAppUserId { get; set; }
        public decimal DiscountAmount { get; set; }
        public DateTime RedeemedUtc { get; set; }

        public virtual StoreCoupon Coupon { get; set; } = null!;
        public virtual StoreOrder Order { get; set; } = null!;
        public virtual AppUser? BuyerAppUser { get; set; }
    }
}
