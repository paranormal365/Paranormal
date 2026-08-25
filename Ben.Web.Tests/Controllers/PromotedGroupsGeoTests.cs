using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Public;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Geo-fed promotion (item 186 F8): nearest-first ordering from PUBLIC addresses only, and
/// counters that move on serve and on click.
/// </summary>
/// <remarks>
/// The privacy claim carries this suite: a group whose only location is an area-of-operation
/// circle gets NO distance, ever — the circle's centre exists to hide where a home-based group
/// is, and a published "12.3 miles from you" derived from it would leak exactly what it hides.
/// </remarks>
public sealed class PromotedGroupsGeoTests
{
    private sealed class SimpleFactory(DbContextOptions<BenDataContext> opts) : IDbContextFactory<BenDataContext>
    {
        public BenDataContext CreateDbContext() => new(opts);
        public Task<BenDataContext> CreateDbContextAsync(CancellationToken ct = default)
            => Task.FromResult(new BenDataContext(opts));
    }

    // Nashville-ish viewer.
    private const double ViewerLat = 36.16, ViewerLon = -86.78;

    private static async Task<(IDbContextFactory<BenDataContext> Factory, Guid NearAd, Guid FarAd, Guid HiddenAd)>
        SeedAsync()
    {
        var factory = new SimpleFactory(new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        await using var db = await ((IDbContextFactory<BenDataContext>)factory).CreateDbContextAsync();

        var userId = Guid.NewGuid();
        Guid nearOrg = Guid.NewGuid(), farOrg = Guid.NewGuid(), hiddenOrg = Guid.NewGuid();
        Guid nearAd = Guid.NewGuid(), farAd = Guid.NewGuid(), hiddenAd = Guid.NewGuid();

        db.Organizations.AddRange(
            new Organization { Id = nearOrg, Name = "Near Org", UrlName = "near-org", DateCreated = DateTime.UtcNow },
            new Organization { Id = farOrg, Name = "Far Org", UrlName = "far-org", DateCreated = DateTime.UtcNow },
            new Organization { Id = hiddenOrg, Name = "Hidden Org", UrlName = "hidden-org", DateCreated = DateTime.UtcNow });

        // Near: a public searchable address ~1 mile away. Far: ~180 miles (Memphis-ish).
        db.OrganizationAddresses.AddRange(
            new OrganizationAddress
            {
                Id = Guid.NewGuid(), OrganizationId = nearOrg,
                StreetAddress1 = "1 Near St", City = "Nashville", State = "TN",
                Latitude = 36.17m, Longitude = -86.78m,
                IsSearchable = true, SearchVisibility = OrganizationAddressVisibility.Public,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
            },
            new OrganizationAddress
            {
                Id = Guid.NewGuid(), OrganizationId = farOrg,
                StreetAddress1 = "1 Far St", City = "Memphis", State = "TN",
                Latitude = 35.15m, Longitude = -90.05m,
                IsSearchable = true, SearchVisibility = OrganizationAddressVisibility.Public,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
            },
            // The hidden org HAS an address on file — closer than anybody — but it is not
            // publicly searchable, so it must contribute nothing.
            new OrganizationAddress
            {
                Id = Guid.NewGuid(), OrganizationId = hiddenOrg,
                StreetAddress1 = "1 Secret St", City = "Nashville", State = "TN",
                Latitude = 36.16m, Longitude = -86.78m,
                IsSearchable = false, SearchVisibility = OrganizationAddressVisibility.Private,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
            });

        foreach (var (adId, orgId, name) in new[]
                 { (nearAd, nearOrg, "Near"), (farAd, farOrg, "Far"), (hiddenAd, hiddenOrg, "Hidden") })
            db.OrganizationAds.Add(new OrganizationAd
            {
                Id = adId, OrganizationId = orgId, Headline = name, Body = "x",
                Status = OrganizationAdStatus.Approved,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
            });

        await db.SaveChangesAsync();
        return (factory, nearAd, farAd, hiddenAd);
    }

    private static PublicPromotedGroupsController Build(IDbContextFactory<BenDataContext> factory)
        => new(factory, new Mock<IFileStorageService>().Object);

    private static async Task<List<PromotedGroupCard>> GetCardsAsync(
        IDbContextFactory<BenDataContext> factory, double? lat = null, double? lon = null)
    {
        var result = await Build(factory).Get(10, lat, lon, default);
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        return ((IEnumerable<PromotedGroupCard>)ok.Value!).ToList();
    }

    [Fact]
    public async Task Located_groups_come_nearest_first_and_unlocated_after()
    {
        var (factory, nearAd, farAd, hiddenAd) = await SeedAsync();
        var cards = await GetCardsAsync(factory, ViewerLat, ViewerLon);

        Assert.Equal(3, cards.Count);
        Assert.Equal(nearAd, cards[0].AdId);
        Assert.Equal(farAd, cards[1].AdId);
        Assert.Equal(hiddenAd, cards[2].AdId); // still served — unlocatable, not unseen

        Assert.True(cards[0].DistanceMiles < 5);
        Assert.True(cards[1].DistanceMiles > 100);
    }

    [Fact]
    public async Task A_non_public_address_contributes_no_distance_even_when_nearest()
    {
        var (factory, _, _, hiddenAd) = await SeedAsync();
        var cards = await GetCardsAsync(factory, ViewerLat, ViewerLon);
        Assert.Null(cards.Single(c => c.AdId == hiddenAd).DistanceMiles);
    }

    [Fact]
    public async Task Without_coordinates_no_distance_is_published_at_all()
    {
        var (factory, _, _, _) = await SeedAsync();
        var cards = await GetCardsAsync(factory);
        Assert.All(cards, c => Assert.Null(c.DistanceMiles));
    }

    [Fact]
    public async Task Serving_bumps_impressions_for_exactly_the_served_ads()
    {
        var (factory, nearAd, _, _) = await SeedAsync();
        _ = await GetCardsAsync(factory, ViewerLat, ViewerLon);
        _ = await GetCardsAsync(factory, ViewerLat, ViewerLon);

        await using var db = await factory.CreateDbContextAsync();
        var near = await db.OrganizationAds.SingleAsync(a => a.Id == nearAd);
        Assert.Equal(2, near.Impressions);
        Assert.Equal(0, near.Clicks);
    }

    [Fact]
    public async Task Click_counts_and_answers_the_closed_set_target()
    {
        var (factory, nearAd, _, _) = await SeedAsync();
        var result = await Build(factory).Click(nearAd, default);
        var target = (PromotedClickTarget)Assert.IsType<OkObjectResult>(result.Result).Value!;

        Assert.Equal("org", target.TargetKind);
        Assert.Equal("near-org", target.OrganizationUrlName);

        await using var db = await factory.CreateDbContextAsync();
        Assert.Equal(1, (await db.OrganizationAds.SingleAsync(a => a.Id == nearAd)).Clicks);
    }

    [Fact]
    public async Task Clicking_an_unapproved_ad_counts_nothing_and_reveals_nothing()
    {
        var (factory, nearAd, _, _) = await SeedAsync();
        await using (var db = await factory.CreateDbContextAsync())
        {
            (await db.OrganizationAds.SingleAsync(a => a.Id == nearAd)).Status = OrganizationAdStatus.Rejected;
            await db.SaveChangesAsync();
        }

        Assert.IsType<NotFoundResult>((await Build(factory).Click(nearAd, default)).Result);
        await using var check = await factory.CreateDbContextAsync();
        Assert.Equal(0, (await check.OrganizationAds.SingleAsync(a => a.Id == nearAd)).Clicks);
    }
}
