using Ben.Data.Common.Enums;
using Ben.Service.Models.Entities;
using Ben.Web.Services;
using Ben.Web.Services.WebApi;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>Tests for Phase 6 case transfer and public discovery adapter methods.</summary>
public class Phase6AdapterTests
{
    private static Mock<IWebApiClient> ApiMock() => new();
    private static Mock<IWebApiAuthService> AuthMock() => new();

    private static BenAdminClientAdapter Build(Mock<IWebApiClient> api)
        => new BenAdminClientAdapter(api.Object, AuthMock().Object,
            Microsoft.Extensions.Options.Options.Create(new WebApiOptions()));

    // ── Case transfers ────────────────────────────────────────────────────────

    [Fact]
    public async Task GetCaseTransfersAsync_GetsFromCorrectUrl()
    {
        var orgId  = Guid.NewGuid();
        var caseId = Guid.NewGuid();
        var api    = ApiMock();
        api.Setup(x => x.GetListAsync<CaseTransferLogRecord>(
                $"/api/organizations/{orgId}/cases/{caseId}/transfers", It.IsAny<CancellationToken>()))
           .ReturnsAsync(LoadResult<CaseTransferLogRecord>.Ok([new() { Id = Guid.NewGuid(), CaseId = caseId,
               FromOrganizationId = orgId, ToOrganizationId = Guid.NewGuid(),
               ProposedByAppUserId = Guid.NewGuid(), Status = CaseTransferStatus.Pending,
               DateProposed = DateTime.UtcNow }]));

        var result = await Build(api).GetCaseTransfersAsync(orgId, caseId);

        Assert.False(result.Failed);
        Assert.Single(result.Items);
        api.Verify(x => x.GetListAsync<CaseTransferLogRecord>(
            $"/api/organizations/{orgId}/cases/{caseId}/transfers", It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// A refused fetch must not read as "there is nothing here".
    /// </summary>
    /// <remarks>
    /// This assertion used to be the opposite — that a non-2xx "returns empty" — which made it a
    /// green test defending item 120's bug rather than catching it.
    /// </remarks>
    [Fact]
    public async Task GetCaseTransfersAsync_WhenTheApiRefuses_SaysSoRatherThanReturningEmpty()
    {
        var api = ApiMock();
        api.Setup(x => x.GetListAsync<CaseTransferLogRecord>(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
           .ReturnsAsync(LoadResult<CaseTransferLogRecord>.Failure("The server answered 403 (Forbidden)."));

        var result = await Build(api).GetCaseTransfersAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.True(result.Failed);
        Assert.False(result.IsEmpty);
        Assert.Equal("The server answered 403 (Forbidden).", result.Reason);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task ProposeCaseTransferAsync_PostsToCorrectUrl()
    {
        var orgId  = Guid.NewGuid();
        var caseId = Guid.NewGuid();
        var toOrg  = Guid.NewGuid();
        var api    = ApiMock();
        api.Setup(x => x.SendExpectingReasonAsync<object, CaseTransferLogRecord>(
                HttpMethod.Post, $"/api/organizations/{orgId}/cases/{caseId}/transfers",
                It.IsAny<object>(),
                It.IsAny<CancellationToken>()))
           .ReturnsAsync((new CaseTransferLogRecord
           {
               Id = Guid.NewGuid(), CaseId = caseId,
               FromOrganizationId = orgId, ToOrganizationId = toOrg,
               ProposedByAppUserId = Guid.NewGuid(), Status = CaseTransferStatus.Pending,
               DateProposed = DateTime.UtcNow,
           }, (string?)null));

        var (result, error) = await Build(api).ProposeCaseTransferAsync(orgId, caseId, toOrg, "Org closing.");

        Assert.Null(error);
        Assert.Equal(CaseTransferStatus.Pending, result!.Status);
        api.Verify(x => x.SendExpectingReasonAsync<object, CaseTransferLogRecord>(
            HttpMethod.Post, $"/api/organizations/{orgId}/cases/{caseId}/transfers",
            It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RespondCaseTransferAsync_PutsToRespondUrl()
    {
        var orgId  = Guid.NewGuid();
        var caseId = Guid.NewGuid();
        var logId  = Guid.NewGuid();
        var api    = ApiMock();
        api.Setup(x => x.SendExpectingReasonAsync<object, CaseTransferLogRecord>(
                HttpMethod.Put, $"/api/organizations/{orgId}/cases/{caseId}/transfers/{logId}/respond",
                It.IsAny<object>(),
                It.IsAny<CancellationToken>()))
           .ReturnsAsync((new CaseTransferLogRecord
           {
               Id = logId, CaseId = caseId,
               FromOrganizationId = Guid.NewGuid(), ToOrganizationId = orgId,
               ProposedByAppUserId = Guid.NewGuid(), Status = CaseTransferStatus.Accepted,
               DateProposed = DateTime.UtcNow, DateResponded = DateTime.UtcNow,
           }, (string?)null));

        var (result, error) = await Build(api).RespondCaseTransferAsync(orgId, caseId, logId, true, null);

        Assert.Null(error);
        Assert.Equal(CaseTransferStatus.Accepted, result!.Status);
        api.Verify(x => x.SendExpectingReasonAsync<object, CaseTransferLogRecord>(
            HttpMethod.Put, $"/api/organizations/{orgId}/cases/{caseId}/transfers/{logId}/respond",
            It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── Public case discovery ─────────────────────────────────────────────────

    [Fact]
    public async Task GetPublicCasesAsync_GetsFromPublicUrl()
    {
        var api = ApiMock();
        api.Setup(x => x.GetAnonymousListAsync<PublicCaseListItem>(
                "/api/public/organizations/ghost-hunters-tn/cases", It.IsAny<CancellationToken>()))
           .ReturnsAsync(LoadResult<PublicCaseListItem>.Ok([new("#2026-042", "the-mill-house",
               "Smith, Nashville TN", "Nashville", "TN",
               CaseStatus.Public, DateTime.UtcNow, null, false)]));

        var result = await Build(api).GetPublicCasesAsync("ghost-hunters-tn");

        Assert.False(result.Failed);
        Assert.Single(result.Items);
        Assert.Equal("#2026-042", result.Items[0].CaseReference);
        api.Verify(x => x.GetAnonymousListAsync<PublicCaseListItem>(
            "/api/public/organizations/ghost-hunters-tn/cases", It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// The anonymous case list, refused. A visitor has no account and no error console; if this
    /// page says the group has published nothing, that is all they will ever know.
    /// </summary>
    [Fact]
    public async Task GetPublicCasesAsync_WhenTheApiRefuses_SaysSoRatherThanReturningEmpty()
    {
        var api = ApiMock();
        api.Setup(x => x.GetAnonymousListAsync<PublicCaseListItem>(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
           .ReturnsAsync(LoadResult<PublicCaseListItem>.Failure("The server answered 403 (Forbidden)."));

        var result = await Build(api).GetPublicCasesAsync("org");

        Assert.True(result.Failed);
        Assert.False(result.IsEmpty);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task GetPublicCaseAsync_GetsFromCorrectUrl()
    {
        var api = ApiMock();
        api.Setup(x => x.GetAnonymousAsync<PublicCaseDetail>(
                "/api/public/organizations/ghost-hunters-tn/cases/2026-042",
                It.IsAny<CancellationToken>()))
           .ReturnsAsync(new PublicCaseDetail(
               Guid.NewGuid(), "#2026-042", "Smith, Nashville TN", "Nashville", "TN", "US",
               CaseStatus.Public, false, "The Smith Family", null,
               DateTime.UtcNow, null, [], "Ghost Hunters TN", "ghost-hunters-tn"));

        var result = await Build(api).GetPublicCaseAsync("ghost-hunters-tn", "2026-042");

        Assert.Equal("The Smith Family", result!.ClientName);
        Assert.Equal("#2026-042", result.CaseReference);
    }

    // ── Privacy checks ────────────────────────────────────────────────────────

    [Fact]
    public void PublicCaseDetail_DoesNotExposePrivateAddress()
    {
        var props = typeof(PublicCaseDetail).GetProperties().Select(p => p.Name).ToHashSet();
        // Should not expose private lat/lon or street address
        Assert.DoesNotContain("Latitude",      props);
        Assert.DoesNotContain("Longitude",     props);
        Assert.DoesNotContain("StreetAddress1", props);
        // Should expose city/state (public enough)
        Assert.Contains("City",  props);
        Assert.Contains("State", props);
    }

    [Fact]
    public void PublicCaseListItem_DoesNotExposePrivateAddress()
    {
        var props = typeof(PublicCaseListItem).GetProperties().Select(p => p.Name).ToHashSet();
        Assert.DoesNotContain("Latitude",  props);
        Assert.DoesNotContain("Longitude", props);
        Assert.Contains("CaseReference", props);
    }
}
