using Ben.Data.Common.Enums;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// A vote cast on a piece of evidence (UploadFile).
    /// Admins and case managers see full voter identity; the public sees counts only.
    /// </summary>
    public class EvidenceVote
    {
        public Guid Id { get; set; }
        public Guid UploadFileId { get; set; }
        public Guid VoterAppUserId { get; set; }

        /// <summary>The voter's organization — null for public (non-member) voters.</summary>
        public Guid? VoterOrganizationId { get; set; }

        public EvidenceVoteType VoteType { get; set; }
        public string? Comment { get; set; }

        /// <summary>True when cast by a registered user who is not a member of any organization.</summary>
        public bool IsPublicVoter { get; set; }

        /// <summary>True when the voter is the person who originally uploaded the file.</summary>
        public bool IsOriginalUploader { get; set; }

        /// <summary>The case where this file appears as evidence (null if not linked to a case).</summary>
        public Guid? CaseId { get; set; }

        /// <summary>True when the voter is an active member of the org that owns the case.</summary>
        public bool IsVoterCaseOrgMember { get; set; }

        /// <summary>True when the voter is the originating client of the case.</summary>
        public bool IsVoterCaseClient { get; set; }

        /// <summary>Org display name captured at vote time (so renames don't alter historical records).</summary>
        public string? VoterOrganizationName { get; set; }

        public DateTime DateVoted { get; set; }

        public virtual UploadFile UploadFile { get; set; } = null!;
        public virtual AppUser VoterAppUser { get; set; } = null!;
        public virtual Organization? VoterOrganization { get; set; }
        public virtual Case? Case { get; set; }
    }
}
