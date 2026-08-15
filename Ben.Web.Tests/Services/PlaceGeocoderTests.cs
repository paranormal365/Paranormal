using Ben.Data.Common.Enums;
using Ben.Data.Source.Entities;
using Ben.Service.RepositoryService.Services;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// What a <see cref="Place"/> ends up with after a geocoding attempt.
/// </summary>
/// <remarks>
/// The rule under test throughout: a place that could not be located says so. Silent null
/// coordinates are indistinguishable from a place nobody has looked at yet, so every failure path
/// has to leave a note behind, and every success path has to clear one.
/// </remarks>
public class PlaceGeocoderTests
{
    private static Place FullAddress() => new()
    {
        Id = Guid.NewGuid(),
        StreetAddress1 = "1 Nowhere Rd",
        City = "Nashville",
        State = "TN",
        ZipCode = "37201",
        Country = "US",
        Kind = PlaceKind.PrivateResidence,
    };

    private static AddressGeocodingService.GeocodingLookupResult Found(decimal lat, decimal lon)
        => new(lat, lon, "{}", "rooftop");

    private static AddressGeocodingService.GeocodingLookupResult NotFound
        => AddressGeocodingService.GeocodingLookupResult.Empty;

    [Fact]
    public void A_successful_lookup_writes_coordinates_and_a_timestamp()
    {
        var place = FullAddress();

        PlaceGeocoder.Apply(place, Found(36.1627m, -86.7816m));

        Assert.Equal(36.1627m, place.Latitude);
        Assert.Equal(-86.7816m, place.Longitude);
        Assert.NotNull(place.DateGeocoded);
        Assert.Null(place.GeocodeNote);
    }

    [Fact]
    public void A_failed_lookup_leaves_a_note_rather_than_silent_nulls()
    {
        var place = FullAddress();

        PlaceGeocoder.Apply(place, NotFound);

        Assert.Null(place.Latitude);
        Assert.Null(place.Longitude);
        // The note is the whole point of the class — this is the assertion that matters.
        Assert.False(string.IsNullOrWhiteSpace(place.GeocodeNote));
        Assert.Null(place.DateGeocoded);
    }

    [Fact]
    public void A_later_success_clears_an_earlier_failure_note()
    {
        var place = FullAddress();
        PlaceGeocoder.Apply(place, NotFound);
        Assert.NotNull(place.GeocodeNote);

        PlaceGeocoder.Apply(place, Found(36m, -86m));

        // A stale note beside real coordinates is a lie the UI would faithfully render.
        Assert.Null(place.GeocodeNote);
    }

    [Fact]
    public void A_failed_lookup_does_not_discard_coordinates_it_already_had()
    {
        var place = FullAddress();
        PlaceGeocoder.Apply(place, Found(36.1627m, -86.7816m));
        var geocodedAt = place.DateGeocoded;

        PlaceGeocoder.Apply(place, NotFound);

        // A transient failure must not destroy a good answer from last week.
        Assert.Equal(36.1627m, place.Latitude);
        Assert.Equal(-86.7816m, place.Longitude);
        Assert.Equal(geocodedAt, place.DateGeocoded);
        Assert.NotNull(place.GeocodeNote);
    }

    [Fact]
    public void A_place_with_nothing_to_look_up_is_told_so_specifically()
    {
        var place = new Place { Id = Guid.NewGuid() };

        PlaceGeocoder.Apply(place, NotFound);

        // Distinct from "we looked and could not find it" — the fix is a different one, so the
        // sentence has to be different too.
        Assert.Contains("No address or name", place.GeocodeNote);
    }

    [Fact]
    public void A_partial_address_is_told_what_is_missing_not_that_it_was_not_found()
    {
        var place = new Place { Id = Guid.NewGuid(), Name = "The Bell Witch Cave", City = "Adams" };

        PlaceGeocoder.Apply(place, NotFound);

        Assert.Contains("not enough of an address", place.GeocodeNote);
    }

    [Fact]
    public void A_complete_address_that_cannot_be_found_is_told_to_check_for_typos()
    {
        var place = FullAddress();

        PlaceGeocoder.Apply(place, NotFound);

        Assert.Contains("could not be found", place.GeocodeNote);
    }

    [Fact]
    public async Task Client_supplied_coordinates_are_trusted_without_a_lookup()
    {
        var place = FullAddress();
        place.Latitude = 36.1627m;
        place.Longitude = -86.7816m;
        place.GeocodeNote = "stale note from an earlier failure";

        await PlaceGeocoder.GeocodeAsync(place, trustSuppliedCoordinates: true);

        Assert.Equal(36.1627m, place.Latitude);
        Assert.Null(place.GeocodeNote);
        Assert.NotNull(place.DateGeocoded);
    }

    [Fact]
    public async Task With_no_geocoder_configured_a_place_still_ends_up_coherent()
    {
        // No API key is set under test, so the service returns Empty without a network call. This
        // is the ordinary dev-environment path, and it must produce an explained place rather than
        // an exception or a silently blank one.
        var place = FullAddress();

        await PlaceGeocoder.GeocodeAsync(place);

        Assert.Null(place.Latitude);
        Assert.NotNull(place.GeocodeNote);
        Assert.Null(place.DateGeocoded);
    }
}
