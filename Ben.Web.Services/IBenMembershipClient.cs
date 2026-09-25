using Ben.Web.Services.WebApi;
using Ben.Service.Models.Admin;
using Ben.Service.Models.Support;
using Ben.Service.Models.Entities;
using Ben.Service.Models.People;
using Ben.Data.Common.Enums;

namespace Ben.Web.Services;

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
    Task<LoadResult<OrganizationMembershipRequestRecord>> GetMembershipRequestsAsync(Guid orgId, CancellationToken token = default);

    /// <summary>Returns the current user's membership request for the organization, or null if none exists.</summary>
    Task<OrganizationMembershipRequestRecord?> GetMyMembershipRequestAsync(Guid orgId, CancellationToken token = default);

    /// <summary>
    /// Every application this person has made, anywhere.
    /// </summary>
    /// <remarks>
    /// IH-04: the per-organization version above only answers for somebody who already knows to
    /// look at that group — which an applicant is not a member of. Without this, applying
    /// produced no acknowledgement anywhere in the applicant's own account.
    /// </remarks>
    Task<LoadResult<OrganizationMembershipRequestRecord>> GetMyMembershipRequestsAsync(CancellationToken token = default);

    /// <summary>Submits a membership application to the organization.</summary>
    Task<(OrganizationMembershipRequestRecord? Result, string? Error)> ApplyForMembershipAsync(Guid orgId, string? message, CancellationToken token = default);

    /// <summary>Accepts or denies a pending membership application (requires MembershipRequests-Update permission).</summary>
    /// <summary>
    /// Accepts or denies an application, and hands back the server's own sentence when it refuses.
    /// </summary>
    /// <remarks>
    /// Returned as a reason rather than a null because the most likely refusal is the 402 that
    /// explains the price of a second member. Discarding it (until the 2026-09-17 audit) left the
    /// page saying "Please try again" about the one thing trying again can never fix.
    /// </remarks>
    Task<(OrganizationMembershipRequestRecord? Result, string? Error)> RespondToMembershipRequestAsync(Guid orgId, Guid requestId, OrganizationMembershipRequestStatus status, string? responseNote, bool? canReapply = null, string? denialReason = null, CancellationToken token = default);

    /// <summary>Withdraws the applicant's own pending request.</summary>
    Task<bool> WithdrawMembershipRequestAsync(Guid orgId, Guid requestId, CancellationToken token = default);

    // ── The group's invitation link ───────────────────────────────────────────

    /// <summary>The group's live invitation link, if it has one, and what the plan will say.</summary>
    Task<OrganizationJoinLinkRecord?> GetJoinLinkAsync(Guid orgId, CancellationToken token = default);

    /// <summary>Makes a link, replacing any that came before it.</summary>
    Task<OrganizationJoinLinkRecord?> IssueJoinLinkAsync(Guid orgId, CancellationToken token = default);

    /// <summary>Stops the link working, for everybody who has it.</summary>
    Task<bool> RevokeJoinLinkAsync(Guid orgId, CancellationToken token = default);

    /// <summary>Whose group an invitation link belongs to. Anonymous; changes nothing.</summary>
    Task<OrganizationJoinInviteRecord?> GetJoinInviteAsync(string inviteToken, CancellationToken token = default);

    /// <summary>
    /// Joins the group the link belongs to, or the server's own sentence when it refuses.
    /// </summary>
    /// <remarks>
    /// The reason is carried rather than dropped because the likeliest refusal is the 402 that
    /// explains the price of a second member — the one thing trying again can never fix.
    /// </remarks>
    Task<(OrganizationJoinInviteRecord? Result, string? Error)> AcceptJoinInviteAsync(string inviteToken, CancellationToken token = default);

    // ── Membership Questions (Phase 3) ────────────────────────────────────────
    Task<LoadResult<OrganizationMembershipQuestionRecord>> GetMembershipQuestionsAsync(Guid orgId, CancellationToken token = default);
    Task<OrganizationMembershipQuestionRecord?> CreateMembershipQuestionAsync(Guid orgId, UpsertMembershipQuestionRequest request, CancellationToken token = default);
    Task<OrganizationMembershipQuestionRecord?> UpdateMembershipQuestionAsync(Guid orgId, Guid id, UpsertMembershipQuestionRequest request, CancellationToken token = default);
    Task<bool> DeleteMembershipQuestionAsync(Guid orgId, Guid id, CancellationToken token = default);

    // ── Membership Voting (Phase 3) ───────────────────────────────────────────
    Task<OrganizationMembershipRequestRecord?> OpenMembershipVoteAsync(Guid orgId, Guid requestId, DateTime voteDeadline, CancellationToken token = default);
    Task<MembershipReviewVoteRecord?> CastMembershipVoteAsync(Guid orgId, Guid requestId, Ben.Data.Common.Enums.MembershipVoteType voteType, string? comment, CancellationToken token = default);
    Task<LoadResult<MembershipReviewVoteRecord>> GetMembershipVotesAsync(Guid orgId, Guid requestId, CancellationToken token = default);
}
