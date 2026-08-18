using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Public;
using Ben.Service.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// "What is near me" — groups and public events around a point (backlog item #88).
/// </summary>
/// <remarks>
/// <para>The endpoint existed, honoured every per-address privacy setting, and <b>nothing called
/// it</b>. It was extended rather than replaced; writing a third nearby implementation was the one
/// outcome the backlog entry warned against.</para>
///
/// <para><b>The asymmetry is the whole test file.</b> An organization that ticked "searchable" is a
/// business listing and appears as precisely as it chose. A public event is an invitation and is
/// approximate until somebody attends. Applying the redaction uniformly — the obvious instinct
/// having just built coordinate snapping — would break discovery rather than protect anyone.</para>
/// </remarks>
public sealed class NearbySearchTests
{
    // Downtown Nashville, and a true event location a few blocks away.
    private const double CallerLat = 36.1627, CallerLon = -86.7816;
    private const decimal EventLat = 36.1650m, EventLon = -86.7840m;

    private static readonly Guid OrgId  = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    private static IDbContextFactory<BenDataContext> CreateFactory()
        => new PooledDbContextFactory<BenDataContext>(
            new DbContextOptionsBuilder<BenDataContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static async Task<IDbContextFactory<BenDataContext>> SeedAsync(
        bool eventIsPublic = true,
        PlaceKind placeKind = PlaceKind.PublicLocation,
        bool inThePast = false)
    {
        var factory = CreateFactory();
        await using var db = await factory.CreateDbContextAsync();

        db.Organizations.Add(new Organization
        {
            Id = OrgId, Name = "Ghost Squad", UrlName = "ghost-squad",
            DateCreated = DateTime.UtcNow,
        });

        // A searchable org address: a business listing, shown as precisely as it chose.
        db.OrganizationAddresses.Add(new OrganizationAddress
        {
            Id = Guid.NewGuid(), OrganizationId = OrgId,
            StreetAddress1 = "100 Broadway", City = "Nashville", State = "TN",
            Latitude = (decimal)CallerLat, Longitude = (decimal)CallerLon,
            IsSearchable = true,
            SearchVisibility = OrganizationAddressVisibility.Public,
            PublicDisplayMode = OrganizationAddressDisplayMode.FullAddressAndMap,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = UserId,
        });

        var placeId = Guid.NewGuid();
        db.Places.Add(new Place
        {
            Id = placeId, Name = "The Old Mill", Kind = placeKind,
            StreetAddress1 = "42 Elm Street", City = "Nashville", State = "TN",
            Latitude = EventLat, Longitude = EventLon,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = UserId,
        });

        var start = inThePast ? DateTime.UtcNow.AddDays(-10) : DateTime.UtcNow.AddDays(10);
        db.OrgCalendarEvents.Add(new OrgCalendarEvent
        {
            Id = Guid.NewGuid(), OrganizationId = OrgId, Title = "Ghost Walk",
            UrlName = "2026-08-27-ghost-walk", PlaceId = placeId,
            StartDateTime = start, EndDateTime = start.AddHours(3),
            IsPublic = eventIsPublic,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = UserId,
        });

        await db.SaveChangesAsync();
        return factory;
    }

    private static async Task<NearbyResults> SearchAsync(
        IDbContextFactory<BenDataContext> factory, double radius = 25, string? query = null)
    {
        var result = await new SearchController(factory).Nearby(CallerLat, CallerLon, radius, query, default);
        return Assert.IsType<NearbyResults>(
            Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(result.Result).Value);
    }

    // ── The ordinary case ────────────────────────────────────────────────────

    [Fact]
    public async Task A_searchable_group_is_found()
    {
        var results = await SearchAsync(await SeedAsync());

        Assert.Equal("Ghost Squad", Assert.Single(results.Organizations).OrgName);
    }

    [Fact]
    public async Task An_upcoming_public_event_is_found()
    {
        var results = await SearchAsync(await SeedAsync());

        var ev = Assert.Single(results.Events);
        Assert.Equal("Ghost Walk", ev.Title);
        Assert.Equal("ghost-squad", ev.OrgUrlName);
        Assert.False(string.IsNullOrWhiteSpace(ev.UrlName));
    }

    /// <summary>The town is published, because "near Nashville" is the useful part.</summary>
    [Fact]
    public async Task An_event_says_which_town_it_is_in()
    {
        var ev = Assert.Single((await SearchAsync(await SeedAsync())).Events);

        Assert.Equal("Nashville", ev.City);
        Assert.Equal("TN", ev.State);
    }

    // ── The asymmetry ────────────────────────────────────────────────────────

    /// <summary>
    /// A group that asked to be findable is shown where it actually is. Snapping this would defeat
    /// the feature — an organization that cannot be found has been broken, not protected.
    /// </summary>
    [Fact]
    public async Task A_group_is_shown_at_its_real_position()
    {
        var org = Assert.Single((await SearchAsync(await SeedAsync())).Organizations);

        Assert.Equal((decimal)CallerLat, org.Latitude);
        Assert.Equal((decimal)CallerLon, org.Longitude);
        Assert.Equal("100 Broadway", org.StreetAddress1);
    }

    /// <summary>An event is not, because an invitation is not a listing.</summary>
    [Fact]
    public async Task An_event_is_never_shown_at_its_real_position()
    {
        var ev = Assert.Single((await SearchAsync(await SeedAsync())).Events);

        Assert.NotNull(ev.Latitude);
        Assert.NotEqual(EventLat, ev.Latitude);
        Assert.NotEqual(EventLon, ev.Longitude);

        // Still genuinely nearby — an approximation that lands in another state is a bug, not a
        // redaction.
        Assert.True(Math.Abs(ev.Latitude!.Value - EventLat) < 1m);
    }

    /// <summary>
    /// The published distance is measured to the snapped point, so it cannot be used to solve for
    /// the real one. Two events in the same grid cell are the same distance away.
    /// </summary>
    [Fact]
    public async Task The_reported_distance_agrees_with_the_published_position()
    {
        var ev = Assert.Single((await SearchAsync(await SeedAsync())).Events);

        var fromPublished = Haversine(
            CallerLat, CallerLon, (double)ev.Latitude!.Value, (double)ev.Longitude!.Value);

        Assert.Equal(Math.Round(fromPublished, 1), ev.DistanceMiles, 1);
    }

    /// <summary>No field on an event result can carry a street address.</summary>
    [Fact]
    public void An_event_result_has_no_room_for_an_exact_address()
    {
        var names = typeof(NearbyEventResult).GetProperties().Select(p => p.Name).ToList();

        Assert.DoesNotContain(names, n => n.Contains("Street", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, n => n.Contains("Address", StringComparison.OrdinalIgnoreCase));
    }

    // ── What it leaves out ───────────────────────────────────────────────────

    [Fact]
    public async Task A_private_event_is_not_listed()
        => Assert.Empty((await SearchAsync(await SeedAsync(eventIsPublic: false))).Events);

    /// <summary>
    /// An event at somebody's home is not listed even if marked public — the same rule the events
    /// pages enforce, reused rather than restated.
    /// </summary>
    [Fact]
    public async Task An_event_at_a_private_residence_is_not_listed()
        => Assert.Empty((await SearchAsync(await SeedAsync(placeKind: PlaceKind.PrivateResidence))).Events);

    [Fact]
    public async Task A_past_event_is_not_listed()
        => Assert.Empty((await SearchAsync(await SeedAsync(inThePast: true))).Events);

    [Fact]
    public async Task Nothing_outside_the_radius_is_listed()
    {
        var results = await SearchAsync(await SeedAsync(), radius: 0.1);

        Assert.Empty(results.Events);
    }

    /// <summary>A text query filters both lists rather than only one.</summary>
    [Fact]
    public async Task A_query_that_matches_nothing_returns_nothing()
    {
        var results = await SearchAsync(await SeedAsync(), query: "zzz-no-such-thing");

        Assert.Empty(results.Organizations);
        Assert.Empty(results.Events);
    }

    [Fact]
    public async Task A_query_matching_the_event_title_finds_it()
        => Assert.Single((await SearchAsync(await SeedAsync(), query: "ghost walk")).Events);

    private static double Haversine(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 3958.8;
        var dLat = (lat2 - lat1) * Math.PI / 180;
        var dLon = (lon2 - lon1) * Math.PI / 180;
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
              + Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180)
              * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return R * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }
}
