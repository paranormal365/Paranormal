using Ben.Web.Services;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// Where the standalone case canvas editor lives (canvas plan M6-11), copied from
/// <see cref="StandaloneEditorAddressTests"/> because the failure it prevents is the same one: a
/// literal mount path that is a 404 on every development machine.
/// </summary>
public class StandaloneCanvasAddressTests
{
    [Fact]
    public void Production_without_config_is_the_mount_path()
        => Assert.Equal("/editors/canvas/",
                        StandaloneCanvasAddress.For(isDevelopment: false, configured: null));

    [Fact]
    public void Development_without_config_is_localhost_5125()
        => Assert.Equal("http://localhost:5125/",
                        StandaloneCanvasAddress.For(isDevelopment: true, configured: null));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Configured_wins(bool isDevelopment)
        => Assert.Equal("https://canvas.example.com/",
                        StandaloneCanvasAddress.For(isDevelopment, "https://canvas.example.com/"));

    [Fact]
    public void Configured_wins_and_ends_in_a_slash()
        => Assert.Equal("https://uat.example.com/editors/canvas/",
                        StandaloneCanvasAddress.For(false, "https://uat.example.com/editors/canvas"));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void An_unset_key_falls_through_to_the_default(string? configured)
        => Assert.Equal("/editors/canvas/",
                        StandaloneCanvasAddress.For(isDevelopment: false, configured));
}
