using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// One band in the platform's price list: a member-count range, and what it costs to be in it.
    /// </summary>
    /// <remarks>
    /// <para><b>Rows, not constants.</b> Prices change, and a price change should not be a
    /// deployment. Keeping the bands in the database also means the boundary arithmetic is tested
    /// against the same rows production uses rather than against a literal in a switch.</para>
    ///
    /// <para><b>The bands must tile the whole range.</b> Every possible member count belongs to
    /// exactly one active band, or an organization can grow into a gap and owe nothing by accident.
    /// <c>SubscriptionTierResolver</c> enforces that rather than trusting whoever edits the rows.</para>
    ///
    /// <para><see cref="MaxMembers"/> is null for the top band, meaning unbounded. A number there
    /// would need editing every time a group outgrows it, which is the failure mode this avoids.</para>
    ///
    /// <para><b>The band does not hold a price.</b> It holds <see cref="Prices"/> — one row per
    /// billing cadence — because a band sold monthly and yearly has two prices and neither is
    /// derived from the other. See <see cref="SubscriptionTierPrice"/>.</para>
    /// </remarks>
    public class SubscriptionTier : IAuditableEntity
    {
        public Guid Id { get; set; }

        /// <summary>Shown to the organization — "Small group", "Free", and so on.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Fewest active members this band covers. The lowest band starts at 1.</summary>
        public int MinMembers { get; set; }

        /// <summary>Most active members this band covers, or null for the unbounded top band.</summary>
        public int? MaxMembers { get; set; }

        /// <summary>Display order in the price list; independent of the member bands.</summary>
        public int SortOrder { get; set; }

        /// <summary>
        /// Retired bands stay for the periods that were billed against them.
        /// </summary>
        /// <remarks>
        /// Deleting a tier somebody was charged on would leave an invoice pointing at nothing —
        /// the same retire-instead-of-delete rule the equipment work settled on.
        /// </remarks>
        public bool IsActive { get; set; } = true;

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }

        /// <summary>
        /// What this band costs, one row per cadence. Zero is a real, deliberate price — the free
        /// band — and is why <see cref="Ben.Data.Common.Enums.SubscriptionStatus.Free"/> exists as
        /// its own status rather than being inferred from an absent row.
        /// </summary>
        public virtual ICollection<SubscriptionTierPrice> Prices { get; set; } = [];

        /// <summary>
        /// What this band caps — open cases, equipment, loans, storage. A cap with no row is no
        /// cap at all, which is the safe default; see <see cref="SubscriptionTierLimit"/>.
        /// </summary>
        public virtual ICollection<SubscriptionTierLimit> Limits { get; set; } = [];
    }
}
