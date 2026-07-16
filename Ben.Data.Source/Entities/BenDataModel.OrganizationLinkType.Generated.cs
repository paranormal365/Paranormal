using System;
using System.Collections.Generic;

namespace Ben.Data.Source.Entities
{
    public partial class OrganizationLinkType
    {
        public string Name { get; set; } = null!;
        public string? Description { get; set; }
        public string? IconClass { get; set; }
        public string? ColorClass { get; set; }
        public bool IsActive { get; set; }
        public bool IsPublic { get; set; }
        public int SortOrder { get; set; }
        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
        public virtual ICollection<OrganizationLink> OrganizationLinks { get; set; } = new List<OrganizationLink>();
    }
}
