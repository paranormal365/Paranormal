using Ben.Web.Website.Services;
using Xunit;

namespace Ben.Web.Tests.Website;

/// <summary>
/// Who may have a map picture signed, and what may be asked: our pages only (it spends our Apple quota), and only
/// within Apple's limits.
/// </summary>
public class MapKitSnapshotRequestTests
{
    private const string Site = "https://ishaunted.com";
    private static readonly string[] DevAllowed = ["http://localhost:5125"];

    private static MapKitSnapshotAsk? Read(string? referer, string lat = "36.1612", string lng = "-86.7717", string z = "14", string w = "360", string h = "200", string scheme = "dark", string[]? allowed = null) =>
        MapKitSnapshotRequest.Read(lat, lng, z, w, h, scheme, Site, referer, allowed ?? []);

    [Fact]
    public void A_request_from_our_own_canvas_page_is_read()
    {
        var ask = Read("https://ishaunted.com/editors/canvas/");

        Assert.Equal(new MapKitSnapshotAsk(36.1612, -86.7717, 14, 360, 200, "dark"), ask);
    }

    [Fact]
    public void An_allow_listed_development_origin_is_read()
    {
        Assert.NotNull(Read("http://localhost:5125/", allowed: DevAllowed));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("https://someone-else.example/page")]
    [InlineData("http://localhost:5125/")]
    [InlineData("not a url")]
    public void A_request_not_from_our_pages_is_refused(string? referer)
    {
        Assert.Null(Read(referer));
    }

    [Theory]
    [InlineData("91", "0", "14", "360", "200", "dark")]
    [InlineData("0", "-181", "14", "360", "200", "dark")]
    [InlineData("0", "0", "2", "360", "200", "dark")]
    [InlineData("0", "0", "14", "641", "200", "dark")]
    [InlineData("0", "0", "14", "360", "49", "dark")]
    [InlineData("0", "0", "14", "360", "200", "satellite")]
    [InlineData("36,16", "0", "14", "360", "200", "dark")]
    public void A_request_outside_apples_limits_is_refused(string lat, string lng, string z, string w, string h, string scheme)
    {
        Assert.Null(Read("https://ishaunted.com/editors/canvas/", lat, lng, z, w, h, scheme));
    }
}
