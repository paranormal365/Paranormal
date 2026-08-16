using Ben.Data.Common.Enums;
using Ben.Service.Models.Admin;
using Ben.Service.Models.Support;
using Ben.Service.Models.Entities;
using Ben.Service.Models.People;
using Ben.Web.Library.Services;
using Microsoft.Extensions.Options;

namespace Ben.Web.WebApp.Services.WebApi;

/// <summary>
/// The Membership half of the adapter — implements <see cref="Ben.Web.Library.Services.IBenMembershipClient"/>.
/// </summary>
/// <remarks>
/// One partial class split across files by domain, matching the slices of IBenAdminClient.
/// The constructor and shared fields live in BenAdminClientAdapter.cs.
/// </remarks>
public sealed partial class BenAdminClientAdapter
{
    // ── Membership Requests ───────────────────────────────────────────────────

    public async Task<IReadOnlyList<OrganizationMembershipRequestRecord>> GetMembershipRequestsAsync(Guid orgId, CancellationToken token = default)
    {
        var result = await _api.GetAsync<IReadOnlyList<OrganizationMembershipRequestRecord>>($"/api/organizations/{orgId}/membership-requests", token);
        return result ?? [];
    }

    public Task<OrganizationMembershipRequestRecord?> GetMyMembershipRequestAsync(Guid orgId, CancellationToken token = default)
        => _api.GetAsync<OrganizationMembershipRequestRecord>($"/api/organizations/{orgId}/membership-requests/my", token);

    public Task<OrganizationMembershipRequestRecord?> ApplyForMembershipAsync(Guid orgId, string? message, CancellationToken token = default)
        => _api.PostAsync<object, OrganizationMembershipRequestRecord>(
               $"/api/organizations/{orgId}/membership-requests", new { Message = message }, token);

    public Task<OrganizationMembershipRequestRecord?> RespondToMembershipRequestAsync(
        Guid orgId, Guid requestId, OrganizationMembershipRequestStatus status, string? responseNote,
        bool? canReapply = null, string? denialReason = null, CancellationToken token = default)
        => _api.PutAsync<object, OrganizationMembershipRequestRecord>(
               $"/api/organizations/{orgId}/membership-requests/{requestId}/respond",
               new { Status = status, ResponseNote = responseNote, CanReapply = canReapply, DenialReason = denialReason }, token);

    public Task<bool> WithdrawMembershipRequestAsync(Guid orgId, Guid requestId, CancellationToken token = default)
        => _api.DeleteAsync($"/api/organizations/{orgId}/membership-requests/{requestId}", token);

    // ── Membership Questions (Phase 3) ────────────────────────────────────────

    public async Task<IReadOnlyList<OrganizationMembershipQuestionRecord>> GetMembershipQuestionsAsync(Guid orgId, CancellationToken token = default)
    {
        var result = await _api.GetAsync<IReadOnlyList<OrganizationMembershipQuestionRecord>>($"/api/organizations/{orgId}/membership-questions", token);
        return result ?? [];
    }

    public Task<OrganizationMembershipQuestionRecord?> CreateMembershipQuestionAsync(Guid orgId, UpsertMembershipQuestionRequest request, CancellationToken token = default)
        => _api.PostAsync<UpsertMembershipQuestionRequest, OrganizationMembershipQuestionRecord>($"/api/organizations/{orgId}/membership-questions", request, token);

    public Task<OrganizationMembershipQuestionRecord?> UpdateMembershipQuestionAsync(Guid orgId, Guid id, UpsertMembershipQuestionRequest request, CancellationToken token = default)
        => _api.PutAsync<UpsertMembershipQuestionRequest, OrganizationMembershipQuestionRecord>($"/api/organizations/{orgId}/membership-questions/{id}", request, token);

    public Task<bool> DeleteMembershipQuestionAsync(Guid orgId, Guid id, CancellationToken token = default)
        => _api.DeleteAsync($"/api/organizations/{orgId}/membership-questions/{id}", token);

    // ── Membership Voting (Phase 3) ───────────────────────────────────────────

    public Task<OrganizationMembershipRequestRecord?> OpenMembershipVoteAsync(Guid orgId, Guid requestId, DateTime voteDeadline, CancellationToken token = default)
        => _api.PostAsync<object, OrganizationMembershipRequestRecord>(
               $"/api/organizations/{orgId}/membership-requests/{requestId}/open-vote",
               new { VoteDeadline = voteDeadline }, token);

    public Task<MembershipReviewVoteRecord?> CastMembershipVoteAsync(Guid orgId, Guid requestId, Ben.Data.Common.Enums.MembershipVoteType voteType, string? comment, CancellationToken token = default)
        => _api.PostAsync<object, MembershipReviewVoteRecord>(
               $"/api/organizations/{orgId}/membership-requests/{requestId}/vote",
               new { VoteType = voteType, Comment = comment }, token);

    public async Task<IReadOnlyList<MembershipReviewVoteRecord>> GetMembershipVotesAsync(Guid orgId, Guid requestId, CancellationToken token = default)
    {
        var result = await _api.GetAsync<IReadOnlyList<MembershipReviewVoteRecord>>($"/api/organizations/{orgId}/membership-requests/{requestId}/votes", token);
        return result ?? [];
    }
}
