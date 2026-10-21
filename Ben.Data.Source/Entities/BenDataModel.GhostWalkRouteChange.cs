using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// One change to how many tour routes a Ghost Walk Tour subscription pays for, part-way through a
    /// month, and what it is worth (10/21/2026).
    /// </summary>
    /// <remarks>
    /// <para><b>Prorated the way Stripe prorates by default.</b> Raising the quantity on day ten of a
    /// thirty-day month owes the twenty days left for the extra route; lowering it gives back the
    /// twenty days not used. Nothing is charged or refunded at the moment of the change: like
    /// Stripe's default, the amount waits as a line on the next monthly bill, which is when
    /// <see cref="SettledUtc"/> is stamped.</para>
    ///
    /// <para><b>A credit carried forward</b> is a row with no quantity change and a negative amount:
    /// what was left over when a month's credits came to more than its bill.</para>
    /// </remarks>
    public class GhostWalkRouteChange : IAuditableEntity
    {
        public Guid Id { get; set; }
        public Guid GhostWalkSubscriptionId { get; set; }

        public DateTime ChangedUtc { get; set; }
        public int FromQuantity { get; set; }
        public int ToQuantity { get; set; }

        /// <summary>What the change adds to the next bill, before tax. Negative is a credit.</summary>
        public decimal ProratedAmount { get; set; }

        /// <summary>When it went on a bill. Null while it is still waiting for the next one.</summary>
        public DateTime? SettledUtc { get; set; }

        /// <summary>The payment it was settled with, for tracing a bill line back.</summary>
        public string? SettledPaymentRef { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual GhostWalkSubscription GhostWalkSubscription { get; set; } = null!;
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
    }
}
