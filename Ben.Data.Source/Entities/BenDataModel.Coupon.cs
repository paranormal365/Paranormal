using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// A discount code a SuperAdmin can issue and an organization can redeem against its
    /// subscription.
    /// </summary>
    /// <remarks>
    /// <para><b>Percent or amount, never both.</b> Two discount fields that can each be set invite
    /// the question "what happens if both are?", and every answer to that question is a surprise to
    /// somebody. <c>CouponMath</c> rejects a coupon that sets neither or both rather than picking
    /// one.</para>
    ///
    /// <para><b>Codes are matched case-insensitively</b> and stored upper-cased. A code is read off
    /// an email or a conference badge and typed by hand; treating <c>launch25</c> and
    /// <c>LAUNCH25</c> as different codes creates a support ticket, not a security boundary.</para>
    ///
    /// <para><b>Retired, not deleted.</b> A coupon that has priced a period is part of the billing
    /// record — the same rule as <see cref="SubscriptionTier"/>.</para>
    /// </remarks>
    public class Coupon : IAuditableEntity
    {
        public Guid Id { get; set; }

        /// <summary>The code as typed, stored upper-cased. Unique across active and retired alike.</summary>
        public string Code { get; set; } = string.Empty;

        /// <summary>Shown to the SuperAdmin issuing it, not to the organization.</summary>
        public string? Description { get; set; }

        /// <summary>Percentage off, 1–100. Mutually exclusive with <see cref="AmountOff"/>.</summary>
        public int? PercentOff { get; set; }

        /// <summary>Fixed amount off a period. Mutually exclusive with <see cref="PercentOff"/>.</summary>
        public decimal? AmountOff { get; set; }

        public CouponDuration Duration { get; set; }

        /// <summary>How many periods a <see cref="CouponDuration.Repeating"/> coupon covers.</summary>
        public int? DurationPeriods { get; set; }

        /// <summary>
        /// Total redemptions allowed across all organizations, or null for unlimited.
        /// </summary>
        public int? MaxRedemptions { get; set; }

        /// <summary>
        /// Redemptions so far. Kept as a column rather than counted from
        /// <see cref="CouponRedemption"/> on every check.
        /// </summary>
        /// <remarks>
        /// A cache with a real race in it — two organizations redeeming the last use of a coupon at
        /// the same moment — so the redemption itself is guarded by the unique index on
        /// <c>CouponRedemption</c> and this column is the fast path, not the authority.
        /// </remarks>
        public int RedemptionCount { get; set; }

        /// <summary>Last day the code may be redeemed. Redemptions already made are unaffected.</summary>
        public DateTime? RedeemByUtc { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
        public virtual ICollection<CouponRedemption> Redemptions { get; set; } = [];
    }
}
