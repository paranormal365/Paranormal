using System;

namespace Ben.Data.Source.Entities
{
    public partial class OrganizationEmail
    {
        public Guid OrganizationId { get; set; }
        public Guid OrganizationEmailTypeId { get; set; }
        public string? DisplayText { get; set; }
        public string EmailAddress { get; set; } = null!;
        public bool IsPublic { get; set; }
        public bool IsHidden { get; set; }
        public bool IsPrimary { get; set; }
        public DateTime? DateValidated { get; set; }
        public string? ValidationToken { get; set; }
        public bool IsValidated { get; set; }
        public int SortOrder { get; set; }
        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual Organization Organization { get; set; } = null!;
        public virtual OrganizationEmailType OrganizationEmailType { get; set; } = null!;
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
    }
}
