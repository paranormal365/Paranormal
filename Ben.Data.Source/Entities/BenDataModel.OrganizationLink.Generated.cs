using System;

namespace Ben.Data.Source.Entities
{
    public partial class OrganizationLink
    {
        public Guid OrganizationId { get; set; }
        public Guid OrganizationLinkTypeId { get; set; }
        public string? DisplayText { get; set; }
        public string LinkUrl { get; set; } = null!;
        public bool IsPublic { get; set; }
        public bool IsActive { get; set; }
        public bool IsVerifiedApproved { get; set; }
        public DateTime? DateVerifiedApproved { get; set; }
        public Guid? VerifiedApprovedByAppUserId { get; set; }
        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual Organization Organization { get; set; } = null!;
        public virtual OrganizationLinkType OrganizationLinkType { get; set; } = null!;
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
    }
}
