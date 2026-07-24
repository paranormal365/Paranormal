namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// Specific experience sub-type under a category used across client requests,
    /// case timelines, and investigation evidence (e.g. Audible → Knocking).
    /// Global to the platform — not org-scoped. New entries require SuperAdmin approval.
    /// </summary>
    public partial class ExperienceType
    {
        public Guid ExperienceCategoryId { get; set; }
        public string Name { get; set; } = null!;
        public string? Description { get; set; }
        public string? IconClass { get; set; }
        public int SortOrder { get; set; }
        public bool IsActive { get; set; }

        /// <summary>
        /// True when approved by a SuperAdmin for platform-wide use.
        /// SuperAdmin-created entries are approved on creation.
        /// Org-proposed entries start as false.
        /// </summary>
        public bool IsApproved { get; set; }

        /// <summary>Org that proposed this entry; null when created by SuperAdmin directly.</summary>
        public Guid? ProposedByOrganizationId { get; set; }

        public Guid? ApprovedByAppUserId { get; set; }
        public DateTime? DateApproved { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual ExperienceCategory ExperienceCategory { get; set; } = null!;
        public virtual Organization? ProposedByOrganization { get; set; }
        public virtual AppUser? ApprovedByAppUser { get; set; }
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
    }
}
