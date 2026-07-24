using Ben.Data.Common.Enums;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// A single reviewer's vote on a membership application that is under committee review.
    /// </summary>
    public class MembershipReviewVote
    {
        public Guid Id { get; set; }
        public Guid OrganizationMembershipRequestId { get; set; }
        public Guid VoterAppUserId { get; set; }
        public MembershipVoteType VoteType { get; set; }
        public string? Comment { get; set; }
        public DateTime DateVoted { get; set; }

        public virtual OrganizationMembershipRequest MembershipRequest { get; set; } = null!;
        public virtual AppUser VoterAppUser { get; set; } = null!;
    }
}
