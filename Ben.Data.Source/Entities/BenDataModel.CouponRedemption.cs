using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// One organization's redemption of one coupon — the record of who got what discount and when.
    /// </summary>
    /// <remarks>
    /// <para>Separate from the flag on the subscription because this is the <b>financial record</b>
    /// and the subscription only carries the currently-applied discount. When the coupon runs out
    /// the subscription stops pointing at it; this row stays, because "why was this group charged
    /// less in March?" must remain answerable.</para>
    ///
    /// <para>The unique index on organization and coupon is also what makes the redemption limit
    /// safe: <c>Coupon.RedemptionCount</c> is a cache, and two simultaneous redemptions of the last
    /// use race on it. The database decides.</para>
    /// </remarks>
    public class CouponRedemption : IAuditableEntity
    {
        public Guid Id { get; set; }

        public Guid CouponId { get; set; }
        public Guid OrganizationId { get; set; }

        /// <summary>Periods still to be discounted, counted down as each one is billed.</summary>
        /// <remarks>Null for a <c>Forever</c> coupon, which never runs out.</remarks>
        public int? PeriodsRemaining { get; set; }

        public DateTime RedeemedAtUtc { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual Coupon Coupon { get; set; } = null!;
        public virtual Organization Organization { get; set; } = null!;
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
    }
}
