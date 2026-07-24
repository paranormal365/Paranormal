using Ben.Data.Common.Enums;

namespace Ben.Service.Models.Entities;

public record MembershipReviewVoteRecord
{
    public Guid Id { get; init; }
    public Guid OrganizationMembershipRequestId { get; init; }
    public Guid VoterAppUserId { get; init; }
    public string? VoterDisplayName { get; init; }
    public MembershipVoteType VoteType { get; init; }
    public string? Comment { get; init; }
    public DateTime DateVoted { get; init; }
}
