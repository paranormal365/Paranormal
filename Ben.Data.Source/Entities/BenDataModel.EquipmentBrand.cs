using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// A gear manufacturer/brand name in the public equipment catalog. Accumulates from user
    /// entries (mirrors <see cref="ExperienceCategory"/>'s moderation shape) rather than being
    /// seeded — the useful brand list is far larger than the category list and grows with the
    /// user base. New entries require SuperAdmin approval before appearing in the public,
    /// anonymous catalog browse; the proposer can keep using their own unapproved entry
    /// immediately.
    /// </summary>
    public class EquipmentBrand : IAuditableEntity
    {
        public Guid Id { get; set; }

        public string Name { get; set; } = null!;

        /// <summary>
        /// The readable part of this make's address, as in <c>/equipment/zoom</c>.
        /// </summary>
        /// <remarks>
        /// Derived from the name rather than typed, and <b>regenerated when the name changes</b> —
        /// unlike a case or an organization, whose slug is frozen because somebody chose and shared
        /// it. This catalog is the site's own vocabulary, its rename path exists specifically to
        /// correct mistakes, and a page for a corrected make that still answered to the typo would
        /// preserve the error in the one place everybody sees.
        /// </remarks>
        public string? UrlName { get; set; }

        /// <summary>
        /// True when approved by a SuperAdmin for platform-wide, anonymous visibility.
        /// SuperAdmin-created entries are approved on creation. User- or org-proposed
        /// entries start as false, but remain usable by their own proposer meanwhile.
        /// </summary>
        public bool IsApproved { get; set; }

        /// <summary>Org that proposed this entry; null when proposed by an individual user or created by SuperAdmin.</summary>
        public Guid? ProposedByOrganizationId { get; set; }

        /// <summary>
        /// User that proposed this entry; null when created by SuperAdmin. Widened vs.
        /// <see cref="ExperienceCategory"/>'s org-only proposer field — personal equipment owners
        /// (who may belong to no organization at all) contribute to this catalog too.
        /// </summary>
        public Guid? ProposedByAppUserId { get; set; }

        public Guid? ApprovedByAppUserId { get; set; }
        public DateTime? DateApproved { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual Organization? ProposedByOrganization { get; set; }
        public virtual AppUser? ProposedByAppUser { get; set; }
        public virtual AppUser? ApprovedByAppUser { get; set; }
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
        public virtual ICollection<EquipmentModel> EquipmentModels { get; set; } = new List<EquipmentModel>();
    }
}
