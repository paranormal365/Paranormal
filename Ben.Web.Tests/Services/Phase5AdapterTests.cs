using Ben.Data.Common.Enums;
using Ben.Service.Models.Entities;
using Ben.Web.Services;
using Ben.Web.Services.WebApi;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>Tests for Phase 5 investigation and evidence vote adapter methods.</summary>
public class Phase5AdapterTests
{
    private static Mock<IWebApiClient> ApiMock() => new();
    private static Mock<IWebApiAuthService> AuthMock() => new();

    private static BenAdminClientAdapter Build(Mock<IWebApiClient> api)
        => new BenAdminClientAdapter(api.Object, AuthMock().Object,
            Microsoft.Extensions.Options.Options.Create(new WebApiOptions()));

    private static string InvBase(Guid orgId, Guid caseId)
        => $"/api/organizations/{orgId}/cases/{caseId}/investigations";

    // ── GetInvestigationsAsync ────────────────────────────────────────────────

    [Fact]
    public async Task GetInvestigationsAsync_GetsFromCorrectUrl()
    {
        var orgId  = Guid.NewGuid();
        var caseId = Guid.NewGuid();
        var api    = ApiMock();
        api.Setup(x => x.GetAsync<IReadOnlyList<InvestigationRecord>>(
                InvBase(orgId, caseId), It.IsAny<CancellationToken>()))
           .ReturnsAsync([new() { Id = Guid.NewGuid(), CaseId = caseId, Title = "Night Inv", Status = InvestigationStatus.Scheduled, ScheduledDateTime = DateTime.UtcNow }]);

        var result = await Build(api).GetInvestigationsAsync(orgId, caseId);

        Assert.Single(result);
        api.Verify(x => x.GetAsync<IReadOnlyList<InvestigationRecord>>(
            InvBase(orgId, caseId), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetInvestigationsAsync_WhenApiReturnsNull_ReturnsEmpty()
    {
        var api = ApiMock();
        api.Setup(x => x.GetAsync<IReadOnlyList<InvestigationRecord>>(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
           .ReturnsAsync((IReadOnlyList<InvestigationRecord>?)null);

        var result = await Build(api).GetInvestigationsAsync(Guid.NewGuid(), Guid.NewGuid());
        Assert.Empty(result);
    }

    // ── CreateInvestigationAsync ──────────────────────────────────────────────

    [Fact]
    public async Task CreateInvestigationAsync_PostsToCorrectUrl()
    {
        var orgId  = Guid.NewGuid();
        var caseId = Guid.NewGuid();
        var api    = ApiMock();
        api.Setup(x => x.PostAsync<UpsertInvestigationRequest, InvestigationRecord>(
                InvBase(orgId, caseId),
                It.IsAny<UpsertInvestigationRequest>(),
                It.IsAny<CancellationToken>()))
           .ReturnsAsync(new InvestigationRecord { Id = Guid.NewGuid(), CaseId = caseId, Title = "Inv", Status = InvestigationStatus.Scheduled, ScheduledDateTime = DateTime.UtcNow });

        var req = new UpsertInvestigationRequest("Inv", null, null, DateTime.UtcNow, null, InvestigationStatus.Scheduled, null, null);
        var result = await Build(api).CreateInvestigationAsync(orgId, caseId, req);

        Assert.NotNull(result);
        api.Verify(x => x.PostAsync<UpsertInvestigationRequest, InvestigationRecord>(
            InvBase(orgId, caseId), req, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── UpdateInvestigationAsync ──────────────────────────────────────────────

    [Fact]
    public async Task UpdateInvestigationAsync_PutsToCorrectUrl()
    {
        var orgId  = Guid.NewGuid();
        var caseId = Guid.NewGuid();
        var invId  = Guid.NewGuid();
        var api    = ApiMock();
        api.Setup(x => x.PutAsync<UpsertInvestigationRequest, InvestigationRecord>(
                $"{InvBase(orgId, caseId)}/{invId}",
                It.IsAny<UpsertInvestigationRequest>(),
                It.IsAny<CancellationToken>()))
           .ReturnsAsync(new InvestigationRecord { Id = invId, CaseId = caseId, Title = "Updated", Status = InvestigationStatus.Completed, ScheduledDateTime = DateTime.UtcNow });

        var req = new UpsertInvestigationRequest("Updated", null, null, DateTime.UtcNow, null, InvestigationStatus.Completed, "<p>Done.</p>", null);
        var result = await Build(api).UpdateInvestigationAsync(orgId, caseId, invId, req);

        Assert.Equal(InvestigationStatus.Completed, result!.Status);
    }

    // ── Investigation attendees ───────────────────────────────────────────────

    [Fact]
    public async Task AddInvestigationAttendeeAsync_PostsToAttendeesUrl()
    {
        var orgId  = Guid.NewGuid();
        var caseId = Guid.NewGuid();
        var invId  = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var api    = ApiMock();
        api.Setup(x => x.PostAsync<AddInvestigationAttendeeRequest, InvestigationAttendeeRecord>(
                $"{InvBase(orgId, caseId)}/{invId}/attendees",
                It.IsAny<AddInvestigationAttendeeRequest>(),
                It.IsAny<CancellationToken>()))
           .ReturnsAsync(new InvestigationAttendeeRecord
           {
               Id = Guid.NewGuid(), InvestigationId = invId, AppUserId = userId,
               AssignedRole = "Lead Investigator",
           });

        var req = new AddInvestigationAttendeeRequest(userId, "Lead Investigator");
        var result = await Build(api).AddInvestigationAttendeeAsync(orgId, caseId, invId, req);

        Assert.Equal("Lead Investigator", result!.AssignedRole);
        api.Verify(x => x.PostAsync<AddInvestigationAttendeeRequest, InvestigationAttendeeRecord>(
            $"{InvBase(orgId, caseId)}/{invId}/attendees", req, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── Evidence voting ───────────────────────────────────────────────────────

    [Fact]
    public async Task GetEvidenceVoteSummaryAsync_GetsFromSummaryUrl()
    {
        var fileId = Guid.NewGuid();
        var api    = ApiMock();
        api.Setup(x => x.GetAnonymousAsync<EvidenceVoteSummary>(
                $"/api/evidence-votes/{fileId}/summary", It.IsAny<CancellationToken>()))
           .ReturnsAsync(new EvidenceVoteSummary(fileId, 5, 1, 2, 8, EvidenceVoteType.Confirms));

        var result = await Build(api).GetEvidenceVoteSummaryAsync(fileId);

        Assert.Equal(8, result!.TotalVotes);
        Assert.Equal(5, result.ConfirmsCount);
        api.Verify(x => x.GetAnonymousAsync<EvidenceVoteSummary>(
            $"/api/evidence-votes/{fileId}/summary", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CastEvidenceVoteAsync_PostsToVoteUrl()
    {
        var fileId = Guid.NewGuid();
        var api    = ApiMock();
        api.Setup(x => x.PostAsync<object, EvidenceVoteSummary>(
                $"/api/evidence-votes/{fileId}",
                It.IsAny<object>(),
                It.IsAny<CancellationToken>()))
           .ReturnsAsync(new EvidenceVoteSummary(fileId, 6, 1, 2, 9, EvidenceVoteType.Confirms));

        var result = await Build(api).CastEvidenceVoteAsync(fileId, EvidenceVoteType.Confirms, "Great evidence!");

        Assert.Equal(EvidenceVoteType.Confirms, result!.CurrentUserVote);
        api.Verify(x => x.PostAsync<object, EvidenceVoteSummary>(
            $"/api/evidence-votes/{fileId}", It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RemoveEvidenceVoteAsync_DeletesCorrectUrl()
    {
        var fileId = Guid.NewGuid();
        var api    = ApiMock();
        api.Setup(x => x.DeleteAsync($"/api/evidence-votes/{fileId}", It.IsAny<CancellationToken>()))
           .ReturnsAsync(true);

        await Build(api).RemoveEvidenceVoteAsync(fileId);

        api.Verify(x => x.DeleteAsync(
            $"/api/evidence-votes/{fileId}", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void EvidenceVoteSummary_DoesNotExposeVoterIdentity()
    {
        var props = typeof(EvidenceVoteSummary).GetProperties().Select(p => p.Name).ToHashSet();
        Assert.DoesNotContain("VoterAppUserId", props);
        Assert.DoesNotContain("VoterDisplayName", props);
        Assert.Contains("ConfirmsCount", props);
        Assert.Contains("TotalVotes", props);
        Assert.Contains("CurrentUserVote", props);
    }
}
