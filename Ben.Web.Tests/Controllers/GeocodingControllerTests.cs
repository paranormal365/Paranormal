using Ben.Data.WebApi.Controllers;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Tests for <see cref="GeocodingController"/> — covers validation/early-exit
/// paths that don't require an actual Nominatim HTTP call.
/// Integration tests for the Nominatim HTTP layer are out of scope for unit tests.
/// </summary>
public class GeocodingControllerTests
{
    private static GeocodingController Build() => new();

    // ── Preview — validation paths ────────────────────────────────────────────

    [Fact]
    public async Task Preview_WhenStreetAddressIsEmpty_ReturnsOkWithNullCoords()
    {
        var ctrl   = Build();
        var result = await ctrl.Preview(
            streetAddress1: "",    // blank — should short-circuit
            streetAddress2: null,
            city:           "Austin",
            state:          "TX",
            zipCode:        "78701",
            country:        "US",
            ct:             CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var r  = Assert.IsType<GeocodingPreviewResponse>(ok.Value);
        Assert.Null(r.Latitude);
        Assert.Null(r.Longitude);
    }

    [Fact]
    public async Task Preview_WhenCityIsEmpty_ReturnsOkWithNullCoords()
    {
        var ctrl   = Build();
        var result = await ctrl.Preview(
            streetAddress1: "123 Main St",
            streetAddress2: null,
            city:           "",   // blank
            state:          "TX",
            zipCode:        "78701",
            country:        "US",
            ct:             CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var r  = Assert.IsType<GeocodingPreviewResponse>(ok.Value);
        Assert.Null(r.Latitude);
        Assert.Null(r.Longitude);
    }

    [Theory]
    [InlineData("", "Austin", "TX", "78701", "US")]
    [InlineData("123 Main", "", "TX", "78701", "US")]
    [InlineData("123 Main", "Austin", "", "78701", "US")]
    [InlineData("123 Main", "Austin", "TX", "78701", "")]
    public async Task Preview_WhenAnyRequiredFieldBlank_ReturnsNullCoords(
        string street, string city, string state, string zip, string country)
    {
        var ctrl   = Build();
        var result = await ctrl.Preview(street, null, city, state, zip, country, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var r  = Assert.IsType<GeocodingPreviewResponse>(ok.Value);
        Assert.Null(r.Latitude);
        Assert.Null(r.Longitude);
    }

    // ── Reverse — validation paths ────────────────────────────────────────────

    [Fact]
    public async Task Reverse_ReturnsOkResponse()
    {
        // The actual Nominatim call will fail in unit tests (no network), but
        // the controller should return an OkObjectResult even when geocoding
        // returns Empty (which happens on network failure — silently caught).
        var ctrl   = Build();
        var result = await ctrl.Reverse(
            latitude:  30.2672,
            longitude: -97.7431,
            ct:        CancellationToken.None);

        // Should always return 200 — geocoding errors are swallowed
        Assert.IsType<OkObjectResult>(result.Result);
    }
}
