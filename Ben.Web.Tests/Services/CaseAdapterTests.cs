using Ben.Data.Common.Enums;
using Ben.Service.Models.Entities;
using Ben.Web.Services;
using Ben.Web.Services.WebApi;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// Tests for Case adapter methods: URL construction, delegation, null-safety,
/// and CaseRecord computed properties (reference formatting, display label).
/// </summary>
public class CaseAdapterTests
{
    private static Mock<IWebApiClient> ApiMock() => new();
    private static Mock<IWebApiAuthService> AuthMock() => new();

    private static BenAdminClientAdapter Build(Mock<IWebApiClient> api)
        => new BenAdminClientAdapter(api.Object, AuthMock().Object,
            Microsoft.Extensions.Options.Options.Create(new WebApiOptions()));

    private static CaseRecord MakeCase(int year = 2026, int number = 42,
        string title = "Smith, Nashville TN") =>
        new() { Id = Guid.NewGuid(), OrganizationId = Guid.NewGuid(),
                Title = title, CaseYear = year, OrgCaseNumber = number,
                Status = CaseStatus.Accepted };

    // ── CaseRecord computed properties ────────────────────────────────────────

    [Theory]
    [InlineData(2026, 1,   "#2026-001")]
    [InlineData(2026, 42,  "#2026-042")]
    [InlineData(2026, 100, "#2026-100")]
    [InlineData(2025, 7,   "#2025-007")]
    public void CaseRecord_CaseReference_IsFormattedCorrectly(
        int year, int number, string expected)
    {
        var record = MakeCase(year, number);
        Assert.Equal(expected, record.CaseReference);
    }

    [Fact]
    public void CaseRecord_DisplayLabel_IncludesReferenceAndTitle()
    {
        var record = MakeCase(2026, 42, "Smith, Nashville TN");
        Assert.Equal("#2026-042 — Smith, Nashville TN", record.DisplayLabel);
    }

    [Fact]
    public void CaseRecord_DisplayLabel_HandlesShortTitle()
    {
        var record = MakeCase(2026, 1, "Doe, Austin TX");
        Assert.Equal("#2026-001 — Doe, Austin TX", record.DisplayLabel);
    }

    // ── GetOrgCasesAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetOrgCasesAsync_GetsFromCorrectUrl()
    {
        var orgId = Guid.NewGuid();
        var api   = ApiMock();
        api.Setup(x => x.GetListAsync<CaseRecord>(
                $"/api/organizations/{orgId}/cases", It.IsAny<CancellationToken>()))
           .ReturnsAsync(LoadResult<CaseRecord>.Ok([MakeCase()]));

        var result = await Build(api).GetOrgCasesAsync(orgId);

        Assert.False(result.Failed);
        Assert.Single(result.Items);
        api.Verify(x => x.GetListAsync<CaseRecord>(
            $"/api/organizations/{orgId}/cases", It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// A group's whole case list, refused — read as "no cases", which is what an ordinary member
    /// saw on every org-scoped tab before item 120.
    /// </summary>
    [Fact]
    public async Task GetOrgCasesAsync_WhenTheApiRefuses_SaysSoRatherThanReturningEmpty()
    {
        var api = ApiMock();
        api.Setup(x => x.GetListAsync<CaseRecord>(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
           .ReturnsAsync(LoadResult<CaseRecord>.Failure("The server answered 403 (Forbidden)."));

        var result = await Build(api).GetOrgCasesAsync(Guid.NewGuid());

        Assert.True(result.Failed);
        Assert.False(result.IsEmpty);
        Assert.Equal("The server answered 403 (Forbidden).", result.Reason);
        Assert.Empty(result.Items);
    }

    // ── GetOrgCaseAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task GetOrgCaseAsync_GetsFromCorrectUrl()
    {
        var orgId  = Guid.NewGuid();
        var caseId = Guid.NewGuid();
        var api    = ApiMock();
        api.Setup(x => x.GetAsync<CaseRecord>(
                $"/api/organizations/{orgId}/cases/{caseId}", It.IsAny<CancellationToken>()))
           .ReturnsAsync(MakeCase());

        await Build(api).GetOrgCaseAsync(orgId, caseId);

        api.Verify(x => x.GetAsync<CaseRecord>(
            $"/api/organizations/{orgId}/cases/{caseId}", It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── CreateOrgCaseAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task CreateOrgCaseAsync_PostsToCorrectUrl()
    {
        var orgId = Guid.NewGuid();
        var api   = ApiMock();
        // Reason-carrying since the subscription caps landed: the refusal sentence ("your plan
        // includes 2 open cases…") must survive to the screen, which PostAsync's null cannot do.
        api.Setup(x => x.SendExpectingReasonAsync<CreateCaseRequest, CaseRecord>(
                HttpMethod.Post,
                $"/api/organizations/{orgId}/cases",
                It.IsAny<CreateCaseRequest>(),
                It.IsAny<CancellationToken>()))
           .ReturnsAsync((MakeCase(), (string?)null));

        var req = new CreateCaseRequest("Smith, Nashville TN", null,
            "123 Main", null, "Nashville", "TN", "37201", "US", 36.16m, -86.78m);
        var (result, error) = await Build(api).CreateOrgCaseAsync(orgId, req);

        Assert.NotNull(result);
        Assert.Null(error);
        api.Verify(x => x.SendExpectingReasonAsync<CreateCaseRequest, CaseRecord>(
            HttpMethod.Post, $"/api/organizations/{orgId}/cases", req, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── AcceptClientRequestAsCaseAsync ────────────────────────────────────────

    [Fact]
    public async Task AcceptClientRequestAsCaseAsync_PostsToAcceptUrl()
    {
        var orgId     = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        var api       = ApiMock();
        api.Setup(x => x.SendExpectingReasonAsync<AcceptClientRequestAsCaseRequest, CaseRecord>(
                HttpMethod.Post,
                $"/api/organizations/{orgId}/cases/accept-client-request/{requestId}",
                It.IsAny<AcceptClientRequestAsCaseRequest>(),
                It.IsAny<CancellationToken>()))
           .ReturnsAsync((MakeCase(), (string?)null));

        var req = new AcceptClientRequestAsCaseRequest(null, null);
        await Build(api).AcceptClientRequestAsCaseAsync(orgId, requestId, req);

        api.Verify(x => x.SendExpectingReasonAsync<AcceptClientRequestAsCaseRequest, CaseRecord>(
            HttpMethod.Post,
            $"/api/organizations/{orgId}/cases/accept-client-request/{requestId}",
            req, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── UpdateOrgCaseAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateOrgCaseAsync_PutsToCorrectUrl()
    {
        var orgId  = Guid.NewGuid();
        var caseId = Guid.NewGuid();
        var api    = ApiMock();
        api.Setup(x => x.SendExpectingReasonAsync<UpdateCaseRequest, CaseRecord>(
                HttpMethod.Put,
                $"/api/organizations/{orgId}/cases/{caseId}",
                It.IsAny<UpdateCaseRequest>(),
                It.IsAny<CancellationToken>()))
           .ReturnsAsync((MakeCase(), null));

        var req = new UpdateCaseRequest("Smith, Nashville TN", null,
            CaseStatus.Active, null, false, null);
        await Build(api).UpdateOrgCaseAsync(orgId, caseId, req);

        api.Verify(x => x.SendExpectingReasonAsync<UpdateCaseRequest, CaseRecord>(
                HttpMethod.Put,
            $"/api/organizations/{orgId}/cases/{caseId}", req, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── Timeline ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetCaseTimelineAsync_GetsFromCorrectUrl()
    {
        var orgId  = Guid.NewGuid();
        var caseId = Guid.NewGuid();
        var api    = ApiMock();
        api.Setup(x => x.GetListAsync<CaseTimelineEntryRecord>(
                $"/api/organizations/{orgId}/cases/{caseId}/timeline", It.IsAny<CancellationToken>()))
           .ReturnsAsync(LoadResult<CaseTimelineEntryRecord>.Ok([new() { Id = Guid.NewGuid(), CaseId = caseId,
               AuthorAppUserId = Guid.NewGuid(), EntryType = CaseTimelineEntryType.Evidence }]));

        var result = await Build(api).GetCaseTimelineAsync(orgId, caseId);

        Assert.False(result.Failed);
        Assert.Single(result.Items);
        api.Verify(x => x.GetListAsync<CaseTimelineEntryRecord>(
            $"/api/organizations/{orgId}/cases/{caseId}/timeline",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetCaseTimelineAsync_WhenTheApiRefuses_SaysSoRatherThanReturningEmpty()
    {
        var api = ApiMock();
        api.Setup(x => x.GetListAsync<CaseTimelineEntryRecord>(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
           .ReturnsAsync(LoadResult<CaseTimelineEntryRecord>.Failure("The server answered 403 (Forbidden)."));

        var result = await Build(api).GetCaseTimelineAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.True(result.Failed);
        Assert.False(result.IsEmpty);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task AddCaseTimelineEntryAsync_PostsToTimelineUrl()
    {
        var orgId  = Guid.NewGuid();
        var caseId = Guid.NewGuid();
        var api    = ApiMock();
        // Reason-carrying since item 84: the lapsed-subscription refusal must reach the screen.
        api.Setup(x => x.SendExpectingReasonAsync<UpsertTimelineEntryRequest, CaseTimelineEntryRecord>(
                HttpMethod.Post,
                $"/api/organizations/{orgId}/cases/{caseId}/timeline",
                It.IsAny<UpsertTimelineEntryRequest>(),
                It.IsAny<CancellationToken>()))
           .ReturnsAsync((new CaseTimelineEntryRecord
           {
               Id = Guid.NewGuid(), CaseId = caseId,
               AuthorAppUserId = Guid.NewGuid(),
               EntryType = CaseTimelineEntryType.ClientReport,
           }, (string?)null));

        var req = new UpsertTimelineEntryRequest(CaseTimelineEntryType.ClientReport,
            DateTime.UtcNow, "Knocking at night", "<p>Loud knocking.</p>", CaseTimelineVisibility.OrgOnly, []);
        var (result, error) = await Build(api).AddCaseTimelineEntryAsync(orgId, caseId, req);

        Assert.NotNull(result);
        Assert.Null(error);
        api.Verify(x => x.SendExpectingReasonAsync<UpsertTimelineEntryRequest, CaseTimelineEntryRecord>(
            HttpMethod.Post, $"/api/organizations/{orgId}/cases/{caseId}/timeline", req,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteCaseTimelineEntryAsync_DeletesCorrectUrl()
    {
        var orgId   = Guid.NewGuid();
        var caseId  = Guid.NewGuid();
        var entryId = Guid.NewGuid();
        var api     = ApiMock();
        api.Setup(x => x.DeleteAsync(
                $"/api/organizations/{orgId}/cases/{caseId}/timeline/{entryId}",
                It.IsAny<CancellationToken>()))
           .ReturnsAsync(true);

        await Build(api).DeleteCaseTimelineEntryAsync(orgId, caseId, entryId);

        api.Verify(x => x.DeleteAsync(
            $"/api/organizations/{orgId}/cases/{caseId}/timeline/{entryId}",
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
