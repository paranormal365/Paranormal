using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// One rung of an organization's member-title ladder — "Investigator", "Senior
    /// Investigator" (item 157).
    /// </summary>
    /// <remarks>
    /// A title is seniority, never permission: it grants nothing, and no code may ever read it
    /// to decide access. Roles (<see cref="OrganizationRole"/>) define permission sets; titles
    /// define the level a member is within the group. The one sanctioned bridge — a duty's
    /// optional minimum title, item 158 — reads <see cref="SortOrder"/> for eligibility only.
    /// Per-organization and fully editable, like <see cref="OrgCalendarEventType"/>.
    /// </remarks>
    public partial class OrganizationMemberLevel : IAuditableEntity
    {
        public Guid Id { get; set; }
        public Guid OrganizationId { get; set; }
        public string Name { get; set; } = null!;

        /// <summary>Position in the ladder; higher means more senior. Eligibility comparisons
        /// (item 158) use this, never the name.</summary>
        public int SortOrder { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual Organization Organization { get; set; } = null!;
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
    }
}
