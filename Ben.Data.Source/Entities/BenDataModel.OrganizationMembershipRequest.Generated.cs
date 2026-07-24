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

        // ── Phase 3: committee review ─────────────────────────────────────────

        /// <summary>When true, the application has been escalated to a committee vote.</summary>
        public bool IsUnderReview { get; set; }

        /// <summary>Deadline for committee members to cast their votes. Null = not in review.</summary>
        public DateTime? VoteDeadline { get; set; }

        /// <summary>
        /// When denied: whether the applicant is allowed to reapply in the future.
        /// Null = not yet responded.
        /// </summary>
        public bool? CanReapply { get; set; }

        /// <summary>Optional reason provided when denying the application.</summary>
        public string? DenialReason { get; set; }

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
        public virtual ICollection<OrganizationMembershipAnswer> Answers { get; set; } = new List<OrganizationMembershipAnswer>();
        public virtual ICollection<MembershipReviewVote> ReviewVotes { get; set; } = new List<MembershipReviewVote>();
    }
}
