using Ben.Data.Common.Enums;
using Ben.Service.Models.Entities;
using Ben.Web.Services;
using Ben.Web.Services.WebApi;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// Tests for Phase 3 membership adapter methods:
/// questions CRUD, committee voting, and enhanced respond.
/// </summary>
public class MembershipPhase3AdapterTests
{
    private static Mock<IWebApiClient> ApiMock() => new();
    private static Mock<IWebApiAuthService> AuthMock() => new();

    private static BenAdminClientAdapter Build(Mock<IWebApiClient> api)
        => new BenAdminClientAdapter(api.Object, AuthMock().Object,
            Microsoft.Extensions.Options.Options.Create(new WebApiOptions()));

    private static OrganizationMembershipQuestionRecord MakeQuestion(Guid orgId) =>
        new() { Id = Guid.NewGuid(), OrganizationId = orgId,
                QuestionText = "Why join?", IsRequired = true, SortOrder = 1, IsActive = true };

    // ── GetMembershipQuestionsAsync ───────────────────────────────────────────

    [Fact]
    public async Task GetMembershipQuestionsAsync_GetsFromCorrectUrl()
    {
        var orgId = Guid.NewGuid();
        var api   = ApiMock();
        api.Setup(x => x.GetListAsync<OrganizationMembershipQuestionRecord>(
                $"/api/organizations/{orgId}/membership-questions", It.IsAny<CancellationToken>()))
           .ReturnsAsync(LoadResult<OrganizationMembershipQuestionRecord>.Ok([MakeQuestion(orgId)]));

        var result = await Build(api).GetMembershipQuestionsAsync(orgId);

        Assert.Single(result.Items);
        api.Verify(x => x.GetListAsync<OrganizationMembershipQuestionRecord>(
            $"/api/organizations/{orgId}/membership-questions", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetMembershipQuestionsAsync_WhenApiReturnsNull_ReturnsEmpty()
    {
        var api = ApiMock();
        api.Setup(x => x.GetListAsync<OrganizationMembershipQuestionRecord>(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
           .ReturnsAsync(LoadResult<OrganizationMembershipQuestionRecord>.Failure("The server answered 403 (Forbidden)."));

        var result = await Build(api).GetMembershipQuestionsAsync(Guid.NewGuid());

        Assert.Empty(result.Items);
    }

    // ── CreateMembershipQuestionAsync ─────────────────────────────────────────

    [Fact]
    public async Task CreateMembershipQuestionAsync_PostsToCorrectUrl()
    {
        var orgId = Guid.NewGuid();
        var api   = ApiMock();
        api.Setup(x => x.PostAsync<UpsertMembershipQuestionRequest, OrganizationMembershipQuestionRecord>(
                $"/api/organizations/{orgId}/membership-questions",
                It.IsAny<UpsertMembershipQuestionRequest>(),
                It.IsAny<CancellationToken>()))
           .ReturnsAsync(MakeQuestion(orgId));

        var req = new UpsertMembershipQuestionRequest("Why join?", true, 1, true);
        await Build(api).CreateMembershipQuestionAsync(orgId, req);

        api.Verify(x => x.PostAsync<UpsertMembershipQuestionRequest, OrganizationMembershipQuestionRecord>(
            $"/api/organizations/{orgId}/membership-questions", req, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── UpdateMembershipQuestionAsync ─────────────────────────────────────────

    [Fact]
    public async Task UpdateMembershipQuestionAsync_PutsToCorrectUrl()
    {
        var orgId = Guid.NewGuid();
        var id    = Guid.NewGuid();
        var api   = ApiMock();
        api.Setup(x => x.PutAsync<UpsertMembershipQuestionRequest, OrganizationMembershipQuestionRecord>(
                $"/api/organizations/{orgId}/membership-questions/{id}",
                It.IsAny<UpsertMembershipQuestionRequest>(),
                It.IsAny<CancellationToken>()))
           .ReturnsAsync(MakeQuestion(orgId));

        var req = new UpsertMembershipQuestionRequest("Updated?", false, 2, true);
        await Build(api).UpdateMembershipQuestionAsync(orgId, id, req);

        api.Verify(x => x.PutAsync<UpsertMembershipQuestionRequest, OrganizationMembershipQuestionRecord>(
            $"/api/organizations/{orgId}/membership-questions/{id}", req, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── DeleteMembershipQuestionAsync ─────────────────────────────────────────

    [Fact]
    public async Task DeleteMembershipQuestionAsync_DeletesCorrectUrl()
    {
        var orgId = Guid.NewGuid();
        var id    = Guid.NewGuid();
        var api   = ApiMock();
        api.Setup(x => x.DeleteAsync(
                $"/api/organizations/{orgId}/membership-questions/{id}",
                It.IsAny<CancellationToken>()))
           .ReturnsAsync(true);

        await Build(api).DeleteMembershipQuestionAsync(orgId, id);

        api.Verify(x => x.DeleteAsync(
            $"/api/organizations/{orgId}/membership-questions/{id}",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── OpenMembershipVoteAsync ───────────────────────────────────────────────

    [Fact]
    public async Task OpenMembershipVoteAsync_PostsToOpenVoteUrl()
    {
        var orgId     = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        var deadline  = DateTime.UtcNow.AddDays(7);
        var api       = ApiMock();
        api.Setup(x => x.PostAsync<object, OrganizationMembershipRequestRecord>(
                $"/api/organizations/{orgId}/membership-requests/{requestId}/open-vote",
                It.IsAny<object>(),
                It.IsAny<CancellationToken>()))
           .ReturnsAsync(new OrganizationMembershipRequestRecord
           {
               OrganizationName = "Org", ApplicantDisplayName = "Alice",
               ApplicantEmail = "a@b.com", Status = OrganizationMembershipRequestStatus.Pending,
               IsUnderReview = true, VoteDeadline = deadline,
           });

        var result = await Build(api).OpenMembershipVoteAsync(orgId, requestId, deadline);

        Assert.True(result!.IsUnderReview);
        Assert.NotNull(result.VoteDeadline);
        api.Verify(x => x.PostAsync<object, OrganizationMembershipRequestRecord>(
            $"/api/organizations/{orgId}/membership-requests/{requestId}/open-vote",
            It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── CastMembershipVoteAsync ───────────────────────────────────────────────

    [Fact]
    public async Task CastMembershipVoteAsync_PostsToVoteUrl()
    {
        var orgId     = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        var api       = ApiMock();
        api.Setup(x => x.PostAsync<object, MembershipReviewVoteRecord>(
                $"/api/organizations/{orgId}/membership-requests/{requestId}/vote",
                It.IsAny<object>(),
                It.IsAny<CancellationToken>()))
           .ReturnsAsync(new MembershipReviewVoteRecord
           {
               Id = Guid.NewGuid(), OrganizationMembershipRequestId = requestId,
               VoterAppUserId = Guid.NewGuid(), VoteType = MembershipVoteType.Approve,
               DateVoted = DateTime.UtcNow,
           });

        var result = await Build(api).CastMembershipVoteAsync(orgId, requestId, MembershipVoteType.Approve, "Looks great!");

        Assert.Equal(MembershipVoteType.Approve, result!.VoteType);
        api.Verify(x => x.PostAsync<object, MembershipReviewVoteRecord>(
            $"/api/organizations/{orgId}/membership-requests/{requestId}/vote",
            It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── GetMembershipVotesAsync ───────────────────────────────────────────────

    [Fact]
    public async Task GetMembershipVotesAsync_GetsFromCorrectUrl()
    {
        var orgId     = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        var api       = ApiMock();
        api.Setup(x => x.GetListAsync<MembershipReviewVoteRecord>(
                $"/api/organizations/{orgId}/membership-requests/{requestId}/votes",
                It.IsAny<CancellationToken>()))
           .ReturnsAsync(LoadResult<MembershipReviewVoteRecord>.Ok([
               new() { Id = Guid.NewGuid(), OrganizationMembershipRequestId = requestId,
                       VoterAppUserId = Guid.NewGuid(), VoteType = MembershipVoteType.Approve,
                       DateVoted = DateTime.UtcNow }
           ]));

        var result = await Build(api).GetMembershipVotesAsync(orgId, requestId);

        Assert.Single(result.Items);
        api.Verify(x => x.GetListAsync<MembershipReviewVoteRecord>(
            $"/api/organizations/{orgId}/membership-requests/{requestId}/votes",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetMembershipVotesAsync_WhenApiReturnsNull_ReturnsEmpty()
    {
        var api = ApiMock();
        api.Setup(x => x.GetListAsync<MembershipReviewVoteRecord>(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
           .ReturnsAsync(LoadResult<MembershipReviewVoteRecord>.Failure("The server answered 403 (Forbidden)."));

        var result = await Build(api).GetMembershipVotesAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.Empty(result.Items);
    }

    // ── Enhanced RespondToMembershipRequestAsync ──────────────────────────────

    [Fact]
    public async Task RespondToMembershipRequestAsync_IncludesCanReapplyAndDenialReason()
    {
        var orgId     = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        var api       = ApiMock();
        string? capturedJson = null;
        api.Setup(x => x.PutAsync<object, OrganizationMembershipRequestRecord>(
                $"/api/organizations/{orgId}/membership-requests/{requestId}/respond",
                It.IsAny<object>(),
                It.IsAny<CancellationToken>()))
           .Callback<string, object, CancellationToken>((_, body, _) =>
               capturedJson = System.Text.Json.JsonSerializer.Serialize(body))
           .ReturnsAsync(new OrganizationMembershipRequestRecord
           {
               OrganizationName = "Org", ApplicantDisplayName = "Alice",
               ApplicantEmail = "a@b.com",
               Status = OrganizationMembershipRequestStatus.Denied,
               CanReapply = true, DenialReason = "Try again later.",
           });

        var result = await Build(api).RespondToMembershipRequestAsync(
            orgId, requestId, OrganizationMembershipRequestStatus.Denied,
            "Try again later.", canReapply: true, denialReason: "Try again later.");

        Assert.Equal(OrganizationMembershipRequestStatus.Denied, result!.Status);
        Assert.True(result.CanReapply);
        Assert.Contains("Try again", result.DenialReason);
    }
}
