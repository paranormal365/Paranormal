using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// One line of the money trail (item 168): a charge, a payment, an adjustment, or a referral
    /// payout. <b>Append-only</b> — there is no update or delete path anywhere; a mistake is
    /// corrected by an Adjustment row that names it, the way a paper ledger works.
    /// </summary>
    /// <remarks>
    /// <para>Everything needed to reprint the document is frozen ON the row — tax rate, tax
    /// amount, description — the same reasoning as <see cref="CouponRedemption.ListPrice"/> and
    /// the contract-terms snapshot: a receipt reprinted next year must say what it said, whatever
    /// has happened to tax rules or tiers since.</para>
    /// <para><see cref="OrganizationId"/> is null exactly when <see cref="Kind"/> is
    /// <see cref="BillingLedgerKind.ReferralPayout"/> — a payout goes to a person, not a group —
    /// and <see cref="ReferrerAppUserId"/> is set exactly then.</para>
    /// </remarks>
    public class BillingLedgerEntry : IAuditableEntity
    {
        public Guid Id { get; set; }

        public BillingLedgerKind Kind { get; set; }

        /// <summary>The group this row bills or credits. Null only for referral payouts.</summary>
        public Guid? OrganizationId { get; set; }

        /// <summary>Who the payout went to. Set only for referral payouts.</summary>
        public Guid? ReferrerAppUserId { get; set; }

        /// <summary>Pre-tax amount, always positive. Direction comes from <see cref="Kind"/>;
        /// adjustments carry <see cref="AdjustmentIsCredit"/> to say which way they cut.</summary>
        public decimal Amount { get; set; }

        /// <summary>True when an Adjustment reduces what the group owes; irrelevant otherwise.</summary>
        public bool AdjustmentIsCredit { get; set; }

        /// <summary>The rate applied, frozen at write time. Zero when no rule matched the
        /// group's state — many states do not tax this.</summary>
        public decimal TaxRatePercent { get; set; }

        /// <summary>Tax in dollars, frozen — never recomputed from the rate.</summary>
        public decimal TaxAmount { get; set; }

        /// <summary>What this line is, in a sentence. Required — an unexplained ledger row is a
        /// question nobody can answer later.</summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>Check number, transfer id, processor reference — whatever proves the payment.</summary>
        public string? PaymentReference { get; set; }

        /// <summary>Sequential receipt number, assigned to Payment rows only. Unique; stable
        /// forever — the payer re-downloads the same receipt by the same number.</summary>
        public int? ReceiptNumber { get; set; }

        /// <summary>The billing period this row covers, when it covers one.</summary>
        public DateTime? PeriodStart { get; set; }
        public DateTime? PeriodEnd { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual Organization? Organization { get; set; }
        public virtual AppUser? ReferrerAppUser { get; set; }
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
    }
}
