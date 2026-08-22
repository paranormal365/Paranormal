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
    /// <para>The unique index on organization and <b>coupon</b> — not code — is also what makes the
    /// redemption limit safe: <c>Coupon.RedemptionCount</c> is a cache, and two simultaneous
    /// redemptions of the last use race on it. The database decides.</para>
    ///
    /// <para>Indexing on the coupon rather than the code is deliberate. A group handed two codes
    /// from the same batch has been handed the same offer twice, and letting them stack it is a
    /// mistake the batch's own single-use limit cannot catch.</para>
    /// </remarks>
    public class CouponRedemption : IAuditableEntity
    {
        public Guid Id { get; set; }

        public Guid CouponId { get; set; }

        /// <summary>The particular code that was typed.</summary>
        /// <remarks>
        /// Both this and <see cref="CouponId"/> are stored, rather than reaching the coupon through
        /// the code. The campaign-wide limit is checked on every redemption and the join to get
        /// there would be on the hot path; more to the point, "which code did they use?" is a
        /// question a generated batch exists to answer, and it should not depend on the code row
        /// still being there.
        /// </remarks>
        public Guid CouponCodeId { get; set; }

        public Guid OrganizationId { get; set; }

        /// <summary>Periods still to be discounted, counted down as each one is billed.</summary>
        /// <remarks>Null for a <c>Forever</c> coupon, which never runs out.</remarks>
        public int? PeriodsRemaining { get; set; }

        public DateTime RedeemedAtUtc { get; set; }

        /// <summary>The period price before the discount, frozen at redemption.</summary>
        /// <remarks>
        /// The money lives HERE, not looked up later: reimbursement math ("what do we owe the
        /// referrer for this?") must survive every later price change, tier retirement and coupon
        /// edit. Ben's requirement verbatim — track referrals in case somebody must be
        /// reimbursed for them.
        /// </remarks>
        public decimal ListPrice { get; set; }

        /// <summary>What the code took off this period.</summary>
        public decimal Discount { get; set; }

        /// <summary>What the group actually paid for the period.</summary>
        public decimal Payable { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual Coupon Coupon { get; set; } = null!;
        public virtual CouponCode CouponCode { get; set; } = null!;
        public virtual Organization Organization { get; set; } = null!;
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
    }
}
