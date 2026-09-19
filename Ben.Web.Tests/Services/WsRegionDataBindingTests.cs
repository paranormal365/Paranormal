using System.Text.Json;
using Ben.Web.Website.Library.Manage.Audio;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// The region payload the player sends has to bind, and the failure if it does not is silent.
/// </summary>
/// <remarks>
/// <para>A computed <c>Kind</c> property was added next to the <c>KindName</c> string it reads,
/// and <c>KindName</c> carries <c>[JsonPropertyName("kind")]</c>. Under the web naming policy the
/// computed property claims the same name, and a collision makes the serializer throw for the whole
/// type on every deserialization — not for the one property.</para>
///
/// <para>Nothing said so. The player wraps its interop calls in a <c>safe()</c> helper that
/// swallows rejections, so <c>region-created</c> simply stopped arriving: dragging on the waveform
/// drew a region and selected nothing, with no error in the browser console and none on the server.
/// It was found by opening the page and watching a selection fail to register, after the unit tests
/// for the new behaviour had all passed (2026-09-06 audio audit, phase 2).</para>
///
/// <para>This binds the exact payload shape the module sends. Any future property that shadows
/// another one's JSON name fails here instead of silently disconnecting the waveform.</para>
/// </remarks>
public sealed class WsRegionDataBindingTests
{
    /// <summary>The options Blazor's JS interop uses: web defaults, camelCase.</summary>
    private static readonly JsonSerializerOptions Interop = new(JsonSerializerDefaults.Web);

    /// <summary>Exactly what <c>rd(r)</c> in <c>WaveSurferPlayer.razor.js</c> builds.</summary>
    private const string PlayerPayload = """
        {"id":"wavesurfer_abc","start":37.5,"end":75,"color":"rgba(0,0,0,0.1)","label":null,"kind":"user"}
        """;

    [Fact]
    public void The_payload_the_player_sends_binds()
    {
        var region = JsonSerializer.Deserialize<WsRegionData>(PlayerPayload, Interop);

        Assert.NotNull(region);
        Assert.Equal("wavesurfer_abc", region!.Id);
        Assert.Equal(37.5, region.Start);
        Assert.Equal(75,   region.End);
        Assert.Equal(RegionKind.User, region.Kind);
    }

    [Theory]
    [InlineData("silence", RegionKind.Silence)]
    [InlineData("marker",  RegionKind.Marker)]
    [InlineData("clip",    RegionKind.Clip)]
    [InlineData("overlay", RegionKind.Overlay)]
    [InlineData("user",    RegionKind.User)]
    public void Every_kind_the_player_sends_is_understood(string sent, RegionKind expected)
    {
        var json   = $$"""{"id":"r","start":0,"end":1,"kind":"{{sent}}"}""";
        var region = JsonSerializer.Deserialize<WsRegionData>(json, Interop);

        Assert.Equal(expected, region!.Kind);
    }

    /// <summary>
    /// An older player, or one that grows a kind this build has not heard of, must not have its
    /// regions reclassified as overlays — that would silently drop somebody's selection.
    /// </summary>
    [Theory]
    [InlineData("""{"id":"r","start":0,"end":1}""")]
    [InlineData("""{"id":"r","start":0,"end":1,"kind":null}""")]
    [InlineData("""{"id":"r","start":0,"end":1,"kind":"something-newer"}""")]
    public void Anything_unrecognised_is_treated_as_drawn_by_a_person(string json)
        => Assert.Equal(RegionKind.User, JsonSerializer.Deserialize<WsRegionData>(json, Interop)!.Kind);

    /// <summary>The region-params payload travels the other way and must serialize.</summary>
    [Fact]
    public void The_params_the_editor_sends_carry_their_kind()
    {
        var json = JsonSerializer.Serialize(
            new WsRegionParams { Id = "marker-1", Start = 4, End = 6, Kind = "marker" }, Interop);

        Assert.Contains("\"kind\":\"marker\"", json);
    }
}
