using Ben.Service.Models.Admin;
using Ben.Service.Models.Support;
using Ben.Service.Models.Entities;
using Ben.Service.Models.People;
using Ben.Data.Common.Enums;

namespace Ben.Web.Library.Services;

/// <summary>
/// The Membership slice of <see cref="IBenAdminClient"/> — joining an organization — requests, questions and votes.
/// </summary>
/// <remarks>
/// Part of splitting one 383-method interface into domain-sized pieces.
/// <see cref="IBenAdminClient"/> inherits every slice, so existing callers and the single
/// adapter are unchanged; new code (and test doubles) can depend on just the slice it needs.
/// </remarks>
public interface IBenMembershipClient
{
    // ── Membership Requests ───────────────────────────────────────────────────

    /// <summary>Returns all membership requests for the organization (requires MembershipRequests-Read permission).</summary>
    Task<IReadOnlyList<OrganizationMembershipRequestRecord>> GetMembershipRequestsAsync(Guid orgId, CancellationToken token = default);

    /// <summary>Returns the current user's membership request for the organization, or null if none exists.</summary>
    Task<OrganizationMembershipRequestRecord?> GetMyMembershipRequestAsync(Guid orgId, CancellationToken token = default);

    /// <summary>Submits a membership application to the organization.</summary>
    Task<OrganizationMembershipRequestRecord?> ApplyForMembershipAsync(Guid orgId, string? message, CancellationToken token = default);

    /// <summary>Accepts or denies a pending membership application (requires MembershipRequests-Update permission).</summary>
    Task<OrganizationMembershipRequestRecord?> RespondToMembershipRequestAsync(Guid orgId, Guid requestId, OrganizationMembershipRequestStatus status, string? responseNote, bool? canReapply = null, string? denialReason = null, CancellationToken token = default);

    /// <summary>Withdraws the applicant's own pending request.</summary>
    Task<bool> WithdrawMembershipRequestAsync(Guid orgId, Guid requestId, CancellationToken token = default);

    // ── Membership Questions (Phase 3) ────────────────────────────────────────
    Task<IReadOnlyList<OrganizationMembershipQuestionRecord>> GetMembershipQuestionsAsync(Guid orgId, CancellationToken token = default);
    Task<OrganizationMembershipQuestionRecord?> CreateMembershipQuestionAsync(Guid orgId, UpsertMembershipQuestionRequest request, CancellationToken token = default);
    Task<OrganizationMembershipQuestionRecord?> UpdateMembershipQuestionAsync(Guid orgId, Guid id, UpsertMembershipQuestionRequest request, CancellationToken token = default);
    Task<bool> DeleteMembershipQuestionAsync(Guid orgId, Guid id, CancellationToken token = default);

    // ── Membership Voting (Phase 3) ───────────────────────────────────────────
    Task<OrganizationMembershipRequestRecord?> OpenMembershipVoteAsync(Guid orgId, Guid requestId, DateTime voteDeadline, CancellationToken token = default);
    Task<MembershipReviewVoteRecord?> CastMembershipVoteAsync(Guid orgId, Guid requestId, Ben.Data.Common.Enums.MembershipVoteType voteType, string? comment, CancellationToken token = default);
    Task<IReadOnlyList<MembershipReviewVoteRecord>> GetMembershipVotesAsync(Guid orgId, Guid requestId, CancellationToken token = default);
}
