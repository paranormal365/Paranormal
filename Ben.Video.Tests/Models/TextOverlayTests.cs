using Ben.Video.Editor.Models;

namespace Ben.Video.Tests.Models;

public sealed class TextOverlayTests
{
    private static TextOverlay MakeOverlay(double duration = 4.0, double fadeIn = 1.0, double fadeOut = 1.0) => new()
    {
        Text           = "Hello",
        Duration       = duration,
        FadeInSeconds  = fadeIn,
        FadeOutSeconds = fadeOut,
    };

    [Fact]
    public void ComputeFadeAlpha_DuringFadeIn_RampsLinearly()
    {
        var overlay = MakeOverlay(duration: 4.0, fadeIn: 1.0, fadeOut: 1.0);
        Assert.Equal(0.0, overlay.ComputeFadeAlpha(0.0), 3);
        Assert.Equal(0.5, overlay.ComputeFadeAlpha(0.5), 3);
        Assert.Equal(1.0, overlay.ComputeFadeAlpha(1.0), 3);
    }

    [Fact]
    public void ComputeFadeAlpha_DuringFadeOut_RampsLinearlyDown()
    {
        var overlay = MakeOverlay(duration: 4.0, fadeIn: 1.0, fadeOut: 1.0);
        // Fade-out window is the last 1s: elapsed in [3.0, 4.0]
        Assert.Equal(1.0, overlay.ComputeFadeAlpha(3.0), 3);
        Assert.Equal(0.5, overlay.ComputeFadeAlpha(3.5), 3);
        Assert.Equal(0.0, overlay.ComputeFadeAlpha(4.0), 3);
    }

    [Fact]
    public void ComputeFadeAlpha_MiddleOfLifetime_IsFullyOpaque()
    {
        var overlay = MakeOverlay(duration: 4.0, fadeIn: 1.0, fadeOut: 1.0);
        Assert.Equal(1.0, overlay.ComputeFadeAlpha(2.0), 3);
    }

    [Fact]
    public void ComputeFadeAlpha_NoFade_AlwaysFullyOpaque()
    {
        var overlay = MakeOverlay(duration: 4.0, fadeIn: 0.0, fadeOut: 0.0);
        Assert.Equal(1.0, overlay.ComputeFadeAlpha(0.0), 3);
        Assert.Equal(1.0, overlay.ComputeFadeAlpha(2.0), 3);
        Assert.Equal(1.0, overlay.ComputeFadeAlpha(4.0), 3);
    }

    [Fact]
    public void ComputeFadeAlpha_ClampsToZeroOneRange()
    {
        var overlay = MakeOverlay(duration: 4.0, fadeIn: 1.0, fadeOut: 1.0);
        // Past the end of the overlay's own lifetime — remaining goes negative.
        Assert.Equal(0.0, overlay.ComputeFadeAlpha(5.0), 3);
    }
}
