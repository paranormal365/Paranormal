using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// One permission area a subscription tier includes (item 156, decision D1).
    /// </summary>
    /// <remarks>
    /// Presence is the entitlement: a row means this tier's groups may use custom-role
    /// permissions in this area; no row means the role editor grays that area out with an
    /// upgrade note, and (from Phase D) grants in it stop applying at runtime — stored,
    /// grayed-but-remembered, resuming on upgrade (D4). Modeled as keyed rows for the same
    /// reason <see cref="SubscriptionTierLimit"/> is: a checklist the SuperAdmin edits, not a
    /// column-per-idea migration treadmill.
    /// </remarks>
    public class SubscriptionTierPermissionArea : IAuditableEntity
    {
        public Guid Id { get; set; }
        public Guid SubscriptionTierId { get; set; }
        public OrganizationPermissionArea Area { get; set; }
        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual SubscriptionTier SubscriptionTier { get; set; } = null!;
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
    }
}
