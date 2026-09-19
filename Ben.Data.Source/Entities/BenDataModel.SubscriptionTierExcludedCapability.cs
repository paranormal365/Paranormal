using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// One capability a subscription tier EXCLUDES (item 167).
    /// </summary>
    /// <remarks>
    /// Exclusion rows, deliberately the inverse of <see cref="SubscriptionTierPermissionArea"/>'s
    /// inclusion rows: with only one capability defined, an inclusion model cannot tell "never
    /// configured" from "explicitly none" — unchecking the only capability would leave zero rows,
    /// which fail-open reads as everything-included, and the uncheck would silently do nothing.
    /// Exclusions make the fail-open property structural: zero rows means nothing excluded, no
    /// seeding or backfill required, and excluding the only capability writes exactly one row.
    /// </remarks>
    public class SubscriptionTierExcludedCapability : IAuditableEntity
    {
        public Guid Id { get; set; }
        public Guid SubscriptionTierId { get; set; }
        public TierCapability Capability { get; set; }
        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual SubscriptionTier SubscriptionTier { get; set; } = null!;
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
    }
}
