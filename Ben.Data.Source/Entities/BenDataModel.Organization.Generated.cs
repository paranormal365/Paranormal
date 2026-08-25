using System;
using System.Collections.Generic;

namespace Ben.Data.Source.Entities
{
    public partial class Organization
    {
        public string Name { get; set; } = null!;
        public string UrlName { get; set; } = null!;

        /// <summary>When true, registered users may submit membership applications to join this organization.</summary>
        /// <summary>
        /// What this organization primarily is (ghost walking tours, 2026-08-24). Chosen when
        /// the group is created, where it decides the DEFAULTS a new group starts with; after
        /// that it is a label for discovery, never a gate on any feature.
        /// </summary>
        public Ben.Data.Common.Enums.OrganizationKind Kind { get; set; }

        /// <summary>
        /// This group runs public walking tours, whatever kind it primarily is.
        /// </summary>
        /// <remarks>
        /// True by default for a <see cref="Ben.Data.Common.Enums.OrganizationKind.GhostWalkingTour"/>,
        /// and separately settable by an investigation group that also runs tours — plenty do,
        /// and none of them should have to register a second group to be found for it. The
        /// finder's "walking tours" filter matches on THIS, not on Kind, so a group that does
        /// both appears in both places while its badge still says what it mainly is.
        /// </remarks>
        public bool RunsPublicTours { get; set; }

        public bool IsAcceptingApplications { get; set; }

        /// <summary>When true, the public can submit investigation requests to this organization.</summary>
        public bool IsAcceptingClients { get; set; }

        /// <summary>When true, the org will also consider client requests outside their configured operating area.</summary>
        public bool AcceptsClientsOutsideRange { get; set; }

        /// <summary>
        /// The organization's half of the two-key rule for member private photos. Lets clients of
        /// this org see the private photo of a member who has *also* opted in
        /// (<see cref="AppUser.SharePrivatePhotoWithClients"/>). Either key alone shows nothing.
        /// </summary>
        public bool AllowMemberPrivatePhotosToClients { get; set; }

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
