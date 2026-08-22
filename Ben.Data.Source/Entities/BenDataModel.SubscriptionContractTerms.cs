using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// What one organization actually bought for one period — the contract, frozen.
    /// </summary>
    /// <remarks>
    /// <para><b>Why a copy and not a reference.</b> The tier rows are live and a SuperAdmin edits
    /// them; the deal a group paid for must not move when that happens. Ben's rule: the tier they
    /// signed up for is a contract for the term. So a period opening takes a snapshot, and
    /// everything that enforces or displays a paid group's terms reads the snapshot through
    /// <c>EffectiveTermsResolver</c> — which lets live <i>improvements</i> through and holds the
    /// line against reductions until renewal.</para>
    ///
    /// <para><b>The limits are JSON, deliberately.</b> They are read whole, never queried by key,
    /// and a frozen copy must not join back to the live rows — drifting with the live tier is
    /// precisely the failure this table exists to prevent. The tier <i>name</i> is copied for the
    /// same reason: "Small group" can be renamed, but the receipt says what it said.</para>
    ///
    /// <para><b>One row per period, kept forever.</b> The previous period's row is the answer to
    /// "what were they promised in March?", which is the same append-only reasoning as
    /// <see cref="CouponRedemption"/>.</para>
    /// </remarks>
    public class SubscriptionContractTerms : IAuditableEntity
    {
        public Guid Id { get; set; }

        public Guid OrganizationSubscriptionId { get; set; }

        /// <summary>The tier this was copied from. Restrict-deleted, so it stays resolvable.</summary>
        public Guid SubscriptionTierId { get; set; }

        /// <summary>The band's name as it was sold, immune to renames.</summary>
        public string TierName { get; set; } = string.Empty;

        public BillingInterval Interval { get; set; }

        /// <summary>The price actually agreed for the period.</summary>
        public decimal Price { get; set; }

        /// <summary>
        /// The caps as sold, serialized as {"OpenCases":10,"StorageMegabytes":null,...}.
        /// A key absent from the JSON was uncapped at signing; null is written-down-unlimited.
        /// </summary>
        public string LimitsJson { get; set; } = "{}";

        public DateTime PeriodStartUtc { get; set; }
        public DateTime PeriodEndUtc { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual OrganizationSubscription OrganizationSubscription { get; set; } = null!;
        public virtual SubscriptionTier SubscriptionTier { get; set; } = null!;
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
    }
}
