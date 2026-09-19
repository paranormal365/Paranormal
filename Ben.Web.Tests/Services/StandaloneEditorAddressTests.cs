using Ben.Web.Services;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// Where the standalone video editor lives (V-6, site evaluation 2026-09-06).
/// </summary>
/// <remarks>
/// "Standalone editor" on <c>/my-videos</c> linked to the literal <c>/editors/video/</c> — a path
/// IIS mounts in production and nothing serves in development. So the site's one route into the
/// standalone editor was a 404 on every machine the site is built on, and the evaluation drove
/// the phase-12 handoff by hand against <c>:5180</c> instead.
/// </remarks>
public class StandaloneEditorAddressTests
{
    [Fact]
    public void Production_keeps_the_mount_path()
        => Assert.Equal("/editors/video/",
                        StandaloneEditorAddress.For(isDevelopment: false, configured: null));

    [Fact]
    public void Development_goes_to_the_webassembly_dev_server()
        => Assert.Equal("http://localhost:5180/",
                        StandaloneEditorAddress.For(isDevelopment: true, configured: null));

    /// <summary>Configuration wins, because a deployment knows its own layout.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_configured_address_beats_both_defaults(bool isDevelopment)
        => Assert.Equal("https://editor.example.com/",
                        StandaloneEditorAddress.For(isDevelopment, "https://editor.example.com/"));

    /// <summary>
    /// Always ends in a slash, because the handoff appends a fragment to it.
    /// </summary>
    /// <remarks>
    /// <c>/editors/video#handoff=…</c> is a different URL from <c>/editors/video/#handoff=…</c>,
    /// and the first one does not resolve to the app. Configuring the address without its trailing
    /// slash is the obvious way to get that wrong.
    /// </remarks>
    [Fact]
    public void A_configured_address_without_a_trailing_slash_gets_one()
        => Assert.Equal("https://editor.example.com/app/",
                        StandaloneEditorAddress.For(false, "https://editor.example.com/app"));

    /// <summary>
    /// Blank is somebody who has not decided, not somebody who chose "".
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void An_unset_key_falls_through_to_the_default(string? configured)
        => Assert.Equal("/editors/video/",
                        StandaloneEditorAddress.For(isDevelopment: false, configured));
}
