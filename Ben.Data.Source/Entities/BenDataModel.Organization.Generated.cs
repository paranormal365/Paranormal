using System;
using System.Collections.Generic;

namespace Ben.Data.Source.Entities
{
    public partial class Organization
    {
        public string Name { get; set; } = null!;
        public string UrlName { get; set; } = null!;
        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
        public virtual ICollection<OrganizationAddress> OrganizationAddresses { get; set; } = new List<OrganizationAddress>();
        public virtual ICollection<OrganizationEmail> OrganizationEmails { get; set; } = new List<OrganizationEmail>();
        public virtual ICollection<OrganizationPhone> OrganizationPhones { get; set; } = new List<OrganizationPhone>();
        public virtual ICollection<OrganizationLink> OrganizationLinks { get; set; } = new List<OrganizationLink>();
        public virtual ICollection<OrganizationNote> OrganizationNotes { get; set; } = new List<OrganizationNote>();
        public virtual ICollection<OrganizationPage> OrganizationPages { get; set; } = new List<OrganizationPage>();
    }
}
