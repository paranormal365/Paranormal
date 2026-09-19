using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// One person's own paid seat in a group that has outgrown its band (item 144, Ben's
    /// overflow-seat model): the group's contract stays one row at its band, and each extra
    /// member beyond the band's cap is billed individually at the tier's per-extra-member price.
    /// </summary>
    /// <remarks>
    /// <para>The price is FROZEN here at creation, the same rule as every other money figure in
    /// this schema — a later tier edit must not reprice a seat somebody already agreed to.</para>
    /// <para>One row per (organization, person): a member either holds a seat in a group or does
    /// not; renewal updates the row's period, it does not add rows.</para>
    /// </remarks>
    public class MemberSeatSubscription : IAuditableEntity
    {
        public Guid Id { get; set; }

        public Guid OrganizationId { get; set; }

        public Guid AppUserId { get; set; }

        /// <summary>Reuses the org-subscription lifecycle: Pending payment → Active → Lapsed/Canceled.</summary>
        public SubscriptionStatus Status { get; set; }

        public BillingInterval Interval { get; set; } = BillingInterval.Monthly;

        /// <summary>The per-extra-member price at the moment the seat was offered, frozen.</summary>
        public decimal PriceAtStart { get; set; }

        public DateTime? CurrentPeriodStart { get; set; }
        public DateTime? CurrentPeriodEnd { get; set; }

        /// <summary>Which provider bills this seat, when one does. Null in the manual era.</summary>
        public string? ProviderName { get; set; }

        /// <summary>The provider's customer record for THIS MEMBER (Stripe: <c>cus_…</c>).</summary>
        /// <remarks>
        /// The member's own, never the group's: a seat is a bill addressed to one person, and
        /// their card must never be charged under the group's customer or vice versa.
        /// </remarks>
        public string? ProviderCustomerRef { get; set; }

        /// <summary>The saved payment method seat renewals charge (Stripe: <c>pm_…</c>).</summary>
        public string? ProviderPaymentMethodRef { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual Organization Organization { get; set; } = null!;
        public virtual AppUser AppUser { get; set; } = null!;
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
    }
}
