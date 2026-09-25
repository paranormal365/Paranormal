using System.Globalization;
using Ben.Canvas.Core.Model;
using Ben.Canvas.Core.Options;
using Ben.Canvas.Editor.Components.Nodes;
using Ben.Canvas.Editor.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Ben.Canvas.Tests.Components;

/// <summary>
/// Map boxes are visual (R35): a still picture from the site's signer when there is one, in both schemes so the
/// theme picks, never stored and never MapKit at rest; always an Open in Apple Maps link; the address card only
/// when no picture can be had.
/// </summary>
public sealed class MapBoxTests
{
    private const string Signer = "https://ishaunted.test/auth/mapkit-snapshot";

    private static CanvasNode Map(double lat = 36.1612, double lng = -86.7717, string? address = "Shelby Street Bridge") =>
        new() { Type = CanvasNodeType.Map, Width = 360, Height = 260, Data = new MapData { Latitude = lat, Longitude = lng, Address = address } };

    private static Task<string> RenderAsync(CanvasNode node, string? signer, string? token = null) =>
        RenderHelper.RenderAsync<MapNode>(new Dictionary<string, object?> { ["Node"] = node, ["Data"] = node.Data },
            services => services.Configure<CanvasEditorOptions>(o => { o.MapSnapshotUrl = signer; o.MapTokenUrl = token; }));

    [Fact]
    public async Task A_placed_map_is_a_picture_in_both_schemes_with_an_apple_maps_link()
    {
        var html = (await RenderAsync(Map(), Signer, "https://ishaunted.test/auth/mapkit-token")).Replace("&amp;", "&");

        Assert.Contains($"src=\"{Signer}?lat=36.161200&lng=-86.771700&z=14&w=360&h=200&scheme=light\"", html);
        Assert.Contains("scheme=dark", html);
        Assert.Contains("loading=\"lazy\"", html);
        Assert.Contains("alt=\"Map of Shelby Street Bridge\"", html);
        Assert.Contains("href=\"https://maps.apple.com/?ll=36.161200,-86.771700&q=Shelby%20Street%20Bridge\"", html);
        Assert.Contains("Live map", html);
        Assert.DoesNotContain("bc-map__place", html);
    }

    [Fact]
    public async Task Without_a_signer_the_box_is_the_address_card_and_still_links_to_apple_maps()
    {
        var html = await RenderAsync(Map(), signer: null);

        Assert.Contains("bc-map__place", html);
        Assert.DoesNotContain("<img", html);
        Assert.Contains("maps.apple.com", html);
        Assert.DoesNotContain("Live map", html);
    }

    [Fact]
    public async Task A_map_with_no_place_yet_asks_apple_for_nothing()
    {
        var html = await RenderAsync(Map(0, 0, null), Signer, "https://ishaunted.test/auth/mapkit-token");

        Assert.Contains("No place set yet", html);
        Assert.DoesNotContain("<img", html);
        Assert.DoesNotContain("maps.apple.com", html);
    }

    [Fact]
    public void The_picture_address_uses_dots_and_stays_within_apples_limits_under_fr_FR()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("fr-FR");
        try
        {
            var url = MapNode.SnapshotAddress(Signer, new MapData { Latitude = 48.8566, Longitude = 2.3522, Zoom = 25 }, 2000, 10, "dark");
            Assert.Equal($"{Signer}?lat=48.856600&lng=2.352200&z=20&w=640&h=50&scheme=dark", url);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData("https://ishaunted.com/auth/mapkit-token", "https://ishaunted.com/auth/mapkit-snapshot")]
    [InlineData("http://localhost:5078/auth/mapkit-token?origin=http://localhost:5125", "http://localhost:5078/auth/mapkit-snapshot")]
    [InlineData("https://maps.example.com/token", null)]
    [InlineData("", null)]
    public void The_signer_sits_beside_the_map_token(string token, string? expected) =>
        Assert.Equal(expected, CanvasEditorHostDefaults.SnapshotUrlBeside(token));
}
