using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// "Did you mean this place?" — the rule that decides whether an existing place is offered.
/// </summary>
/// <remarks>
/// <para>The rule is <b>the same address and less than a tenth of a mile apart</b>. Both halves
/// matter, and the tests are arranged so that dropping either half fails something: a same-address
/// pair a mile apart must not be offered, and a near-neighbour at a different address must not be
/// offered either.</para>
///
/// <para>Nothing here merges anything. The endpoint is read-only by design, so the cost of a false
/// positive is a suggestion somebody ignores — which is why the radius is generous rather than
/// tight.</para>
/// </remarks>
public class PlaceCandidateTests
{
    private static readonly Guid MeId = Guid.NewGuid();

    // Belmont Blvd, Nashville. The three-row duplicate this feature exists to stop.
    private const decimal BelmontLat = 36.0913m;
    private const decimal BelmontLon = -86.7930m;

    private static PlaceController Build(IDbContextFactory<BenDataContext> f)
        => new(f)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, MeId.ToString())], "Bearer"))
                }
            }
        };

    private static Place NewPlace(
        string? name = null,
        string? street = null,
        string? city = "Nashville",
        string? state = "TN",
        string? zip = "37215",
        decimal? lat = BelmontLat,
        decimal? lon = BelmontLon,
        PlaceKind kind = PlaceKind.PrivateResidence) => new()
        {
            Id = Guid.NewGuid(),
            Name = name,
            StreetAddress1 = street,
            City = city,
            State = state,
            ZipCode = zip,
            Latitude = lat,
            Longitude = lon,
            Kind = kind,
            DateCreated = DateTime.UtcNow,
            CreatedByAppUserId = MeId,
        };

    private static async Task<IDbContextFactory<BenDataContext>> SeedAsync(params Place[] places)
    {
        var factory = TestDbFactory.Create();
        await using var db = await factory.CreateDbContextAsync();
        db.Places.AddRange(places);
        await db.SaveChangesAsync();
        return factory;
    }

    private static async Task<IReadOnlyList<PlaceCandidate>> FindAsync(
        IDbContextFactory<BenDataContext> f,
        string? street = null, string? city = "Nashville", string? state = "TN", string? zip = "37215",
        string? name = null, decimal? lat = BelmontLat, decimal? lon = BelmontLon)
    {
        var result = await Build(f).FindCandidates(street, city, state, zip, name, lat, lon, default);
        var value = Assert.IsType<OkObjectResult>(result.Result).Value;
        return Assert.IsAssignableFrom<IEnumerable<PlaceCandidate>>(value).ToList();
    }

    // ── The address half ──────────────────────────────────────────────────────

    [Fact]
    public async Task The_same_address_at_the_same_spot_is_offered()
    {
        var f = await SeedAsync(NewPlace(street: "4512 Belmont Blvd"));

        var candidates = await FindAsync(f, street: "4512 Belmont Blvd");

        // The exact case from the dev database: the backfill gave three cases at this address three
        // separate places, because merging during a migration would have been a silent guess.
        Assert.Equal("4512 Belmont Blvd", Assert.Single(candidates).StreetAddress1);
    }

    [Theory]
    [InlineData("4512 belmont blvd.")]
    [InlineData("  4512   Belmont   Blvd  ")]
    [InlineData("4512 Belmont Blvd,")]
    public async Task Punctuation_spacing_and_case_do_not_make_it_a_different_address(string typed)
    {
        var f = await SeedAsync(NewPlace(street: "4512 Belmont Blvd"));

        Assert.Single(await FindAsync(f, street: typed));
    }

    [Fact]
    public async Task A_different_address_next_door_is_not_offered()
    {
        var f = await SeedAsync(NewPlace(street: "4514 Belmont Blvd"));

        // Same street, same coordinates to four decimal places — and still a different house.
        // Proximity alone would merge a whole terrace.
        Assert.Empty(await FindAsync(f, street: "4512 Belmont Blvd"));
    }

    [Fact]
    public async Task A_matching_address_in_another_town_is_not_offered()
    {
        // Same street text, genuinely elsewhere: Belmont Blvd in Nashville against one in Memphis,
        // about 200 miles away.
        var f = await SeedAsync(NewPlace(
            street: "4512 Belmont Blvd", city: "Memphis", zip: "38104",
            lat: 35.1495m, lon: -90.0490m));

        Assert.Empty(await FindAsync(f, street: "4512 Belmont Blvd"));
    }

    [Fact]
    public async Task The_same_address_beyond_a_tenth_of_a_mile_is_not_offered()
    {
        // ~0.35 miles north. Far enough that two records claiming one address are describing two
        // different buildings, whatever the text says.
        var f = await SeedAsync(NewPlace(street: "4512 Belmont Blvd", lat: BelmontLat + 0.005m));

        Assert.Empty(await FindAsync(f, street: "4512 Belmont Blvd"));
    }

    [Fact]
    public async Task A_geocoder_disagreeing_by_a_building_or_two_is_still_the_same_place()
    {
        // ~0.02 miles. Two geocoders on one address routinely land this far apart; a radius tight
        // enough to reject this would reject most real duplicates.
        var f = await SeedAsync(NewPlace(street: "4512 Belmont Blvd", lat: BelmontLat + 0.0003m));

        Assert.Single(await FindAsync(f, street: "4512 Belmont Blvd"));
    }

    [Fact]
    public async Task A_hotel_or_apartment_block_offers_the_existing_row()
    {
        // One address, many units, one set of coordinates. Ben's call: offer it. The person
        // entering room 402 is the only one who knows whether the building already on file is
        // theirs, and they are being shown a suggestion, not having it applied.
        var f = await SeedAsync(NewPlace(name: "Union Station Hotel", street: "1001 Broadway"));

        var candidate = Assert.Single(await FindAsync(f, street: "1001 Broadway"));
        Assert.Equal("Union Station Hotel", candidate.Name);
    }

    // ── The name half, for places that have no street address ─────────────────

    [Fact]
    public async Task A_landmark_matches_on_its_name_when_there_is_no_address()
    {
        var f = await SeedAsync(NewPlace(
            name: "Bell Witch Cave", city: "Adams", zip: null,
            lat: 36.5893m, lon: -87.0625m, kind: PlaceKind.PublicLocation));

        var candidates = await FindAsync(
            f, name: "The Bell Witch Cave", city: "Adams", zip: null,
            lat: 36.5893m, lon: -87.0625m);

        // "The Bell Witch Cave" against "Bell Witch Cave" is the duplicate this codebase actually
        // produced, from inline creation with no lookup.
        Assert.Equal("Bell Witch Cave", Assert.Single(candidates).Name);
    }

    [Fact]
    public async Task A_landmark_with_a_different_name_at_the_same_spot_is_not_offered()
    {
        var f = await SeedAsync(NewPlace(
            name: "Bell Witch Cabin", city: "Adams", zip: null,
            lat: 36.5893m, lon: -87.0625m, kind: PlaceKind.PublicLocation));

        // Cave and cabin sit on the same property and are two places to anyone who has been there.
        Assert.Empty(await FindAsync(
            f, name: "Bell Witch Cave", city: "Adams", zip: null, lat: 36.5893m, lon: -87.0625m));
    }

    [Fact]
    public async Task A_matching_name_a_mile_away_is_not_offered()
    {
        var f = await SeedAsync(NewPlace(
            name: "Bell Witch Cave", city: "Adams", zip: null,
            lat: 36.6100m, lon: -87.0625m, kind: PlaceKind.PublicLocation));

        Assert.Empty(await FindAsync(
            f, name: "Bell Witch Cave", city: "Adams", zip: null, lat: 36.5893m, lon: -87.0625m));
    }

    // ── Missing information ───────────────────────────────────────────────────

    [Fact]
    public async Task A_place_nobody_could_geocode_is_still_offered()
    {
        var f = await SeedAsync(NewPlace(street: "4512 Belmont Blvd", lat: null, lon: null));

        // Unknown coordinates mean the distance test cannot answer, and "no opinion" has to fall
        // towards offering: a place that would not geocode is exactly the one that gets typed in
        // twice. The address still has to match.
        Assert.Single(await FindAsync(f, street: "4512 Belmont Blvd"));
    }

    [Fact]
    public async Task A_row_missing_its_city_still_matches_on_the_address()
    {
        var f = await SeedAsync(NewPlace(street: "4512 Belmont Blvd", city: null, zip: null));

        // Half the rows came from a backfill and are missing a field or two. Demanding a full match
        // on every part would find none of the duplicates that actually exist.
        Assert.Single(await FindAsync(f, street: "4512 Belmont Blvd"));
    }

    [Fact]
    public async Task Nothing_to_go_on_returns_nothing()
    {
        var f = await SeedAsync(NewPlace(street: "4512 Belmont Blvd"));

        // No address and no name is somebody who has typed nothing yet. Returning every place
        // within a tenth of a mile would be a suggestion list they cannot act on.
        Assert.Empty(await FindAsync(f, street: null, name: null));
    }

    // ── What comes back ───────────────────────────────────────────────────────

    [Fact]
    public async Task Candidates_come_back_nearest_first()
    {
        var near = NewPlace(street: "4512 Belmont Blvd", lat: BelmontLat + 0.0001m);
        var far = NewPlace(street: "4512 Belmont Blvd", lat: BelmontLat + 0.0010m);
        var f = await SeedAsync(far, near);

        var candidates = await FindAsync(f, street: "4512 Belmont Blvd");

        Assert.Equal(2, candidates.Count);
        Assert.Equal(near.Id, candidates[0].Id);
    }

    [Fact]
    public async Task Each_candidate_carries_how_much_has_happened_there()
    {
        var place = NewPlace(street: "4512 Belmont Blvd");
        var f = await SeedAsync(place);

        await using (var db = await f.CreateDbContextAsync())
        {
            var orgId = Guid.NewGuid();
            db.Organizations.Add(new Organization
            { Id = orgId, Name = "Mine", UrlName = "mine", DateCreated = DateTime.UtcNow });
            db.Investigations.Add(new Investigation
            {
                Id = Guid.NewGuid(), OrganizationId = orgId, PlaceId = place.Id,
                Title = "First visit", ScheduledDateTime = DateTime.UtcNow.AddYears(-1),
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = MeId,
            });
            await db.SaveChangesAsync();
        }

        var candidate = Assert.Single(await FindAsync(f, street: "4512 Belmont Blvd"));

        // A place with history behind it is almost certainly the one meant; a bare row with nothing
        // attached usually is not. That distinction is the whole reason to show a count.
        Assert.Equal(1, candidate.InvestigationCount);
        Assert.NotNull(candidate.DistanceMiles);
    }

    [Fact]
    public async Task Distance_is_reported_as_unknown_rather_than_zero()
    {
        var f = await SeedAsync(NewPlace(street: "4512 Belmont Blvd", lat: null, lon: null));

        // Zero would read as "right on top of you", which is the opposite of what an ungeocoded
        // row means.
        Assert.Null(Assert.Single(await FindAsync(f, street: "4512 Belmont Blvd")).DistanceMiles);
    }
}
