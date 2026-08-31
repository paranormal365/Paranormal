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

        /// <summary>
        /// True when this organization exists only to carry one person's own subscription, and is
        /// not a group anybody joins.
        /// </summary>
        /// <remarks>
        /// <para><b>Why an organization at all for somebody who has no group.</b> Everything a
        /// paid solo tier sells is org-scoped: cases, subscriptions, privacy, private-residence
        /// work. Giving the solo investigator a hidden organization means all of it works with no
        /// second implementation, rather than a parallel account-level version of each that could
        /// drift from the group one.</para>
        ///
        /// <para><b>A flag, not an <see cref="Ben.Data.Common.Enums.OrganizationKind"/>.</b> That
        /// enum's own contract is that a kind is "a starting point and a label, never a gate", and
        /// hiding an organization is exactly a gate. Overloading it would have cost no migration
        /// and quietly contradicted the rule every other reader of Kind relies on.</para>
        ///
        /// <para><b>What it changes is visibility, nothing else.</b> A personal organization is a
        /// real organization in every other respect. It is excluded from the places that present
        /// groups to be found or joined, because a person who bought a solo plan did not create a
        /// group and must not turn up in a directory as one — that would publish the fact that
        /// they subscribed, under their own name. See <c>PersonalOrganizations</c>, which owns the
        /// filter.</para>
        /// </remarks>
        public bool IsPersonal { get; set; }

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
