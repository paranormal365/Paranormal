using System;
using System.Collections.Generic;

namespace Ben.Data.Source.Entities
{
    public partial class UserLink
    {
        public Guid UserLinkTypeId { get; set; }
        public Guid AppUserId { get; set; }
        public string? DisplayText { get; set; }
        public string LinkUrl { get; set; } = null!;
        public bool IsActive { get; set; }
        public bool IsPublic { get; set; }
        public bool IsVerifiedApproved { get; set; }
        public Guid? VerifiedApprovedByAppUserId { get; set; }
        public DateTime? DateVerifiedApproved { get; set; }
        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual UserLinkType UserLinkType { get; set; } = null!;
        public virtual AppUser AppUser { get; set; } = null!;
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
    }
}
