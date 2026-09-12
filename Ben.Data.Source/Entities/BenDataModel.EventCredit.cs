using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// One event, bought (item 235).
    /// </summary>
    /// <remarks>
    /// <para><b>Ben's rule, 2026-09-11:</b> "If someone hosts an event and is hosting from their
    /// group or organization, they can buy an event credit. This is a single event they can
    /// schedule or host. They have a year to have hosted the event otherwise they lose the credit.
    /// For multiple events they would need multiple credits, one for each event."</para>
    ///
    /// <para><b>Why a credit rather than a monthly charge.</b> Metering an event month by month
    /// anchors its price to a tour's rate, and a tour is cheap, constant and durable in a way a
    /// weekend with classes is not. It would price a hotel weekend as a walk round a block, and it
    /// would reach none of the groups that run one fundraiser a year — they will not take out a
    /// subscription for a weekend, so today they are worth nothing to us.</para>
    ///
    /// <para><b>Spent at publish.</b> Not at creation: a draft has no page, takes no bookings and
    /// sends no confirmations, so building one costs nothing and a mistyped event never costs real
    /// money. Publishing is the act that lets other people's answers start arriving, which is the
    /// last moment at which stopping is still free — and it is the same moment an event starts
    /// occupying a slot on a business plan, so one idea covers both kinds of customer.</para>
    ///
    /// <para><b>Not returned.</b> The confirmation says so before the button is pressed. Taking an
    /// event down does not give the credit back, and putting it up again never spends a second one:
    /// one event, one credit, for the life of that event.</para>
    /// </remarks>
    public partial class EventCredit : IAuditableEntity
    {
        public Guid Id { get; set; }

        /// <summary>
        /// The group that holds it. Null when a person holds it in their own right.
        /// </summary>
        /// <remarks>
        /// Ben said "the member or group", so both can. Exactly one of this and
        /// <see cref="OwnerAppUserId"/> is set, which the context enforces with a check
        /// constraint — a credit belonging to nobody, or to two owners, is unspendable either way.
        /// </remarks>
        public Guid? OwnerOrganizationId { get; set; }

        /// <summary>The person who holds it, when it is not a group's.</summary>
        public Guid? OwnerAppUserId { get; set; }

        /// <summary>What was paid, frozen.</summary>
        /// <remarks>
        /// The price is a site setting and will move. Freezing it here means moving it never
        /// reaches a credit somebody already holds, and a receipt written last year still adds up.
        /// </remarks>
        public decimal PriceAtPurchase { get; set; }

        /// <summary>Currency of <see cref="PriceAtPurchase"/>, ISO-4217.</summary>
        public string Currency { get; set; } = "USD";

        public DateTime PurchasedUtc { get; set; }

        /// <summary>
        /// When it lapses if it has not been spent — a year after it was bought.
        /// </summary>
        /// <remarks>
        /// Checked at both ends when it is spent: the credit must be unexpired AND the event's
        /// first date must fall inside its year. That is Ben's "a year to have hosted the event"
        /// read literally, and it stops a credit being parked by publishing a placeholder dated
        /// years out.
        /// </remarks>
        public DateTime ExpiresUtc { get; set; }

        /// <summary>When the holder was warned it was about to lapse, so they are warned once.</summary>
        public DateTime? ExpiryWarningSentUtc { get; set; }

        /// <summary>When it was spent. Null while it is still in hand.</summary>
        public DateTime? SpentUtc { get; set; }

        /// <summary>The event it went on, so "have we paid for this one?" has one answer for ever.</summary>
        public Guid? SpentOnHostedEventId { get; set; }

        /// <summary>
        /// Why this credit was handed over rather than bought. Null for every credit somebody paid
        /// for, which is nearly all of them.
        /// </summary>
        /// <remarks>
        /// <para>A granted credit is the remedy for the cases a payment cannot answer: a purchase
        /// that never landed, an apology, a credit somebody was promised. Before it existed the
        /// only way to put one in a group's hands was a live card payment, so support had nothing
        /// to offer and the refund screen could not be exercised at all.</para>
        ///
        /// <para><b>It is not a sale.</b> <see cref="PriceAtPurchase"/> is zero, there is no
        /// provider reference and no receipt, and nothing reaches the ledger — a $0 charge and
        /// payment pair would put a sale that never happened into the money trail. The reason and
        /// <see cref="CreatedByAppUserId"/> are the record instead, which is why the reason is
        /// required.</para>
        ///
        /// <para>In every other respect it behaves exactly like a bought one: a year to use, spent
        /// at publish, oldest first, warned at thirty days, refundable while unspent.</para>
        /// </remarks>
        public string? GrantedReason { get; set; }

        /// <summary>True when nobody paid for this one. See <see cref="GrantedReason"/>.</summary>
        public bool WasGranted => GrantedReason is not null;

        /// <summary>When it was refunded. A SuperAdmin's act, never a button.</summary>
        public DateTime? RefundedUtc { get; set; }

        /// <summary>Who refunded it, and why.</summary>
        public Guid? RefundedByAppUserId { get; set; }
        public string? RefundedReason { get; set; }

        /// <summary>The provider's checkout session, for tracing a purchase back.</summary>
        public string? ProviderCheckoutRef { get; set; }

        /// <summary>The provider's payment, which is also what makes fulfilment idempotent.</summary>
        public string? ProviderPaymentRef { get; set; }

        /// <summary>The receipt number on the ledger rows this purchase wrote.</summary>
        public string? ReceiptNumber { get; set; }

        /// <summary>In hand: bought, not spent, not refunded, not lapsed.</summary>
        public bool IsSpendable(DateTime now)
            => SpentUtc is null && RefundedUtc is null && ExpiresUtc > now;

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual Organization? OwnerOrganization { get; set; }
        public virtual AppUser? OwnerAppUser { get; set; }
        public virtual HostedEvent? SpentOnHostedEvent { get; set; }
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
    }
}
