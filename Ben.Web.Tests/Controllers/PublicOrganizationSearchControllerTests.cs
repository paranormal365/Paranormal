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
        string? displayLabel = null,
        bool isUnlisted = false)
    {
        await using var db = await factory.CreateDbContextAsync();

        var org = new Organization
        {
            Id                      = Guid.NewGuid(),
            Name                    = $"Org @ ({centerLat},{centerLon})",
            UrlName                 = $"org-{Guid.NewGuid():N}",
            IsAcceptingClients      = isAcceptingClients,
            AcceptsClientsOutsideRange = acceptsOutsideRange,
            IsUnlisted              = isUnlisted,
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

    // ── The three conditions a client search demands ──────────────────────────
    //
    // A group that takes client cases reaches a client only when it is accepting, listed, and has
    // an operating area. The group page renders a "clients cannot find you" notice from exactly
    // these three, so each one is pinned here: the rule and the notice must not drift apart.
    // Each test seeds a reachable twin at the same point, so removing the clause under test makes
    // the assertion fail rather than quietly returning both.

    /// <summary>Seeds a group that takes clients but never said where it works.</summary>
    private static async Task<Organization> SeedOrgWithoutAreaAsync(
        IDbContextFactory<BenDataContext> factory)
    {
        await using var db = await factory.CreateDbContextAsync();
        var org = new Organization
        {
            Id                 = Guid.NewGuid(),
            Name               = "No area at all",
            UrlName            = $"org-{Guid.NewGuid():N}",
            IsAcceptingClients = true,
            CreatedByAppUserId = Guid.NewGuid(),
        };
        db.Organizations.Add(org);
        await db.SaveChangesAsync();
        return org;
    }

    [Fact]
    public async Task Search_UnlistedOrg_IsExcludedWhileItsListedTwinIsReturned()
    {
        var factory = TestDbFactory.Create();
        await SeedOrgWithAreaAsync(factory, isAcceptingClients: true, acceptsOutsideRange: false,
            centerLat: 41.88m, centerLon: -87.63m, radiusMiles: 50, displayLabel: "Listed");
        await SeedOrgWithAreaAsync(factory, isAcceptingClients: true, acceptsOutsideRange: false,
            centerLat: 41.88m, centerLon: -87.63m, radiusMiles: 50, displayLabel: "Unlisted",
            isUnlisted: true);

        var result = await Build(factory).Search(41.88, -87.63, ct: CancellationToken.None);
        var ok     = Assert.IsType<OkObjectResult>(result.Result);
        var list   = Assert.IsAssignableFrom<IEnumerable<OrgSearchResult>>(ok.Value).ToList();

        Assert.Single(list);
        Assert.Equal("Listed", list[0].DisplayLabel);
    }

    [Fact]
    public async Task Search_OrgWithNoOperatingArea_IsExcludedWhileOneWithAnAreaIsReturned()
    {
        var factory = TestDbFactory.Create();
        await SeedOrgWithoutAreaAsync(factory);
        await SeedOrgWithAreaAsync(factory, isAcceptingClients: true, acceptsOutsideRange: false,
            centerLat: 41.88m, centerLon: -87.63m, radiusMiles: 50, displayLabel: "Has an area");

        var result = await Build(factory).Search(41.88, -87.63, ct: CancellationToken.None);
        var ok     = Assert.IsType<OkObjectResult>(result.Result);
        var list   = Assert.IsAssignableFrom<IEnumerable<OrgSearchResult>>(ok.Value).ToList();

        Assert.Single(list);
        Assert.Equal("Has an area", list[0].DisplayLabel);
    }

    [Fact]
    public async Task Search_OrgNotAcceptingClients_IsExcludedWhileAnAcceptingOneIsReturned()
    {
        var factory = TestDbFactory.Create();
        await SeedOrgWithAreaAsync(factory, isAcceptingClients: false, acceptsOutsideRange: false,
            centerLat: 41.88m, centerLon: -87.63m, radiusMiles: 50, displayLabel: "Closed");
        await SeedOrgWithAreaAsync(factory, isAcceptingClients: true, acceptsOutsideRange: false,
            centerLat: 41.88m, centerLon: -87.63m, radiusMiles: 50, displayLabel: "Open");

        var result = await Build(factory).Search(41.88, -87.63, ct: CancellationToken.None);
        var ok     = Assert.IsType<OkObjectResult>(result.Result);
        var list   = Assert.IsAssignableFrom<IEnumerable<OrgSearchResult>>(ok.Value).ToList();

        Assert.Single(list);
        Assert.Equal("Open", list[0].DisplayLabel);
    }

    // ── Paid promotion (item 194 / Ben's "promote the paid groups") ───────────

    /// <summary>Puts <paramref name="org"/> on a tier that excludes private-residence work.</summary>
    private static async Task OnFreeTierAsync(IDbContextFactory<BenDataContext> factory, Organization org)
    {
        await using var db = await factory.CreateDbContextAsync();
        var tierId = await db.SubscriptionTiers
            .Where(t => t.Name == "Free").Select(t => (Guid?)t.Id).FirstOrDefaultAsync();

        if (tierId is null)
        {
            tierId = Guid.NewGuid();
            db.SubscriptionTiers.Add(new Ben.Data.Source.Entities.SubscriptionTier
            {
                Id = tierId.Value, Name = "Free", MinMembers = 0, MaxMembers = 3,
                SortOrder = 1, IsActive = true,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = org.CreatedByAppUserId,
            });
            db.SubscriptionTiers.Add(new Ben.Data.Source.Entities.SubscriptionTier
            {
                Id = Guid.NewGuid(), Name = "Paid", MinMembers = 4, MaxMembers = null,
                SortOrder = 2, IsActive = true,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = org.CreatedByAppUserId,
            });
            db.SubscriptionTierExcludedCapabilities.Add(
                new Ben.Data.Source.Entities.SubscriptionTierExcludedCapability
                {
                    Id = Guid.NewGuid(), SubscriptionTierId = tierId.Value,
                    Capability = Ben.Data.Common.Enums.TierCapability.PrivateResidenceCases,
                    DateCreated = DateTime.UtcNow,
                });
        }

        db.OrganizationSubscriptions.Add(new Ben.Data.Source.Entities.OrganizationSubscription
        {
            Id = Guid.NewGuid(), OrganizationId = org.Id, SubscriptionTierId = tierId.Value,
            Status = Ben.Data.Common.Enums.SubscriptionStatus.Active,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = org.CreatedByAppUserId,
        });
        await db.SaveChangesAsync();
    }

    /// <summary>Inside one range bucket, a group that can take private work leads.</summary>
    [Fact]
    public async Task Search_WithinRange_PromotesGroupsThatTakePrivateWork()
    {
        var factory = TestDbFactory.Create();

        // The FREE group is nearer. Promotion should still put the paid one first, because both
        // are equally reachable and most people searching by address mean their own home.
        var near = await SeedOrgWithAreaAsync(factory, isAcceptingClients: true,
            acceptsOutsideRange: false, centerLat: 41.88m, centerLon: -87.63m, radiusMiles: 50);
        await OnFreeTierAsync(factory, near);
        await SeedOrgWithAreaAsync(factory, isAcceptingClients: true,
            acceptsOutsideRange: false, centerLat: 41.99m, centerLon: -87.63m, radiusMiles: 50);

        var ok   = Assert.IsType<OkObjectResult>(
            (await Build(factory).Search(41.88, -87.63, ct: CancellationToken.None)).Result);
        var list = Assert.IsAssignableFrom<IEnumerable<OrgSearchResult>>(ok.Value).ToList();

        Assert.Equal(2, list.Count);
        Assert.True(list[0].TakesPrivateResidenceCases);
        Assert.False(list[1].TakesPrivateResidenceCases);
    }

    /// <summary>
    /// Promotion never crosses the reachability line.
    /// </summary>
    /// <remarks>
    /// The boundary that keeps the directory honest: a free group that can actually reach the
    /// searcher outranks a paid group that cannot. Promoting across this line is the pay-to-win
    /// shape, and it would make the first result useless to the person who typed their address.
    /// </remarks>
    [Fact]
    public async Task Search_APaidGroupOutOfRangeStillLosesToAFreeGroupInRange()
    {
        var factory = TestDbFactory.Create();

        var inRangeFree = await SeedOrgWithAreaAsync(factory, isAcceptingClients: true,
            acceptsOutsideRange: false, centerLat: 41.88m, centerLon: -87.63m, radiusMiles: 50);
        await OnFreeTierAsync(factory, inRangeFree);

        // Paid, but far away and only reachable because it accepts outside its range.
        await SeedOrgWithAreaAsync(factory, isAcceptingClients: true,
            acceptsOutsideRange: true, centerLat: 44.98m, centerLon: -93.27m, radiusMiles: 25);

        var ok   = Assert.IsType<OkObjectResult>(
            (await Build(factory).Search(41.88, -87.63, ct: CancellationToken.None)).Result);
        var list = Assert.IsAssignableFrom<IEnumerable<OrgSearchResult>>(ok.Value).ToList();

        Assert.Equal(2, list.Count);
        Assert.True(list[0].IsWithinRange);
        Assert.False(list[0].TakesPrivateResidenceCases);   // the free one, and rightly first
        Assert.False(list[1].IsWithinRange);
    }
}
