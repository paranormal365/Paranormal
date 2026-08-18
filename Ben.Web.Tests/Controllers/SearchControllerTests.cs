using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Public;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Tests for <see cref="SearchController"/> — verifies proximity search filtering,
/// Haversine bounding, SearchRadiusMiles enforcement, query filtering, and display-mode masking.
/// </summary>
// These assert the ORGANIZATIONS half of /api/public/search/nearby. The endpoint returns two lists
// as of item #88 — groups and public events — because the two obey different privacy rules: a
// searchable group is a business listing shown as precisely as it chose, while an event's location
// is approximate until somebody is attending. The events half is covered by NearbySearchTests.
public class SearchControllerTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static IDbContextFactory<BenDataContext> CreateFactory()
    {
        var opts = new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new PooledDbContextFactory<BenDataContext>(opts);
    }

    private static SearchController Build(IDbContextFactory<BenDataContext> factory)
        => new(factory);

    /// <summary>Seed a minimal org + address at the given coordinates.</summary>
    private static async Task<(Guid orgId, Guid addressId)> SeedAsync(
        IDbContextFactory<BenDataContext> factory,
        string orgName, string orgUrl,
        decimal lat, decimal lon,
        bool isSearchable = true,
        OrganizationAddressVisibility searchVis = OrganizationAddressVisibility.Public,
        double? searchRadius = null,
        OrganizationAddressDisplayMode displayMode = OrganizationAddressDisplayMode.FullAddressAndMap)
    {
        var orgId     = Guid.NewGuid();
        var creatorId = Guid.NewGuid();
        var addrId    = Guid.NewGuid();
        var typeId    = Guid.NewGuid();

        await using var db = await factory.CreateDbContextAsync();

        db.AppUsers.Add(new AppUser { Id = creatorId, UserName = creatorId.ToString(), Email = $"{creatorId}@t.com" });
        db.Organizations.Add(new Organization { Id = orgId, Name = orgName, UrlName = orgUrl, DateCreated = DateTime.UtcNow, CreatedByAppUserId = creatorId });
        db.OrganizationAddressTypes.Add(new OrganizationAddressType { Id = typeId, Name = "Main", IsActive = true, IsPublic = true, SortOrder = 1, DateCreated = DateTime.UtcNow, CreatedByAppUserId = creatorId });
        db.OrganizationAddresses.Add(new OrganizationAddress
        {
            Id = addrId, OrganizationId = orgId, OrganizationAddressTypeId = typeId,
            StreetAddress1 = "123 Main St", City = "TestCity", State = "TN", ZipCode = "37000", Country = "US",
            Latitude = lat, Longitude = lon,
            IsSearchable = isSearchable, SearchVisibility = searchVis,
            SearchRadiusMiles = searchRadius,
            Visibility = OrganizationAddressVisibility.Public,
            PublicDisplayMode = displayMode,
            MemberDisplayMode = OrganizationAddressDisplayMode.FullAddressAndMap,
            SortOrder = 1, DateCreated = DateTime.UtcNow, CreatedByAppUserId = creatorId
        });
        await db.SaveChangesAsync();
        return (orgId, addrId);
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Nearby_WhenNoSearchableAddresses_ReturnsEmpty()
    {
        var factory = CreateFactory();
        await SeedAsync(factory, "Org", "org", 36.0m, -86.0m, isSearchable: false);

        var ctrl   = Build(factory);
        var result = await ctrl.Nearby(36.0, -86.0, 25);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var list = Assert.IsType<NearbyResults>(ok.Value).Organizations;
        Assert.Empty(list);
    }

    [Fact]
    public async Task Nearby_WithSearchableAddress_WhenWithinRadius_ReturnsOrg()
    {
        var factory = CreateFactory();
        // Nashville area coords — place org ~1 mile away
        await SeedAsync(factory, "Close Org", "close-org", 36.1627m, -86.7816m);

        var ctrl   = Build(factory);
        var result = await ctrl.Nearby(36.1627, -86.7816, 25); // search 25 miles around same point

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var list = Assert.IsType<NearbyResults>(ok.Value).Organizations;
        Assert.Single(list);
        Assert.Equal("Close Org", list[0].OrgName);
        Assert.True(list[0].DistanceMiles < 1.0); // ~0 distance — same coords
    }

    [Fact]
    public async Task Nearby_WhenAddressOutsideSearchRadius_NotReturned()
    {
        var factory = CreateFactory();
        // Org is 50 miles away
        await SeedAsync(factory, "Far Org", "far-org", 37.0m, -86.0m); // ~69 miles N of 36.0

        var ctrl   = Build(factory);
        var result = await ctrl.Nearby(36.0, -86.0, 10); // only 10-mile search

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var list = Assert.IsType<NearbyResults>(ok.Value).Organizations;
        Assert.Empty(list);
    }

    [Fact]
    public async Task Nearby_AddressSearchRadiusMiles_LimitsVisibility()
    {
        var factory = CreateFactory();
        // Org says "only show in search if searcher is within 2 miles"
        await SeedAsync(factory, "Near Only", "near-only", 36.0m, -86.0m, searchRadius: 2.0);

        var ctrl = Build(factory);

        // Searcher is ~3 miles away — should NOT appear
        var result1 = await ctrl.Nearby(36.045, -86.0, 50); // ~3 miles N
        var ok1 = Assert.IsType<OkObjectResult>(result1.Result);
        var list1 = Assert.IsType<NearbyResults>(ok1.Value).Organizations;
        Assert.Empty(list1);

        // Searcher is right at the address — should appear
        var result2 = await ctrl.Nearby(36.0, -86.0, 50);
        var ok2 = Assert.IsType<OkObjectResult>(result2.Result);
        var list2 = Assert.IsType<NearbyResults>(ok2.Value).Organizations;
        Assert.Single(list2);
    }

    [Fact]
    public async Task Nearby_QueryFilter_OnlyMatchingOrgNamesReturned()
    {
        var factory = CreateFactory();
        await SeedAsync(factory, "McDonalds Franklin",  "mcdonalds",  36.0m, -86.0m);
        await SeedAsync(factory, "Burger King Franklin","burger-king", 36.0m, -86.0m);

        var ctrl   = Build(factory);
        var result = await ctrl.Nearby(36.0, -86.0, 50, query: "McDonald");

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var list = Assert.IsType<NearbyResults>(ok.Value).Organizations;
        Assert.Single(list);
        Assert.Equal("McDonalds Franklin", list[0].OrgName);
    }

    [Fact]
    public async Task Nearby_RegionOnlyDisplayMode_OmitsExactCoordsInResult()
    {
        var factory = CreateFactory();
        await SeedAsync(factory, "Privacy Org", "privacy-org", 36.0m, -86.0m,
            displayMode: OrganizationAddressDisplayMode.RegionOnly);

        var ctrl   = Build(factory);
        var result = await ctrl.Nearby(36.0, -86.0, 50);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var list = Assert.IsType<NearbyResults>(ok.Value).Organizations;
        Assert.Single(list);
        // Exact coords should be masked for RegionOnly
        Assert.Null(list[0].Latitude);
        Assert.Null(list[0].Longitude);
    }

    [Fact]
    public async Task Nearby_ResultsOrderedByDistanceAscending()
    {
        var factory = CreateFactory();
        // Seed a far org first, then a near org
        await SeedAsync(factory, "Far Org",  "far",  36.2m, -86.0m); // ~14 miles N
        await SeedAsync(factory, "Near Org", "near", 36.01m, -86.0m); // ~0.7 miles N

        var ctrl   = Build(factory);
        var result = await ctrl.Nearby(36.0, -86.0, 50);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var list = Assert.IsType<NearbyResults>(ok.Value).Organizations;
        Assert.Equal(2, list.Count);
        Assert.Equal("Near Org", list[0].OrgName);  // closer first
        Assert.Equal("Far Org",  list[1].OrgName);
    }
}
