using Ben.Data.Common.Enums;
using Ben.Service.Models.Entities;
using Ben.Web.Library.Services;
using Ben.Web.WebApp.Services.WebApi;
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
        api.Setup(x => x.GetAsync<IReadOnlyList<CaseRecord>>(
                $"/api/organizations/{orgId}/cases", It.IsAny<CancellationToken>()))
           .ReturnsAsync([MakeCase()]);

        var result = await Build(api).GetOrgCasesAsync(orgId);

        Assert.Single(result);
        api.Verify(x => x.GetAsync<IReadOnlyList<CaseRecord>>(
            $"/api/organizations/{orgId}/cases", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetOrgCasesAsync_WhenApiReturnsNull_ReturnsEmpty()
    {
        var api = ApiMock();
        api.Setup(x => x.GetAsync<IReadOnlyList<CaseRecord>>(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
           .ReturnsAsync((IReadOnlyList<CaseRecord>?)null);

        var result = await Build(api).GetOrgCasesAsync(Guid.NewGuid());

        Assert.Empty(result);
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
        api.Setup(x => x.PostAsync<CreateCaseRequest, CaseRecord>(
                $"/api/organizations/{orgId}/cases",
                It.IsAny<CreateCaseRequest>(),
                It.IsAny<CancellationToken>()))
           .ReturnsAsync(MakeCase());

        var req = new CreateCaseRequest("Smith, Nashville TN", null,
            "123 Main", null, "Nashville", "TN", "37201", "US", 36.16m, -86.78m);
        var result = await Build(api).CreateOrgCaseAsync(orgId, req);

        Assert.NotNull(result);
        api.Verify(x => x.PostAsync<CreateCaseRequest, CaseRecord>(
            $"/api/organizations/{orgId}/cases", req, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── AcceptClientRequestAsCaseAsync ────────────────────────────────────────

    [Fact]
    public async Task AcceptClientRequestAsCaseAsync_PostsToAcceptUrl()
    {
        var orgId     = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        var api       = ApiMock();
        api.Setup(x => x.PostAsync<AcceptClientRequestAsCaseRequest, CaseRecord>(
                $"/api/organizations/{orgId}/cases/accept-client-request/{requestId}",
                It.IsAny<AcceptClientRequestAsCaseRequest>(),
                It.IsAny<CancellationToken>()))
           .ReturnsAsync(MakeCase());

        var req = new AcceptClientRequestAsCaseRequest(null, null);
        await Build(api).AcceptClientRequestAsCaseAsync(orgId, requestId, req);

        api.Verify(x => x.PostAsync<AcceptClientRequestAsCaseRequest, CaseRecord>(
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
        api.Setup(x => x.PutAsync<UpdateCaseRequest, CaseRecord>(
                $"/api/organizations/{orgId}/cases/{caseId}",
                It.IsAny<UpdateCaseRequest>(),
                It.IsAny<CancellationToken>()))
           .ReturnsAsync(MakeCase());

        var req = new UpdateCaseRequest("Smith, Nashville TN", null,
            CaseStatus.Active, null, false, null);
        await Build(api).UpdateOrgCaseAsync(orgId, caseId, req);

        api.Verify(x => x.PutAsync<UpdateCaseRequest, CaseRecord>(
            $"/api/organizations/{orgId}/cases/{caseId}", req, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── Timeline ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetCaseTimelineAsync_GetsFromCorrectUrl()
    {
        var orgId  = Guid.NewGuid();
        var caseId = Guid.NewGuid();
        var api    = ApiMock();
        api.Setup(x => x.GetAsync<IReadOnlyList<CaseTimelineEntryRecord>>(
                $"/api/organizations/{orgId}/cases/{caseId}/timeline", It.IsAny<CancellationToken>()))
           .ReturnsAsync([new() { Id = Guid.NewGuid(), CaseId = caseId,
               AuthorAppUserId = Guid.NewGuid(), EntryType = CaseTimelineEntryType.Evidence }]);

        var result = await Build(api).GetCaseTimelineAsync(orgId, caseId);

        Assert.Single(result);
        api.Verify(x => x.GetAsync<IReadOnlyList<CaseTimelineEntryRecord>>(
            $"/api/organizations/{orgId}/cases/{caseId}/timeline",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetCaseTimelineAsync_WhenApiReturnsNull_ReturnsEmpty()
    {
        var api = ApiMock();
        api.Setup(x => x.GetAsync<IReadOnlyList<CaseTimelineEntryRecord>>(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
           .ReturnsAsync((IReadOnlyList<CaseTimelineEntryRecord>?)null);

        var result = await Build(api).GetCaseTimelineAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.Empty(result);
    }

    [Fact]
    public async Task AddCaseTimelineEntryAsync_PostsToTimelineUrl()
    {
        var orgId  = Guid.NewGuid();
        var caseId = Guid.NewGuid();
        var api    = ApiMock();
        api.Setup(x => x.PostAsync<UpsertTimelineEntryRequest, CaseTimelineEntryRecord>(
                $"/api/organizations/{orgId}/cases/{caseId}/timeline",
                It.IsAny<UpsertTimelineEntryRequest>(),
                It.IsAny<CancellationToken>()))
           .ReturnsAsync(new CaseTimelineEntryRecord
           {
               Id = Guid.NewGuid(), CaseId = caseId,
               AuthorAppUserId = Guid.NewGuid(),
               EntryType = CaseTimelineEntryType.ClientReport,
           });

        var req = new UpsertTimelineEntryRequest(CaseTimelineEntryType.ClientReport,
            DateTime.UtcNow, "Knocking at night", "<p>Loud knocking.</p>", CaseTimelineVisibility.OrgOnly, []);
        var result = await Build(api).AddCaseTimelineEntryAsync(orgId, caseId, req);

        Assert.NotNull(result);
        api.Verify(x => x.PostAsync<UpsertTimelineEntryRequest, CaseTimelineEntryRecord>(
            $"/api/organizations/{orgId}/cases/{caseId}/timeline", req,
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
