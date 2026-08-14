namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// A profile photo belonging to a user. Modelled on <see cref="OrganizationLogo"/> — a thin
    /// join onto <see cref="UploadFile"/> rather than a byte column, so photos inherit the
    /// existing storage, share, and audience-access machinery.
    /// </summary>
    /// <remarks>
    /// A user has two independent slots, distinguished by <see cref="IsPublic"/>: a public photo
    /// anyone may see, and a private one shown only to people who have earned it (shared org
    /// membership, or a case relationship where both the org policy and the user's own opt-in
    /// allow it). Keeping them as two rows of one table rather than two columns on AppUser means
    /// a user can retain history — deactivated photos stay for reuse instead of being overwritten.
    /// </remarks>
    public partial class AppUserPhoto
    {
        public Guid AppUserId { get; set; }
        public Guid UploadFileId { get; set; }

        /// <summary>Alt text for the image, for screen readers and broken-image fallback.</summary>
        public string? AltText { get; set; }

        /// <summary>
        /// Which slot this photo occupies: true = the public photo, false = the private one.
        /// Not a permission by itself — <c>IsPublic</c> on the underlying <see cref="UploadFile"/>
        /// is what actually governs direct file access.
        /// </summary>
        public bool IsPublic { get; set; }

        /// <summary>
        /// Whether this is the photo currently in use for its slot. At most one active row per
        /// (user, slot); the others are prior photos kept for reuse.
        /// </summary>
        public bool IsActive { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual AppUser AppUser { get; set; } = null!;
        public virtual UploadFile UploadFile { get; set; } = null!;
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
    }
}
