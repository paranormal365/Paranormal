using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// Where one organization stands with the platform: which band it is on, and until when.
    /// </summary>
    /// <remarks>
    /// <para><b>One row per organization</b>, created when the organization is, so there is never a
    /// question of whether a missing row means free or unpaid. A group with no row would be exactly
    /// the ambiguity item 120 spent two days removing from lists.</para>
    ///
    /// <para><b>The member count is frozen, not recomputed.</b> Item 85's own note: member count
    /// becomes a billing input, so add- and remove-member become financially meaningful, and the
    /// tier boundary creates an incentive to under-report. The count and the tier are recorded
    /// <i>at the moment the period starts</i> and do not move for the rest of it. A group that
    /// grows mid-period is billed for the growth next period, not retroactively, and a group that
    /// shrinks the day before renewal does not get a refund it never paid for.</para>
    ///
    /// <para><b>Cancelling is not a status.</b> <see cref="CancelAtPeriodEnd"/> is a flag on an
    /// otherwise active subscription — see <see cref="SubscriptionStatus"/> for why.</para>
    /// </remarks>
    public class OrganizationSubscription : IAuditableEntity
    {
        public Guid Id { get; set; }

        public Guid OrganizationId { get; set; }

        public SubscriptionStatus Status { get; set; }

        /// <summary>
        /// The band this period was priced on. Null only before the first period is opened.
        /// </summary>
        public Guid? SubscriptionTierId { get; set; }

        /// <summary>Active members counted when the current period opened. See remarks.</summary>
        public int MemberCountAtPeriodStart { get; set; }

        /// <summary>How long each period lasts — the cadence the group chose and is billed on.</summary>
        /// <remarks>
        /// Stored here rather than read off the tier because the tier offers several cadences and
        /// this is the one that was picked. Changing it takes effect at the next renewal, like
        /// every other billing change.
        /// </remarks>
        public BillingInterval Interval { get; set; } = BillingInterval.Monthly;

        /// <summary>Price agreed for this period, copied from the tier so a later price change
        /// does not silently rewrite what was charged.</summary>
        public decimal PriceAtPeriodStart { get; set; }

        public DateTime? CurrentPeriodStart { get; set; }

        /// <summary>
        /// When the paid period runs out. Everything stays available until this passes — read and
        /// write — and after it the organization keeps read access but stops adding records.
        /// </summary>
        public DateTime? CurrentPeriodEnd { get; set; }

        /// <summary>
        /// Set when the organization has asked to stop. They keep every paid ability until
        /// <see cref="CurrentPeriodEnd"/>, and clearing this before then simply resumes billing.
        /// </summary>
        public bool CancelAtPeriodEnd { get; set; }

        /// <summary>When the period actually lapsed, for the wind-down in item 84.</summary>
        public DateTime? LapsedAtUtc { get; set; }

        /// <summary>
        /// The period end the two-week warning was sent for. Not-equal-to-current means unsent.
        /// </summary>
        /// <remarks>
        /// Storing the period end rather than a boolean makes renewal reset the notice for free:
        /// a new period is a new end date, so the stored value no longer matches and the next
        /// approach warns again. A boolean would need clearing in every place a period opens.
        /// </remarks>
        public DateTime? TwoWeekNoticeSentForPeriodEnd { get; set; }

        /// <summary>The period end the one-week notify-your-clients notice was sent for.</summary>
        public DateTime? OneWeekNoticeSentForPeriodEnd { get; set; }

        /// <summary>
        /// When this organization first paid for anything, or null if it never has.
        /// </summary>
        /// <remarks>
        /// The test behind <see cref="CouponApplicability.NewSubscriptionsOnly"/> and
        /// <see cref="CouponApplicability.RenewalsOnly"/>. It has to be its own field rather than
        /// "is there a period open": a group that lapsed in March is not a new subscription in
        /// June, and treating it as one hands the acquisition discount to the people the retention
        /// discount was written for. Set once and never cleared.
        /// </remarks>
        public DateTime? FirstPaidPeriodStartUtc { get; set; }

        /// <summary>
        /// The payment provider's own identifier for this subscription, once there is a provider.
        /// </summary>
        /// <remarks>
        /// Deliberately a loose string rather than a typed reference: Square and PayPal are both
        /// still candidates, and the domain does not care which one wrote it. Null while billing
        /// is handled outside the platform.
        /// </remarks>
        public string? ProviderSubscriptionRef { get; set; }

        /// <summary>Which provider wrote <see cref="ProviderSubscriptionRef"/>, when one has.</summary>
        public string? ProviderName { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual Organization Organization { get; set; } = null!;
        public virtual SubscriptionTier? SubscriptionTier { get; set; }
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
    }
}
