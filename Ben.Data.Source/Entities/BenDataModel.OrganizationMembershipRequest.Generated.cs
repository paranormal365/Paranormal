using Ben.Data.Common.Enums;
using System;

namespace Ben.Data.Source.Entities
{
    public partial class OrganizationMembershipRequest
    {
        /// <summary>The organization this application is for.</summary>
        public Guid OrganizationId { get; set; }

        /// <summary>The user who submitted the application.</summary>
        public Guid AppUserId { get; set; }

        /// <summary>Optional message from the applicant explaining why they want to join.</summary>
        public string? RequestMessage { get; set; }

        /// <summary>Current lifecycle state of this application.</summary>
        public OrganizationMembershipRequestStatus Status { get; set; } = OrganizationMembershipRequestStatus.Pending;

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }

        /// <summary>Audit: who submitted the request (mirrors AppUserId).</summary>
        public Guid CreatedByAppUserId { get; set; }

        /// <summary>Audit: who accepted or denied the request.</summary>
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual Organization Organization { get; set; } = null!;

        /// <summary>The applicant.</summary>
        public virtual AppUser Applicant { get; set; } = null!;

        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
    }
}
