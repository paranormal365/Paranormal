using System;
using System.Collections.Generic;

namespace Ben.Data.Source.Entities
{
    public partial class Organization
    {
        public string Name { get; set; } = null!;
        public string UrlName { get; set; } = null!;

        /// <summary>When true, registered users may submit membership applications to join this organization.</summary>
        public bool IsAcceptingApplications { get; set; }

        /// <summary>When true, the public can submit investigation requests to this organization.</summary>
        public bool IsAcceptingClients { get; set; }

        /// <summary>When true, the org will also consider client requests outside their configured operating area.</summary>
        public bool AcceptsClientsOutsideRange { get; set; }

        /// <summary>Public phone number shown on the org's public page.</summary>
        public string? PublicPhone { get; set; }

        /// <summary>Public email address shown on the org's public page.</summary>
        public string? PublicEmail { get; set; }

        /// <summary>Public website URL shown on the org's public page.</summary>
        public string? PublicWebsite { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
        public virtual OrganizationAreaOfOperation? AreaOfOperation { get; set; }
        public virtual ICollection<OrganizationAddress> OrganizationAddresses { get; set; } = new List<OrganizationAddress>();
        public virtual ICollection<OrganizationEmail> OrganizationEmails { get; set; } = new List<OrganizationEmail>();
        public virtual ICollection<OrganizationPhone> OrganizationPhones { get; set; } = new List<OrganizationPhone>();
        public virtual ICollection<OrganizationLink> OrganizationLinks { get; set; } = new List<OrganizationLink>();
        public virtual ICollection<OrganizationNote> OrganizationNotes { get; set; } = new List<OrganizationNote>();
        public virtual ICollection<OrganizationPage> OrganizationPages { get; set; } = new List<OrganizationPage>();
        public virtual ICollection<OrganizationLogo> OrganizationLogos { get; set; } = new List<OrganizationLogo>();
        public virtual ICollection<OrgMemberGroup> MemberGroups { get; set; } = new List<OrgMemberGroup>();
        public virtual ICollection<OrganizationMembershipRequest> MembershipRequests { get; set; } = new List<OrganizationMembershipRequest>();
        public virtual ICollection<OrganizationFile> OrganizationFiles { get; set; } = new List<OrganizationFile>();
    }
}
