using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Public;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Ben.Web.Tests.Controllers;

public class PublicOrganizationSearchControllerTests
{
    private static PublicOrganizationSearchController Build(IDbContextFactory<BenDataContext> factory)
        => new(factory);

    private static async Task<Organization> SeedOrgWithAreaAsync(
        IDbContextFactory<BenDataContext> factory,
        bool isAcceptingClients,
        bool acceptsOutsideRange,
        decimal centerLat,
        decimal centerLon,
        decimal radiusMiles,
        string? displayLabel = null)
    {
        await using var db = await factory.CreateDbContextAsync();

        var org = new Organization
        {
            Id                      = Guid.NewGuid(),
            Name                    = $"Org @ ({centerLat},{centerLon})",
            UrlName                 = $"org-{Guid.NewGuid():N}",
            IsAcceptingClients      = isAcceptingClients,
            AcceptsClientsOutsideRange = acceptsOutsideRange,
            CreatedByAppUserId      = Guid.NewGuid(),
        };
        var area = new OrganizationAreaOfOperation
        {
            Id              = Guid.NewGuid(),
            OrganizationId  = org.Id,
            CenterLatitude  = centerLat,
            CenterLongitude = centerLon,
            RadiusMiles     = radiusMiles,
            DisplayLabel    = displayLabel ?? "Test Region",
            CreatedByAppUserId = org.CreatedByAppUserId,
        };

        db.Organizations.Add(org);
        db.OrganizationAreaOfOperations.Add(area);
        await db.SaveChangesAsync();
        return org;
    }

    // ── Validation ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(-91, 0)]
    [InlineData(91, 0)]
    [InlineData(0, -181)]
    [InlineData(0, 181)]
    public async Task Search_InvalidCoordinates_Returns400(double lat, double lon)
    {
        var factory = TestDbFactory.Create();
        var result  = await Build(factory).Search(lat, lon, ct: CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    // ── No results ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Search_NoOrgsAcceptingClients_ReturnsEmpty()
    {
        var factory = TestDbFactory.Create();
        await SeedOrgWithAreaAsync(factory, isAcceptingClients: false, acceptsOutsideRange: false,
            centerLat: 41.88m, centerLon: -87.63m, radiusMiles: 50);

        var result = await Build(factory).Search(41.88, -87.63, ct: CancellationToken.None);
        var ok     = Assert.IsType<OkObjectResult>(result.Result);
        var list   = Assert.IsAssignableFrom<IEnumerable<OrgSearchResult>>(ok.Value);
        Assert.Empty(list);
    }

    [Fact]
    public async Task Search_OrgOutsideRange_NotAcceptingOutside_ExcludesOrg()
    {
        var factory = TestDbFactory.Create();
        // Org is centered in Chicago (41.88, -87.63) with 10-mile radius
        await SeedOrgWithAreaAsync(factory, isAcceptingClients: true, acceptsOutsideRange: false,
            centerLat: 41.88m, centerLon: -87.63m, radiusMiles: 10);

        // Search from ~60 miles away in Rockford IL
        var result = await Build(factory).Search(42.27, -89.09, ct: CancellationToken.None);
        var ok     = Assert.IsType<OkObjectResult>(result.Result);
        var list   = Assert.IsAssignableFrom<IEnumerable<OrgSearchResult>>(ok.Value);
        Assert.Empty(list);
    }

    // ── Within range ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Search_OrgWithinRange_IncludesAndMarksWithinRange()
    {
        var factory = TestDbFactory.Create();
        await SeedOrgWithAreaAsync(factory, isAcceptingClients: true, acceptsOutsideRange: false,
            centerLat: 41.88m, centerLon: -87.63m, radiusMiles: 50);

        // Search from same location — definitely within 50-mile radius
        var result = await Build(factory).Search(41.88, -87.63, ct: CancellationToken.None);
        var ok     = Assert.IsType<OkObjectResult>(result.Result);
        var list   = Assert.IsAssignableFrom<IEnumerable<OrgSearchResult>>(ok.Value).ToList();

        Assert.Single(list);
        Assert.True(list[0].IsWithinRange);
    }

    // ── Outside range but accepting ───────────────────────────────────────────

    [Fact]
    public async Task Search_OrgOutsideRange_AcceptingOutside_IncludesWithFlag()
    {
        var factory = TestDbFactory.Create();
        await SeedOrgWithAreaAsync(factory, isAcceptingClients: true, acceptsOutsideRange: true,
            centerLat: 41.88m, centerLon: -87.63m, radiusMiles: 10);

        // Search from ~60 miles away
        var result = await Build(factory).Search(42.27, -89.09, ct: CancellationToken.None);
        var ok     = Assert.IsType<OkObjectResult>(result.Result);
        var list   = Assert.IsAssignableFrom<IEnumerable<OrgSearchResult>>(ok.Value).ToList();

        Assert.Single(list);
        Assert.False(list[0].IsWithinRange);
        Assert.True(list[0].AcceptsClientsOutsideRange);
    }

    // ── Ordering ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Search_WithinRangeOrgsAppearBeforeOutsideRange()
    {
        var factory = TestDbFactory.Create();

        // Near org (within 50 miles)
        await SeedOrgWithAreaAsync(factory, isAcceptingClients: true, acceptsOutsideRange: false,
            centerLat: 41.88m, centerLon: -87.63m, radiusMiles: 50);

        // Far org accepting outside range
        await SeedOrgWithAreaAsync(factory, isAcceptingClients: true, acceptsOutsideRange: true,
            centerLat: 42.27m, centerLon: -89.09m, radiusMiles: 5);

        var result = await Build(factory).Search(41.88, -87.63, ct: CancellationToken.None);
        var ok     = Assert.IsType<OkObjectResult>(result.Result);
        var list   = Assert.IsAssignableFrom<IEnumerable<OrgSearchResult>>(ok.Value).ToList();

        Assert.Equal(2, list.Count);
        Assert.True(list[0].IsWithinRange);   // within-range comes first
        Assert.False(list[1].IsWithinRange);
    }

    // ── Privacy ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Search_ResultDoesNotExposeCenterCoordinates()
    {
        var factory = TestDbFactory.Create();
        await SeedOrgWithAreaAsync(factory, isAcceptingClients: true, acceptsOutsideRange: false,
            centerLat: 41.88m, centerLon: -87.63m, radiusMiles: 50);

        var result = await Build(factory).Search(41.88, -87.63, ct: CancellationToken.None);
        var ok     = Assert.IsType<OkObjectResult>(result.Result);
        var list   = Assert.IsAssignableFrom<IEnumerable<OrgSearchResult>>(ok.Value).ToList();

        // Center coordinates must never appear in the search result
        var props = typeof(OrgSearchResult).GetProperties().Select(p => p.Name);
        Assert.DoesNotContain("CenterLatitude",  props);
        Assert.DoesNotContain("CenterLongitude", props);
        Assert.DoesNotContain("Latitude",        props);
        Assert.DoesNotContain("Longitude",       props);
    }

    // ── maxResults ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Search_MaxResults_LimitsReturnedCount()
    {
        var factory = TestDbFactory.Create();
        for (int i = 0; i < 5; i++)
        {
            await SeedOrgWithAreaAsync(factory, isAcceptingClients: true, acceptsOutsideRange: false,
                centerLat: 41.88m, centerLon: -87.63m, radiusMiles: 50);
        }

        var result = await Build(factory).Search(41.88, -87.63, maxResults: 3, ct: CancellationToken.None);
        var ok     = Assert.IsType<OkObjectResult>(result.Result);
        var list   = Assert.IsAssignableFrom<IEnumerable<OrgSearchResult>>(ok.Value).ToList();
        Assert.Equal(3, list.Count);
    }
}
