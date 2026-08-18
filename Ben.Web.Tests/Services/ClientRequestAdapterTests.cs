using Ben.Data.Common.Enums;
using Ben.Service.Models.Entities;
using Ben.Web.Services;
using Ben.Web.Services.WebApi;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// Tests for ClientRequest adapter methods in BenAdminClientAdapter.
/// Verifies correct URL construction, delegation, and null-safe returns.
/// </summary>
public class ClientRequestAdapterTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Mock<IWebApiClient> ApiMock() => new();
    private static Mock<IWebApiAuthService> AuthMock() => new();

    private static BenAdminClientAdapter Build(Mock<IWebApiClient> api)
        => new BenAdminClientAdapter(api.Object, AuthMock().Object,
            Microsoft.Extensions.Options.Options.Create(new WebApiOptions()));

    private static ClientRequestRecord MakeRecord(ClientRequestStatus status = ClientRequestStatus.Draft) =>
        new() { Id = Guid.NewGuid(), City = "Nashville", State = "TN", Status = status };

    // ── GetMyClientRequestsAsync ──────────────────────────────────────────────

    [Fact]
    public async Task GetMyClientRequestsAsync_GetsFromCorrectUrl()
    {
        var api  = ApiMock();
        var data = new List<ClientRequestRecord> { MakeRecord() };
        api.Setup(x => x.GetAsync<IReadOnlyList<ClientRequestRecord>>(
                "/api/client-requests/my", It.IsAny<CancellationToken>()))
           .ReturnsAsync(data);

        var result = await Build(api).GetMyClientRequestsAsync();

        Assert.Single(result);
        api.Verify(x => x.GetAsync<IReadOnlyList<ClientRequestRecord>>(
            "/api/client-requests/my", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetMyClientRequestsAsync_WhenApiReturnsNull_ReturnsEmpty()
    {
        var api = ApiMock();
        api.Setup(x => x.GetAsync<IReadOnlyList<ClientRequestRecord>>(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
           .ReturnsAsync((IReadOnlyList<ClientRequestRecord>?)null);

        var result = await Build(api).GetMyClientRequestsAsync();

        Assert.Empty(result);
    }

    // ── GetClientRequestAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task GetClientRequestAsync_GetsFromCorrectUrl()
    {
        var id  = Guid.NewGuid();
        var api = ApiMock();
        api.Setup(x => x.GetAsync<ClientRequestRecord>(
                $"/api/client-requests/{id}", It.IsAny<CancellationToken>()))
           .ReturnsAsync(MakeRecord());

        await Build(api).GetClientRequestAsync(id);

        api.Verify(x => x.GetAsync<ClientRequestRecord>(
            $"/api/client-requests/{id}", It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── CreateClientRequestAsync ──────────────────────────────────────────────

    [Fact]
    public async Task CreateClientRequestAsync_PostsToCorrectUrl()
    {
        var api     = ApiMock();
        var created = MakeRecord();
        api.Setup(x => x.PostAsync<UpsertClientRequestRequest, ClientRequestRecord>(
                "/api/client-requests",
                It.IsAny<UpsertClientRequestRequest>(),
                It.IsAny<CancellationToken>()))
           .ReturnsAsync(created);

        var req = new UpsertClientRequestRequest("123 Main", null, "Nashville", "TN",
            "37201", "US", 36.16m, -86.78m, ClientGender.Male, 1985, "<p>Strange activity.</p>");
        var result = await Build(api).CreateClientRequestAsync(req);

        Assert.NotNull(result);
        api.Verify(x => x.PostAsync<UpsertClientRequestRequest, ClientRequestRecord>(
            "/api/client-requests", req, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── UpdateClientRequestAsync ──────────────────────────────────────────────

    [Fact]
    public async Task UpdateClientRequestAsync_PutsToCorrectUrl()
    {
        var id  = Guid.NewGuid();
        var api = ApiMock();
        api.Setup(x => x.PutAsync<UpsertClientRequestRequest, ClientRequestRecord>(
                $"/api/client-requests/{id}",
                It.IsAny<UpsertClientRequestRequest>(),
                It.IsAny<CancellationToken>()))
           .ReturnsAsync(MakeRecord());

        var req = new UpsertClientRequestRequest("123 Main", null, "Nashville", "TN",
            "37201", "US", 36.16m, -86.78m, ClientGender.Female, null, "<p>Updated.</p>");
        await Build(api).UpdateClientRequestAsync(id, req);

        api.Verify(x => x.PutAsync<UpsertClientRequestRequest, ClientRequestRecord>(
            $"/api/client-requests/{id}", req, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── SubmitClientRequestAsync ──────────────────────────────────────────────

    [Fact]
    public async Task SubmitClientRequestAsync_PostsToSubmitUrl()
    {
        var requestId = Guid.NewGuid();
        var orgId1    = Guid.NewGuid();
        var orgId2    = Guid.NewGuid();
        var api       = ApiMock();
        api.Setup(x => x.PostAsync<object, ClientRequestRecord>(
                $"/api/client-requests/{requestId}/submit",
                It.IsAny<object>(),
                It.IsAny<CancellationToken>()))
           .ReturnsAsync(MakeRecord(ClientRequestStatus.Submitted));

        var result = await Build(api).SubmitClientRequestAsync(requestId, [orgId1, orgId2]);

        Assert.Equal(ClientRequestStatus.Submitted, result!.Status);
        api.Verify(x => x.PostAsync<object, ClientRequestRecord>(
            $"/api/client-requests/{requestId}/submit",
            It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SubmitClientRequestAsync_WhenApiFails_ReturnsNull()
    {
        var id  = Guid.NewGuid();
        var api = ApiMock();
        api.Setup(x => x.PostAsync<object, ClientRequestRecord>(
                It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
           .ReturnsAsync((ClientRequestRecord?)null);

        var result = await Build(api).SubmitClientRequestAsync(id, [Guid.NewGuid()]);

        Assert.Null(result);
    }

    // ── WithdrawClientRequestAsync ────────────────────────────────────────────

    [Fact]
    public async Task WithdrawClientRequestAsync_PostsToWithdrawUrl()
    {
        var id  = Guid.NewGuid();
        var api = ApiMock();
        api.Setup(x => x.PostAsync<object, ClientRequestRecord>(
                $"/api/client-requests/{id}/withdraw",
                It.IsAny<object>(),
                It.IsAny<CancellationToken>()))
           .ReturnsAsync(MakeRecord(ClientRequestStatus.Withdrawn));

        var result = await Build(api).WithdrawClientRequestAsync(id);

        Assert.Equal(ClientRequestStatus.Withdrawn, result!.Status);
        api.Verify(x => x.PostAsync<object, ClientRequestRecord>(
            $"/api/client-requests/{id}/withdraw",
            It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── GetClientRequestOrgsAsync ─────────────────────────────────────────────

    [Fact]
    public async Task GetClientRequestOrgsAsync_GetsFromCorrectUrl()
    {
        var id   = Guid.NewGuid();
        var api  = ApiMock();
        var data = new List<ClientRequestOrganizationRecord>
        {
            new() { Id = Guid.NewGuid(), OrganizationId = Guid.NewGuid(),
                    ClientRequestId = id, Status = ClientOrgRequestStatus.Pending },
        };
        api.Setup(x => x.GetAsync<IReadOnlyList<ClientRequestOrganizationRecord>>(
                $"/api/client-requests/{id}/organizations", It.IsAny<CancellationToken>()))
           .ReturnsAsync(data);

        var result = await Build(api).GetClientRequestOrgsAsync(id);

        Assert.Single(result);
        Assert.Equal(ClientOrgRequestStatus.Pending, result[0].Status);
        api.Verify(x => x.GetAsync<IReadOnlyList<ClientRequestOrganizationRecord>>(
            $"/api/client-requests/{id}/organizations", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetClientRequestOrgsAsync_WhenApiReturnsNull_ReturnsEmpty()
    {
        var api = ApiMock();
        api.Setup(x => x.GetAsync<IReadOnlyList<ClientRequestOrganizationRecord>>(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
           .ReturnsAsync((IReadOnlyList<ClientRequestOrganizationRecord>?)null);

        var result = await Build(api).GetClientRequestOrgsAsync(Guid.NewGuid());

        Assert.Empty(result);
    }
}
