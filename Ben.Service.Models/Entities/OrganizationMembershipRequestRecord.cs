using Ben.Data.Common.Enums;

namespace Ben.Service.Models.Entities;

public record OrganizationMembershipRequestRecord
{
    public Guid Id { get; init; }
    public Guid OrganizationId { get; init; }
    public required string OrganizationName { get; init; }
    public Guid AppUserId { get; init; }
    public required string ApplicantDisplayName { get; init; }
    public required string ApplicantEmail { get; init; }
    public string? RequestMessage { get; init; }
    public OrganizationMembershipRequestStatus Status { get; init; }
    /// <summary>Display name of the member who accepted or denied the request. Null while Pending.</summary>
    public string? RespondedByDisplayName { get; init; }
    public DateTime DateCreated { get; init; }
    /// <summary>When the request was accepted, denied, or withdrawn.</summary>
    public DateTime? DateResponded { get; init; }

    // ── Phase 3 fields ────────────────────────────────────────────────────────
    public bool IsUnderReview { get; init; }
    public DateTime? VoteDeadline { get; init; }
    public bool? CanReapply { get; init; }
    public string? DenialReason { get; init; }
}
