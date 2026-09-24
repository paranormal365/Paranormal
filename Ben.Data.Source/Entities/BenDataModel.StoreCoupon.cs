using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// A discount code typed at the cart or checkout (storefront).
    /// </summary>
    /// <remarks>
    /// <para>The store's own table: the subscription <see cref="Coupon"/> is shaped around billing
    /// periods and groups, and a store code is neither.</para>
    ///
    /// <para><b>RedemptionCount is a cache, not the authority.</b> A redemption is reserved when the
    /// order is PLACED, inside the stock transaction, by a conditional UPDATE that only succeeds while
    /// the count is under <see cref="MaxRedemptions"/> — so N parallel checkouts of a single-use code
    /// cannot all succeed — and released with the stock when a checkout is abandoned.</para>
    ///
    /// <para>The discount is clamped to the products subtotal and never touches shipping or tax.</para>
    /// </remarks>
    public class StoreCoupon : IAuditableEntity
    {
        public Guid Id { get; set; }

        /// <summary>What people type; stored upper-case, unique.</summary>
        public string Code { get; set; } = string.Empty;

        /// <summary>A name for the admin; never shown to buyers.</summary>
        public string Name { get; set; } = string.Empty;

        public StoreCouponKind Kind { get; set; }

        /// <summary>1–100, for <see cref="StoreCouponKind.Percent"/>.</summary>
        public int? PercentOff { get; set; }

        /// <summary>Dollars off, for <see cref="StoreCouponKind.Fixed"/>.</summary>
        public decimal? AmountOff { get; set; }

        public decimal? MinimumOrderAmount { get; set; }
        public DateTime? StartsUtc { get; set; }
        public DateTime? EndsUtc { get; set; }

        /// <summary>Total uses across everybody, or null for unlimited.</summary>
        public int? MaxRedemptions { get; set; }

        public int RedemptionCount { get; set; }

        /// <summary>Uses per buyer (by email or account); null for unlimited.</summary>
        public int? MaxRedemptionsPerBuyer { get; set; } = 1;

        public bool IsActive { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
    }
}
