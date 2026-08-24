using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// A discount a SuperAdmin can issue: what comes off, for how long, and under what conditions.
    /// </summary>
    /// <remarks>
    /// <para><b>The coupon is the campaign; the code is in <see cref="Codes"/>.</b> One row here
    /// with one code under it is the flyer-and-conference-badge case. One row here with two hundred
    /// codes under it, each capped at a single redemption, is the generated-batch case. Both use
    /// the same discount, the same window and the same duration, which is why they are one entity
    /// and not two. See <see cref="CouponCode"/>.</para>
    ///
    /// <para><b>Percent or amount, never both.</b> Two discount fields that can each be set invite
    /// the question "what happens if both are?", and every answer to that question is a surprise to
    /// somebody. <c>CouponMath</c> rejects a coupon that sets neither or both rather than picking
    /// one.</para>
    ///
    /// <para><b>Retired, not deleted.</b> A coupon that has priced a period is part of the billing
    /// record — the same rule as <see cref="SubscriptionTier"/>.</para>
    /// </remarks>
    public class Coupon : IAuditableEntity
    {
        public Guid Id { get; set; }

        /// <summary>
        /// A name for the campaign, shown to the SuperAdmin managing it rather than to the group.
        /// </summary>
        /// <remarks>
        /// Required, unlike the old free-text description: with the code moved to
        /// <see cref="Codes"/> there is nothing else to identify a batch by, and a list of
        /// two-hundred-code batches with no names is unusable.
        /// </remarks>
        public string Name { get; set; } = string.Empty;

        /// <summary>Longer notes for whoever inherits this campaign. Never shown to the group.</summary>
        public string? Description { get; set; }

        /// <summary>Whether this is one shared code or a batch of individually-issued ones.</summary>
        public CouponKind Kind { get; set; }

        /// <summary>Percentage off, 1–100. Mutually exclusive with <see cref="AmountOff"/>.</summary>
        public int? PercentOff { get; set; }

        /// <summary>Fixed amount off a period. Mutually exclusive with <see cref="PercentOff"/>.</summary>
        public decimal? AmountOff { get; set; }

        public CouponDuration Duration { get; set; }

        /// <summary>How many periods a <see cref="CouponDuration.Repeating"/> coupon covers.</summary>
        public int? DurationPeriods { get; set; }

        /// <summary>
        /// Total redemptions allowed across every code and every organization, or null for unlimited.
        /// </summary>
        /// <remarks>
        /// Sits on top of each code's own <see cref="CouponCode.MaxRedemptions"/>. A batch of five
        /// hundred single-use codes with fifty here stops at fifty, however many codes are still
        /// unclaimed — which is how a campaign gets a budget rather than a print run.
        /// </remarks>
        public int? MaxRedemptions { get; set; }

        /// <summary>
        /// Redemptions so far across the whole campaign. A cache, not the authority.
        /// </summary>
        /// <remarks>
        /// Two organizations redeeming the last use at the same moment is a real race, so the
        /// redemption is guarded by the unique index on <c>CouponRedemption</c> and this column is
        /// the fast path.
        /// </remarks>
        public int RedemptionCount { get; set; }

        /// <summary>
        /// First moment the code may be redeemed, or null to mean "already".
        /// </summary>
        /// <remarks>
        /// Worth having alongside <see cref="RedeemByUtc"/>: a campaign that opens with a
        /// conference is written days before it starts, and the alternative is somebody
        /// remembering to flip <see cref="IsActive"/> at the right hour.
        /// </remarks>
        public DateTime? ValidFromUtc { get; set; }

        /// <summary>Last moment the code may be redeemed. Redemptions already made are unaffected.</summary>
        public DateTime? RedeemByUtc { get; set; }

        /// <summary>
        /// Restricts the coupon to one billing cadence, or null to allow any.
        /// </summary>
        /// <remarks>
        /// "20% off your first year" is a different offer from "20% off", and without this the
        /// first one can be redeemed against a monthly subscription for a fifth of the intended
        /// discount. Ben asked for a yearly discount; this is what makes one expressible as a
        /// coupon rather than only as a price.
        /// </remarks>
        public BillingInterval? AppliesToInterval { get; set; }

        /// <summary>
        /// Whether this buys new groups, keeps existing ones, or does either.
        /// </summary>
        /// <remarks>
        /// Separate from <see cref="AppliesToInterval"/>, which restricts the <i>cadence</i>. This
        /// restricts the <i>occasion</i>, and the two compose: "20% off a yearly renewal" is both
        /// set at once.
        /// </remarks>
        public CouponApplicability AppliesTo { get; set; } = CouponApplicability.Any;

        public bool IsActive { get; set; } = true;

        /// <summary>
        /// The person whose referrals this coupon tracks (item 168). Every redemption of a
        /// referrer-owned coupon is a referral attributed to them — the redemption rows already
        /// freeze the money, this names who it belongs to. Null for ordinary campaign coupons.
        /// </summary>
        public Guid? ReferrerAppUserId { get; set; }

        /// <summary>
        /// The referrer's cut, as a percent of what redeeming groups actually pay (the frozen
        /// <see cref="CouponRedemption.Payable"/> amounts). Per campaign, because deals differ
        /// per referrer — Ben's rule. Null means no computed figure: standings show both sides
        /// and a human settles it.
        /// </summary>
        public decimal? ReferralCommissionPercent { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual AppUser? ReferrerAppUser { get; set; }
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
        public virtual ICollection<CouponCode> Codes { get; set; } = [];
        public virtual ICollection<CouponRedemption> Redemptions { get; set; } = [];
    }
}
