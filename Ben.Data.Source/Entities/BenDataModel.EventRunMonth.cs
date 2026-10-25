using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// One extra month for a show's run (10/24/2026).
    /// </summary>
    /// <remarks>
    /// <para><b>Ben's rules.</b> One event credit covers a show's run for a month from its first performance.
    /// "It is 50% off to add each additional month", bought only by choosing a published show that isn't over.
    /// "They can only extend the dates and not be considered being used to start a new one. It is separate
    /// than a credit for an event." When the show is called off, its months always come back and can lengthen
    /// any other show; a month held lapses a year after it was bought; a show takes at most eleven.</para>
    ///
    /// <para><b>Its own table, never a kind of credit.</b> Publishing reads only event credits, so nothing
    /// here can stand in for one.</para>
    /// </remarks>
    public partial class EventRunMonth : IAuditableEntity
    {
        public Guid Id { get; set; }

        /// <summary>The group that paid, and holds it when it comes back. Exactly one of the two owners.</summary>
        public Guid? OwnerOrganizationId { get; set; }

        /// <summary>The person who paid, when it was not a group.</summary>
        public Guid? OwnerAppUserId { get; set; }

        /// <summary>What was paid, frozen. Zero for one SuperAdmin gave.</summary>
        public decimal PriceAtPurchase { get; set; }

        /// <summary>Currency of <see cref="PriceAtPurchase"/>, ISO-4217.</summary>
        public string Currency { get; set; } = "USD";

        public DateTime PurchasedUtc { get; set; }

        /// <summary>A year after it was bought. Only matters while it is held.</summary>
        public DateTime ExpiresUtc { get; set; }

        /// <summary>When the holder was warned it was about to lapse, so they are warned once.</summary>
        public DateTime? ExpiryWarningSentUtc { get; set; }

        /// <summary>The show it lengthens. Null while it is held.</summary>
        public Guid? AppliedToHostedEventId { get; set; }

        /// <summary>When it was applied to <see cref="AppliedToHostedEventId"/>.</summary>
        public DateTime? AppliedUtc { get; set; }

        /// <summary>Why it was given rather than bought. Null for every month somebody paid for.</summary>
        public string? GrantedReason { get; set; }

        /// <summary>When the money went back. A refunded month counts for nothing.</summary>
        public DateTime? RefundedUtc { get; set; }
        public Guid? RefundedByAppUserId { get; set; }
        public string? RefundedReason { get; set; }

        /// <summary>The provider's checkout session, for tracing a purchase back.</summary>
        public string? ProviderCheckoutRef { get; set; }

        /// <summary>The provider's payment, which is also what makes fulfilment idempotent.</summary>
        public string? ProviderPaymentRef { get; set; }

        /// <summary>The receipt number on the ledger rows this purchase wrote.</summary>
        public string? ReceiptNumber { get; set; }

        /// <summary>Held: not on a show, not refunded, not lapsed.</summary>
        public bool IsHeld(DateTime now)
            => AppliedToHostedEventId is null && RefundedUtc is null && ExpiresUtc > now;

        /// <summary>Counting toward a show's run: on it, and not refunded.</summary>
        public bool Counts => AppliedToHostedEventId is not null && RefundedUtc is null;

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual Organization? OwnerOrganization { get; set; }
        public virtual AppUser? OwnerAppUser { get; set; }
        public virtual HostedEvent? AppliedToHostedEvent { get; set; }
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
    }
}
