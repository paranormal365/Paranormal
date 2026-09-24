using Ben.Data.Common.Enums;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// Money given back on an order (storefront).
    /// </summary>
    /// <remarks>
    /// <para>The row is written BEFORE Stripe is called. Its id rides in the Stripe refund's metadata
    /// and in the idempotency key <c>store-refund-{Id:N}-{Attempt}</c>; a retry lists Stripe's
    /// refunds first and adopts one it finds, and only then creates with the next
    /// <see cref="Attempt"/> — the same key is never sent twice (Stripe replays the original answer
    /// for 24 hours and makes a second refund after).</para>
    ///
    /// <para>Only a <c>succeeded</c> refund restocks, writes <c>RefundedAmount</c>, sends the letter and
    /// reverses tax; a pending one waits for Stripe's event.</para>
    /// </remarks>
    public class StoreRefund
    {
        public Guid Id { get; set; }
        public Guid OrderId { get; set; }
        public decimal Amount { get; set; }
        public decimal TaxReversed { get; set; }
        public string Reason { get; set; } = string.Empty;
        public bool Restock { get; set; }
        public StoreRefundStatus Status { get; set; }
        public int Attempt { get; set; } = 1;
        public string? StripeRefundId { get; set; }
        public string? StripeTaxReversalId { get; set; }
        public string? FailureReason { get; set; }

        /// <summary>The admin who asked; null for a refund made in the Stripe dashboard.</summary>
        public Guid? RequestedByAppUserId { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime? CompletedUtc { get; set; }

        public virtual StoreOrder Order { get; set; } = null!;
        public virtual AppUser? RequestedByAppUser { get; set; }
        public virtual ICollection<StoreRefundItem> Items { get; set; } = [];
    }
}
