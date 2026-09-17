using System.Net;
using Ben.Canvas.Core.Options;
using Ben.Canvas.Editor.Extensions;
using Ben.Canvas.Editor.Services;
using Ben.Canvas.Tests.Support;

namespace Ben.Canvas.Tests.Services;

/// <summary>
/// Looking an address up: the address asked for, and what each answer means.
/// </summary>
/// <remarks>
/// The URL is asserted because getting it wrong is silent. This asked for <c>/geocode/search</c>
/// instead of <c>/api/geocode/search</c> and every address came back "not found" from a server that
/// had the endpoint all along — a bug only driving the board found (2026-09-17).
/// </remarks>
public sealed class CanvasGeocoderTests
{
    private const string Api = "http://localhost:5252";

    private sealed class OneClient(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            Assert.Equal(CanvasEditorServiceCollectionExtensions.PublicHttpClientName, name);
            return new HttpClient(handler, disposeHandler: false);
        }
    }

    private static CanvasGeocoder Geocoder(HttpMessageHandler handler, string? api = Api) =>
        new(new OneClient(handler), Microsoft.Extensions.Options.Options.Create(
            new CanvasEditorOptions { ApiBaseUrl = api }));

    [Fact]
    public async Task A_found_address_comes_back_with_a_zoom_to_match_how_exactly_it_was_found()
    {
        var api = new StubHandler(HttpStatusCode.OK,
            """{"latitude":35.9249733,"longitude":-86.8654503,"resultType":"rooftop"}""");

        var found = await Geocoder(api).FindAsync("12A Church Street, Franklin, TN");

        Assert.NotNull(found);
        Assert.Equal(35.9249733, found!.Latitude, 5);
        Assert.Equal(17, found.Zoom);
        // The path is the point: /geocode/search without /api is a 404 from a server that has it.
        // Escaping is checked by what survives — a recorded URL renders %20 back as a space.
        Assert.StartsWith($"{Api}/api/geocode/search?q=", api.LastUrl);
        Assert.Contains("12A Church Street%2C Franklin%2C TN", api.LastUrl);
    }

    /// <summary>A town is a shape: a street-level zoom on one shows an arbitrary corner of it.</summary>
    [Theory]
    [InlineData("rooftop", 17)]
    [InlineData("street", 16)]
    [InlineData("place", 14)]
    [InlineData("something-new", 13)]
    public async Task How_close_to_look_follows_how_exactly_the_place_was_found(string kind, double zoom)
    {
        var api = new StubHandler(HttpStatusCode.OK, $"{{\"latitude\":1.0,\"longitude\":2.0,\"resultType\":\"{kind}\"}}");

        Assert.Equal(zoom, (await Geocoder(api).FindAsync("somewhere"))!.Zoom);
    }

    /// <summary>The endpoint answers 200 with nulls for an address it cannot place.</summary>
    [Fact]
    public async Task An_address_the_geocoder_does_not_know_is_null_not_a_place_at_zero_zero() =>
        Assert.Null(await Geocoder(new StubHandler(HttpStatusCode.OK,
            """{"latitude":null,"longitude":null,"resultType":null}""")).FindAsync("nowhere at all"));

    [Fact]
    public async Task Nothing_is_asked_without_an_address_or_without_an_api()
    {
        var api = new StubHandler(HttpStatusCode.OK, """{"latitude":1.0,"longitude":2.0,"resultType":"place"}""");

        Assert.Null(await Geocoder(api).FindAsync("   "));
        Assert.Null(await Geocoder(api).FindAsync(null));
        Assert.Empty(api.Requests);

        Assert.Null(await Geocoder(api, api: null).FindAsync("Nashville, TN"));
        Assert.Empty(api.Requests);
    }

    [Fact]
    public async Task A_server_that_refuses_or_cannot_be_reached_is_null_rather_than_a_throw()
    {
        Assert.Null(await Geocoder(new StubHandler(HttpStatusCode.TooManyRequests, "")).FindAsync("Nashville, TN"));
        Assert.Null(await Geocoder(new StubHandler(HttpStatusCode.OK, "<html>not json</html>")).FindAsync("Nashville, TN"));
    }
}
