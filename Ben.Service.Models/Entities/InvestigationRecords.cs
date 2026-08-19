using Ben.Data.Common.Enums;

namespace Ben.Service.Models.Entities;

public record InvestigationRecord
{
    public Guid Id { get; init; }

    /// <summary>Null for a visit with no client case.</summary>
    public Guid? CaseId { get; init; }

    /// <summary>The group that ran it. Always set, including when there is no case.</summary>
    public Guid OrganizationId { get; init; }

    /// <summary>The shared location this visit happened at, once one is known.</summary>
    public Guid? PlaceId { get; init; }

    public decimal? Latitude { get; init; }
    public decimal? Longitude { get; init; }

    /// <summary>Why there are no coordinates, or null when there are. See <c>PlaceGeocoder</c>.</summary>
    public string? GeocodeNote { get; init; }

    /// <summary>How widely the findings may be shared. See <c>InvestigationVisibility</c>.</summary>
    public InvestigationVisibility Visibility { get; init; } = InvestigationVisibility.GroupOnly;

    public Guid? OrgCalendarEventId { get; init; }
    public required string Title { get; init; }
    public string? Description { get; init; }
    public string? Location { get; init; }
    public DateTime ScheduledDateTime { get; init; }
    public DateTime? EndDateTime { get; init; }
    public InvestigationStatus Status { get; init; }
    public string? Notes { get; init; }
    public int AttendeeCount { get; init; }
    public DateTime? EvidenceDueDate { get; init; }
    public DateTime DateCreated { get; init; }
    public DateTime? DateUpdated { get; init; }
    public Guid CreatedByAppUserId { get; init; }
    public Guid? UpdatedByAppUserId { get; init; }

    /// <summary>
    /// Whether the caller may change this investigation. The server's verdict, not a hint.
    /// </summary>
    /// <remarks>
    /// Editing is narrower than membership — see <c>InvestigationAccess.CanManageAsync</c> for the
    /// five ways to earn it. A screen must render this rather than work it out, or it will
    /// eventually offer a control the endpoint refuses. It is not mapped from the entity; the
    /// controllers set it per caller, and it defaults to <c>false</c> so a path that forgets shows
    /// too little rather than too much.
    /// </remarks>
    public bool CanEditRecord { get; init; }
}

public record InvestigationAttendeeRecord
{
    public Guid Id { get; init; }
    public Guid InvestigationId { get; init; }
    public Guid AppUserId { get; init; }
    public string? DisplayName { get; init; }

    /// <summary>Free text describing the job on the night. Grants nothing.</summary>
    public string? AssignedRole { get; init; }

    /// <summary>
    /// Running this particular visit — delegated authority that expires with it, and distinct from
    /// both <see cref="AssignedRole"/> and standing rank in the group.
    /// </summary>
    public bool IsLead { get; init; }

    public RsvpStatus Rsvp { get; init; } = RsvpStatus.Invited;
    public bool? DidAttend { get; init; }
    public DateTime DateCreated { get; init; }
    public Guid CreatedByAppUserId { get; init; }
}

public record EvidenceVoteRecord
{
    public Guid   Id                    { get; init; }
    public Guid   UploadFileId          { get; init; }
    public Guid   VoterAppUserId        { get; init; }
    public string? VoterDisplayName     { get; init; }
    public Guid?  VoterOrganizationId   { get; init; }
    public string? VoterOrganizationName { get; init; }
    public EvidenceVoteType VoteType    { get; init; }
    public string? Comment              { get; init; }
    public bool   IsPublicVoter         { get; init; }
    public bool   IsOriginalUploader    { get; init; }
    public Guid?  CaseId                { get; init; }
    public string? CaseReference        { get; init; }
    public bool   IsVoterCaseOrgMember  { get; init; }
    public bool   IsVoterCaseClient     { get; init; }
    public DateTime DateVoted           { get; init; }
}

/// <summary>
/// Aggregate vote counts for a single evidence file, returned by
/// <c>EvidenceVoteController.GetSummary</c> and <c>EvidenceVoteController.CastVote</c>.
/// </summary>
/// <remarks>
/// Voter identity is intentionally omitted. <c>CurrentUserVote</c> is non-null only
/// when the requesting user has already voted on this file.
/// Consumed by <c>EvidenceVoteWidget.razor</c> and the Phase 5 adapter tests.
/// </remarks>
/// <param name="UploadFileId">The file that was voted on.</param>
/// <param name="CurrentUserVote">The caller's current vote, or <c>null</c> if they have not voted.</param>
public record EvidenceVoteSummary(
    Guid UploadFileId,
    int ConfirmsCount,
    int DisputesCount,
    int InconclusiveCount,
    int TotalVotes,
    EvidenceVoteType? CurrentUserVote,
    /// <summary>
    /// Signed total: +1 confirms, 0 inconclusive, −1 disputes. Computed server-side by
    /// <see cref="Ben.Data.Common.Enums.EvidenceVoteScore"/> and rendered as given — never
    /// re-derived from the counts, which is how four surfaces end up with four answers.
    /// </summary>
    int Score = 0);

/// <summary>
/// Aggregate community-vote counts for a public <see cref="Ben.Data.Source.Entities.Case"/>,
/// returned by <c>PublicCaseVoteController</c>.
/// </summary>
/// <remarks>
/// Voter identity is never included. <c>CurrentUserVote</c> is populated only for
/// authenticated callers and is used by <c>CaseVoteWidget.razor</c> to highlight
/// the user's active vote button.
/// <br/>
/// This record is defined on both sides of the API boundary:
/// <list type="bullet">
///   <item><description>
///     Server: <c>Ben.Service.Models.Entities.CaseVoteSummary</c> (this file)
///   </description></item>
///   <item><description>
///     Client: same type via the <c>Ben.Service.Models</c> project reference in
///     <c>Ben.Web.Library</c> and <c>Ben.Web.WebApp</c>.
///   </description></item>
/// </list>
/// </remarks>
/// <param name="CaseId">The case that was voted on.</param>
/// <param name="CurrentUserVote">The caller's current vote, or <c>null</c> if anonymous or not yet voted.</param>
public record CaseVoteSummary(
    Guid CaseId,
    int ConfirmsCount,
    int DisputesCount,
    int InconclusiveCount,
    int TotalVotes,
    EvidenceVoteType? CurrentUserVote,
    /// <summary>Signed total: +1 confirms, 0 inconclusive, −1 disputes. See <see cref="EvidenceVoteSummary"/>.</summary>
    int Score = 0);
