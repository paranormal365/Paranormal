using Ben.Data.Common.Enums;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// One line of what a seller is owed (store sellers, backlog 251, P10). Append-only: a correction
    /// is another line, never an edit.
    /// </summary>
    /// <remarks>
    /// <para><b>Written when it happens, once.</b> A package's sale and label lines are written when
    /// it ships, in the same transaction; a refund's line when Stripe completes it. Filtered unique
    /// indexes make a second write of the same thing fail rather than pay twice.</para>
    ///
    /// <para><b>Claimed by a payout.</b> Recording a payment claims the unpaid lines up to a date
    /// with one conditional update; voiding the payment releases them. A line is never paid twice
    /// and never lost.</para>
    ///
    /// <para>This reverses the storefront's "no store ledger" decision: payouts are the consumer that
    /// was missing (Ben, 09/24/2026: payouts are manual, and the site keeps the record).</para>
    /// </remarks>
    public class StoreSellerEarning
    {
        public Guid Id { get; set; }
        public Guid SellerAppUserId { get; set; }
        public StoreEarningKind Kind { get; set; }

        /// <summary>Signed: a refund's line is negative, an adjustment either.</summary>
        public decimal Amount { get; set; }

        public int? Units { get; set; }
        public Guid? OrderId { get; set; }
        public Guid? ParcelId { get; set; }
        public Guid? OrderItemId { get; set; }
        public Guid? RefundId { get; set; }

        /// <summary>The product it is for, when it is for one — the per-item summary reads this.</summary>
        public Guid? ProductId { get; set; }

        public string? Note { get; set; }
        public DateTime OccurredUtc { get; set; }

        /// <summary>The payment that paid it; null while owed.</summary>
        public Guid? PayoutId { get; set; }

        /// <summary>Who wrote an adjustment; null for the lines the store writes itself.</summary>
        public Guid? CreatedByAppUserId { get; set; }

        public virtual AppUser SellerAppUser { get; set; } = null!;
        public virtual StoreSellerPayout? Payout { get; set; }
    }

    /// <summary>
    /// A payment the store made to a seller outside the site — a bank transfer, a check — recorded
    /// here so both sides can see what has been paid (store sellers, backlog 251, P10).
    /// </summary>
    public class StoreSellerPayout
    {
        public const int MaxNoteLength = 300;

        public Guid Id { get; set; }
        public Guid SellerAppUserId { get; set; }

        /// <summary>The sum of the lines it claimed, worked out in C# from those lines — never typed.</summary>
        public decimal Amount { get; set; }

        /// <summary>It pays every unpaid line up to this moment.</summary>
        public DateTime CutoffUtc { get; set; }

        /// <summary>When the money was sent.</summary>
        public DateTime PaidOnUtc { get; set; }

        /// <summary>How it was sent — "Bank transfer, ref 4471".</summary>
        public string? Reference { get; set; }

        public Guid RecordedByAppUserId { get; set; }
        public DateTime RecordedUtc { get; set; }

        public DateTime? VoidedUtc { get; set; }
        public Guid? VoidedByAppUserId { get; set; }
        public string? VoidReason { get; set; }

        public virtual AppUser SellerAppUser { get; set; } = null!;
    }
}
