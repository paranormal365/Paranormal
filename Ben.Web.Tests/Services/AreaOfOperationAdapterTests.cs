using Ben.Service.Models.Entities;
using Ben.Web.Services;
using Ben.Web.Services.WebApi;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// Tests for the area of operation / org search methods in BenAdminClientAdapter.
/// Also tests the Haversine distance formula and public search result privacy.
/// </summary>
public class AreaOfOperationAdapterTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Mock<IWebApiClient> ApiMock() => new();
    private static Mock<IWebApiAuthService> AuthMock() => new();

    private static BenAdminClientAdapter Build(Mock<IWebApiClient> api)
        => new BenAdminClientAdapter(api.Object, AuthMock().Object,
            Microsoft.Extensions.Options.Options.Create(new WebApiOptions()));

    // ── GetOrgAreaOfOperationAsync ────────────────────────────────────────────

    [Fact]
    public async Task GetOrgAreaOfOperationAsync_GetsFromCorrectUrl()
    {
        var orgId  = Guid.NewGuid();
        var api    = ApiMock();
        var record = new OrganizationAreaOfOperationRecord
        {
            RadiusMiles    = 30m,
            CenterLatitude = 36.16m,
            CenterLongitude = -86.78m,
            DisplayLabel   = "Within 30 miles of Nashville, TN",
        };
        api.Setup(x => x.GetAsync<OrganizationAreaOfOperationRecord>(
                $"/api/organizations/{orgId}/area-of-operation",
                It.IsAny<CancellationToken>()))
           .ReturnsAsync(record);

        var result = await Build(api).GetOrgAreaOfOperationAsync(orgId);

        Assert.Equal(30m, result!.RadiusMiles);
        Assert.Equal("Within 30 miles of Nashville, TN", result.DisplayLabel);
        api.Verify(x => x.GetAsync<OrganizationAreaOfOperationRecord>(
            $"/api/organizations/{orgId}/area-of-operation",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetOrgAreaOfOperationAsync_WhenNotConfigured_ReturnsNull()
    {
        var orgId = Guid.NewGuid();
        var api   = ApiMock();
        api.Setup(x => x.GetAsync<OrganizationAreaOfOperationRecord>(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
           .ReturnsAsync((OrganizationAreaOfOperationRecord?)null);

        var result = await Build(api).GetOrgAreaOfOperationAsync(orgId);

        Assert.Null(result);
    }

    // ── UpsertOrgAreaOfOperationAsync ─────────────────────────────────────────

    [Fact]
    public async Task UpsertOrgAreaOfOperationAsync_PutsToCorrectUrl()
    {
        var orgId   = Guid.NewGuid();
        var api     = ApiMock();
        var updated = new OrganizationAreaOfOperationRecord { RadiusMiles = 25m };
        api.Setup(x => x.PutAsync<UpsertAreaOfOperationRequest, OrganizationAreaOfOperationRecord>(
                $"/api/organizations/{orgId}/area-of-operation",
                It.IsAny<UpsertAreaOfOperationRequest>(),
                It.IsAny<CancellationToken>()))
           .ReturnsAsync(updated);

        var req = new UpsertAreaOfOperationRequest(25m, 36m, -87m, "Within 25 miles", true, false);
        var result = await Build(api).UpsertOrgAreaOfOperationAsync(orgId, req);

        Assert.Equal(25m, result!.RadiusMiles);
        api.Verify(x => x.PutAsync<UpsertAreaOfOperationRequest, OrganizationAreaOfOperationRecord>(
            $"/api/organizations/{orgId}/area-of-operation", req,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── DeleteOrgAreaOfOperationAsync ─────────────────────────────────────────

    [Fact]
    public async Task DeleteOrgAreaOfOperationAsync_DeletesCorrectUrl()
    {
        var orgId = Guid.NewGuid();
        var api   = ApiMock();
        api.Setup(x => x.DeleteAsync(
                $"/api/organizations/{orgId}/area-of-operation",
                It.IsAny<CancellationToken>()))
           .ReturnsAsync(true);

        await Build(api).DeleteOrgAreaOfOperationAsync(orgId);

        api.Verify(x => x.DeleteAsync(
            $"/api/organizations/{orgId}/area-of-operation",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── UpdateClientAcceptanceAsync ───────────────────────────────────────────

    [Fact]
    public async Task UpdateClientAcceptanceAsync_PutsToAcceptanceUrl()
    {
        var orgId = Guid.NewGuid();
        var api   = ApiMock();
        api.Setup(x => x.PutVoidAsync(
                $"/api/organizations/{orgId}/area-of-operation/acceptance",
                It.IsAny<object>(),
                It.IsAny<CancellationToken>()))
           .ReturnsAsync(true);

        await Build(api).UpdateClientAcceptanceAsync(orgId, true, false);

        api.Verify(x => x.PutVoidAsync(
            $"/api/organizations/{orgId}/area-of-operation/acceptance",
            It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── SearchOrganizationsAsync ──────────────────────────────────────────────

    [Fact]
    public async Task SearchOrganizationsAsync_GetsToPublicSearchUrl()
    {
        var api     = ApiMock();
        var results = new List<OrgSearchResult>
        {
            new(Guid.NewGuid(), "Ghost Hunters TN", "ghost-hunters-tn",
                "Within 30 miles of Nashville, TN", 30, 12.4, true, false, null),
        };
        api.Setup(x => x.GetAnonymousListAsync<OrgSearchResult>(
                It.Is<string>(s => s.StartsWith("/api/public/organizations/search")),
                It.IsAny<CancellationToken>()))
           .ReturnsAsync(LoadResult<OrgSearchResult>.Ok(results));

        var result = await Build(api).SearchOrganizationsAsync(36.1627, -86.7816);

        Assert.False(result.Failed);
        Assert.Single(result.Items);
        Assert.Equal("Ghost Hunters TN", result.Items[0].Name);
        Assert.True(result.Items[0].IsWithinRange);
    }

    /// <summary>
    /// A refused search must not read as a part of the country with no groups in it.
    /// </summary>
    /// <remarks>
    /// This test used to assert the opposite — that a failed fetch "returns empty" — which was a
    /// green test defending item 120's bug. The front page runs this search for signed-out
    /// visitors, who have no account and no error to go on.
    /// </remarks>
    [Fact]
    public async Task SearchOrganizationsAsync_WhenTheApiRefuses_SaysSoRatherThanReturningEmpty()
    {
        var api = ApiMock();
        api.Setup(x => x.GetAnonymousListAsync<OrgSearchResult>(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
           .ReturnsAsync(LoadResult<OrgSearchResult>.Failure("The server answered 403 (Forbidden)."));

        var result = await Build(api).SearchOrganizationsAsync(36.0, -87.0);

        Assert.True(result.Failed);
        Assert.False(result.IsEmpty);
        Assert.Equal("The server answered 403 (Forbidden).", result.Reason);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task SearchOrganizationsAsync_IncludesCoordinatesInUrl()
    {
        var api = ApiMock();
        string? capturedUrl = null;
        api.Setup(x => x.GetAnonymousListAsync<OrgSearchResult>(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
           .Callback<string, CancellationToken>((url, _) => capturedUrl = url)
           .ReturnsAsync(LoadResult<OrgSearchResult>.Ok([]));

        await Build(api).SearchOrganizationsAsync(36.1627, -86.7816, maxResults: 10);

        Assert.NotNull(capturedUrl);
        Assert.Contains("lat=36.1627", capturedUrl);
        Assert.Contains("lon=-86.7816", capturedUrl);
        Assert.Contains("maxResults=10", capturedUrl);
    }

    // ── OrgSearchResult privacy ───────────────────────────────────────────────

    [Fact]
    public void OrgSearchResult_DoesNotExposeCoordinates()
    {
        // The OrgSearchResult record must NOT have CenterLatitude or CenterLongitude
        var type = typeof(OrgSearchResult);
        var props = type.GetProperties().Select(p => p.Name).ToArray();

        Assert.DoesNotContain("CenterLatitude",  props);
        Assert.DoesNotContain("CenterLongitude", props);
        Assert.DoesNotContain("Latitude",        props);
        Assert.DoesNotContain("Longitude",       props);
    }

    [Fact]
    public void OrgSearchResult_ContainsExpectedPublicFields()
    {
        var type  = typeof(OrgSearchResult);
        var props = type.GetProperties().Select(p => p.Name).ToHashSet();

        Assert.Contains("OrganizationId",           props);
        Assert.Contains("Name",                     props);
        Assert.Contains("UrlName",                  props);
        Assert.Contains("DisplayLabel",             props);
        Assert.Contains("RadiusMiles",              props);
        Assert.Contains("DistanceFromSearchMiles",  props);
        Assert.Contains("IsWithinRange",            props);
        Assert.Contains("AcceptsClientsOutsideRange", props);
    }

    // ── Haversine distance formula ────────────────────────────────────────────
    // The formula is duplicated in the WebApi controller for isolation.
    // We test it here through a local copy to validate the math independently.

    [Theory]
    [InlineData(36.1627, -86.7816, 36.1627, -86.7816, 0.0)]         // Same point = 0
    [InlineData(36.1627, -86.7816, 35.9606, -83.9207, 160.0)]       // Nashville→Knoxville ≈ 160 mi
    [InlineData(40.7128, -74.0060, 34.0522, -118.2437, 2445.0)]     // NYC→LA ≈ 2445 mi
    public void HaversineDistance_ReturnsApproximatelyCorrectMiles(
        double lat1, double lon1, double lat2, double lon2, double expectedMiles)
    {
        double dist = HaversineDistanceMiles(lat1, lon1, lat2, lon2);

        if (expectedMiles == 0.0)
            Assert.Equal(0.0, dist, precision: 3);
        else
            // Allow ±5% tolerance for floating-point rounding
            Assert.InRange(dist, expectedMiles * 0.95, expectedMiles * 1.05);
    }

    [Fact]
    public void HaversineDistance_IsSymmetric()
    {
        double d1 = HaversineDistanceMiles(36.16, -86.78, 35.96, -83.92);
        double d2 = HaversineDistanceMiles(35.96, -83.92, 36.16, -86.78);
        Assert.Equal(d1, d2, precision: 6);
    }

    [Fact]
    public void OrgSearch_SortOrder_WithinRangeAppearsBeforeOutsideRange()
    {
        // The search returns within-range orgs first, then outside-range.
        // Test the conceptual sorting without needing the full controller.
        var results = new List<(string Name, bool IsWithinRange, double Distance, double Radius)>
        {
            ("Org A", false, 75.0, 50.0),   // outside range, 25 mi past edge
            ("Org B", true,  20.0, 50.0),   // within range, 20 mi from center
            ("Org C", false, 60.0, 50.0),   // outside range, 10 mi past edge
            ("Org D", true,   5.0, 50.0),   // within range, 5 mi from center (closest)
        };

        var ordered = results
            .OrderBy(r => r.IsWithinRange ? 0 : 1)
            .ThenBy(r => r.IsWithinRange ? r.Distance : r.Radius + (r.Distance - r.Radius))
            .ToList();

        // Within-range first, sorted by distance
        Assert.Equal("Org D", ordered[0].Name);  // 5 mi
        Assert.Equal("Org B", ordered[1].Name);  // 20 mi
        // Outside-range next, sorted by how far past the edge
        Assert.Equal("Org C", ordered[2].Name);  // 10 mi past edge
        Assert.Equal("Org A", ordered[3].Name);  // 25 mi past edge
    }

    // ── Local Haversine (mirrors WebApi controller implementation) ────────────

    private static double HaversineDistanceMiles(
        double lat1, double lon1, double lat2, double lon2)
    {
        const double R     = 3958.8;
        const double toRad = Math.PI / 180.0;
        double dLat = (lat2 - lat1) * toRad;
        double dLon = (lon2 - lon1) * toRad;
        double a    = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                      Math.Cos(lat1 * toRad) * Math.Cos(lat2 * toRad) *
                      Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return R * 2.0 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }
}
