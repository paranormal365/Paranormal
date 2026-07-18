namespace Ben.Data.Source.Entities
{
    public partial class OrganizationLogo
    {
        public Guid OrganizationId { get; set; }
        public Guid UploadFileId { get; set; }

        /// <summary>Alt text for the logo image used in HTML rendering.</summary>
        public string? AltText { get; set; }

        /// <summary>When true this is the org's active logo; only one should be active at a time.</summary>
        public bool IsActive { get; set; }

        public int SortOrder { get; set; }
        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual Organization Organization { get; set; } = null!;
        public virtual UploadFile UploadFile { get; set; } = null!;
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
    }
}
