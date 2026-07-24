using Ben.Data.Common.Enums;

namespace Ben.Service.Models.Entities;

public record InvestigationRecord
{
    public Guid Id { get; init; }
    public Guid CaseId { get; init; }
    public Guid? OrgCalendarEventId { get; init; }
    public required string Title { get; init; }
    public string? Description { get; init; }
    public string? Location { get; init; }
    public DateTime ScheduledDateTime { get; init; }
    public DateTime? EndDateTime { get; init; }
    public InvestigationStatus Status { get; init; }
    public string? Notes { get; init; }
    public int AttendeeCount { get; init; }
    public DateTime DateCreated { get; init; }
    public DateTime? DateUpdated { get; init; }
    public Guid CreatedByAppUserId { get; init; }
    public Guid? UpdatedByAppUserId { get; init; }
}

public record InvestigationAttendeeRecord
{
    public Guid Id { get; init; }
    public Guid InvestigationId { get; init; }
    public Guid AppUserId { get; init; }
    public string? DisplayName { get; init; }
    public string? AssignedRole { get; init; }
    public bool? DidAttend { get; init; }
    public DateTime DateCreated { get; init; }
    public Guid CreatedByAppUserId { get; init; }
}

public record EvidenceVoteRecord
{
    public Guid Id { get; init; }
    public Guid UploadFileId { get; init; }
    public Guid VoterAppUserId { get; init; }
    public string? VoterDisplayName { get; init; }
    public Guid? VoterOrganizationId { get; init; }
    public EvidenceVoteType VoteType { get; init; }
    public string? Comment { get; init; }
    public bool IsPublicVoter { get; init; }
    public DateTime DateVoted { get; init; }
}

/// <summary>
/// Aggregate vote summary returned on public endpoints — no voter identities.
/// </summary>
public record EvidenceVoteSummary(
    Guid UploadFileId,
    int ConfirmsCount,
    int DisputesCount,
    int InconclusiveCount,
    int TotalVotes,
    EvidenceVoteType? CurrentUserVote);
