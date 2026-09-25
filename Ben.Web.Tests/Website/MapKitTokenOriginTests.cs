using Ben.Web.Website.Services;
using Xunit;

namespace Ben.Web.Tests.Website;

/// <summary>
/// Which origin a MapKit token is minted for (canvas plan M6-12).
/// </summary>
/// <remarks>
/// <para>A MapKit JS token names the origin it was signed for and Apple refuses it anywhere else, so
/// the website mints tokens for its own origin only. In production the canvas at
/// <c>/editors/canvas/</c> is that same origin and needs nothing. In development it is not: the canvas
/// runs on <c>http://localhost:5125</c> and the website on 5078, so a token minted for the request's
/// origin draws no tiles on the canvas.</para>
///
/// <para>The override is an allow list, not a free parameter. A token endpoint that signed for any
/// <c>?origin=</c> would hand our Apple Maps quota to every site on the internet — the list is empty
/// in production and names only the local canvas in development.</para>
/// </remarks>
public sealed class MapKitTokenOriginTests
{
    private static readonly string[] Allowed = ["http://localhost:5125"];

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_requested_origin_uses_the_request_origin(string? requested)
        => Assert.Equal("http://localhost:5078", MapKitTokenOrigin.Resolve("http://localhost:5078", requested, Allowed));

    [Theory]
    [InlineData("http://localhost:5125")]
    [InlineData("http://localhost:5125/")]
    [InlineData("  HTTP://LOCALHOST:5125  ")]
    public void An_allow_listed_origin_is_used(string requested)
        => Assert.Equal("http://localhost:5125", MapKitTokenOrigin.Resolve("http://localhost:5078", requested, Allowed), ignoreCase: true);

    [Theory]
    [InlineData("https://evil.example")]
    [InlineData("http://localhost:5126")]
    [InlineData("https://localhost:5125")]
    public void An_unlisted_origin_is_refused(string requested)
        => Assert.Null(MapKitTokenOrigin.Resolve("http://localhost:5078", requested, Allowed));

    [Theory]
    [InlineData("http://localhost:5125/path")]
    [InlineData("http://localhost:5125/?q=1")]
    [InlineData("http://localhost:5125#x")]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///c:/x")]
    [InlineData("localhost:5125")]
    public void An_origin_with_a_path_or_the_wrong_shape_is_refused(string requested)
        => Assert.Null(MapKitTokenOrigin.Resolve("http://localhost:5078", requested,
            ["http://localhost:5125", "http://localhost:5125/path", "javascript:alert(1)", "localhost:5125"]));

    [Fact]
    public void With_an_empty_allow_list_every_override_is_refused()
        => Assert.Null(MapKitTokenOrigin.Resolve("https://ishaunted.com", "http://localhost:5125", []));
}
