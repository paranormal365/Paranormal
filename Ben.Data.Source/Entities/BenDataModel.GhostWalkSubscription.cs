using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// A group's Ghost Walk Tour subscription: one flat monthly price, no limit on tours or guests.
    /// </summary>
    /// <remarks>
    /// <para><b>Ben's rule, 10/21/2026:</b> "Ghost Walk tours are their own individual subscription."
    /// It is separate from the investigation plan on purpose. A group can hold both, either, or
    /// neither, so it cannot live on <see cref="OrganizationSubscription"/>, which is one row per
    /// group and prices a team by its size.</para>
    ///
    /// <para><b>Monthly only, and the price is a site setting.</b> Each period freezes what it was
    /// charged in <see cref="PriceAtPeriodStart"/>, so moving the setting reaches the next renewal
    /// and never rewrites a period somebody already paid for.</para>
    ///
    /// <para><b>Active means paid through.</b> A row is a standing subscription while
    /// <see cref="Status"/> is Active and <see cref="CurrentPeriodEnd"/> is still ahead. Nothing has
    /// to lapse it: a period that ran out without a renewal simply stops counting.</para>
    /// </remarks>
    public class GhostWalkSubscription : IAuditableEntity
    {
        public Guid Id { get; set; }

        /// <summary>The group it belongs to. One row per group; renewing updates it.</summary>
        public Guid OrganizationId { get; set; }

        public SubscriptionStatus Status { get; set; }

        /// <summary>What this period was charged, before tax, frozen at the moment it opened.</summary>
        public decimal PriceAtPeriodStart { get; set; }

        public DateTime? CurrentPeriodStart { get; set; }
        public DateTime? CurrentPeriodEnd { get; set; }

        /// <summary>Set when the group has said not to renew. The paid period still runs out.</summary>
        public bool CancelAtPeriodEnd { get; set; }

        /// <summary>"Stripe" for a card payment. Null before the first one.</summary>
        public string? ProviderName { get; set; }

        /// <summary>The checkout session or payment that opened the current period.</summary>
        public string? ProviderSubscriptionRef { get; set; }

        /// <summary>The Stripe customer the card is saved under, for renewals.</summary>
        public string? ProviderCustomerRef { get; set; }

        /// <summary>The saved card the renewal job charges.</summary>
        public string? ProviderPaymentMethodRef { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        /// <summary>Whether this subscription covers tours at <paramref name="now"/>.</summary>
        public bool IsStanding(DateTime now)
            => Status == SubscriptionStatus.Active && CurrentPeriodEnd is { } end && end > now;

        public virtual Organization Organization { get; set; } = null!;
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
    }
}
