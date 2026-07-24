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

        public DateTime DateVoted { get; set; }

        public virtual UploadFile UploadFile { get; set; } = null!;
        public virtual AppUser VoterAppUser { get; set; } = null!;
        public virtual Organization? VoterOrganization { get; set; }
    }
}
