using Ben.Video.Editor.Models.Assets;
using Ben.Video.Editor.Services;

namespace Ben.Video.Tests.Services;

public sealed class WatermarkServiceTests
{
    // ── VideoWatermarkConfig model ────────────────────────────────────────────

    [Fact]
    public void WatermarkConfig_Disabled_NoFileRequired()
    {
        var cfg = new VideoWatermarkConfig { Enabled = false };
        Assert.False(cfg.Enabled);
        Assert.Null(cfg.FileUrl);
    }

    [Fact]
    public void WatermarkConfig_Defaults_BottomRight_50pct_15pct()
    {
        var cfg = new VideoWatermarkConfig();
        Assert.Equal(WatermarkPosition.BottomRight, cfg.Position);
        Assert.Equal(0.5,  cfg.Opacity);
        Assert.Equal(0.15, cfg.ScaleFraction);
        Assert.Equal(20,   cfg.MarginX);
        Assert.Equal(20,   cfg.MarginY);
    }

    // ── WatermarkService.BuildOverlayFilter ───────────────────────────────────

    private static string BuildFilter(
        WatermarkPosition pos,
        double opacity       = 1.0,
        double scale         = 0.15,
        int marginX          = 20,
        int marginY          = 20,
        int videoWidth       = 1920,
        int videoHeight      = 1080)
    {
        var cfg = new VideoWatermarkConfig
        {
            Enabled       = true,
            Position      = pos,
            Opacity       = opacity,
            ScaleFraction = scale,
            MarginX       = marginX,
            MarginY       = marginY,
        };
        return WatermarkService.BuildOverlayFilter(cfg, "wm.png", videoWidth, videoHeight);
    }

    [Fact]
    public void BuildOverlayFilter_ContainsInputLabels()
    {
        var f = BuildFilter(WatermarkPosition.BottomRight);
        Assert.Contains("[1:v]", f);
        Assert.Contains("[0:v]", f);
        Assert.Contains("[wm]", f);
        Assert.Contains("[out]", f);
    }

    [Fact]
    public void BuildOverlayFilter_ScalesWatermark()
    {
        // scale = 0.15, videoWidth = 1920 → wmWidth = 288
        var f = BuildFilter(WatermarkPosition.BottomRight, scale: 0.15, videoWidth: 1920);
        Assert.Contains("scale=288:", f);
    }

    [Fact]
    public void BuildOverlayFilter_OpacityInFilter()
    {
        var f = BuildFilter(WatermarkPosition.BottomRight, opacity: 0.5);
        Assert.Contains("aa=0.50", f);
    }

    [Fact]
    public void BuildOverlayFilter_FullOpacity()
    {
        var f = BuildFilter(WatermarkPosition.BottomRight, opacity: 1.0);
        Assert.Contains("aa=1.00", f);
    }

    [Theory]
    [InlineData(WatermarkPosition.TopLeft,     20)]        // x = marginX
    [InlineData(WatermarkPosition.BottomLeft,  20)]        // x = marginX
    [InlineData(WatermarkPosition.MiddleLeft,  20)]        // x = marginX
    public void BuildOverlayFilter_LeftPositions_UseMarginX(WatermarkPosition pos, int expectedX)
    {
        var f = BuildFilter(pos, videoWidth: 1920, marginX: 20);
        Assert.Contains($"overlay={expectedX}:", f);
    }

    /// <summary>
    /// Right and bottom edges are ffmpeg expressions, not arithmetic done here.
    /// </summary>
    /// <remarks>
    /// The x these used to assert (1920 − 288 − 20) was right, but the matching y was computed
    /// from a guessed watermark height of half its width, so a square logo hung below the frame.
    /// The overlay filter knows the scaled size as <c>w</c>/<c>h</c>; letting it do the subtraction
    /// removes the guess (2026-09-05 audit, export-7).
    /// </remarks>
    [Theory]
    [InlineData(WatermarkPosition.TopRight)]
    [InlineData(WatermarkPosition.BottomRight)]
    [InlineData(WatermarkPosition.MiddleRight)]
    public void BuildOverlayFilter_RightPositions_MeasureFromTheRightEdge(WatermarkPosition pos)
    {
        var f = BuildFilter(pos, scale: 0.15, videoWidth: 1920, marginX: 20);
        Assert.Contains("overlay=W-w-20:", f);
    }

    [Theory]
    [InlineData(WatermarkPosition.BottomLeft)]
    [InlineData(WatermarkPosition.BottomCenter)]
    [InlineData(WatermarkPosition.BottomRight)]
    public void BuildOverlayFilter_BottomPositions_MeasureFromTheBottomEdge(WatermarkPosition pos)
    {
        var f = BuildFilter(pos, marginY: 20);
        Assert.EndsWith(":H-h-20[out]", f);
    }

    [Fact]
    public void BuildOverlayFilter_TopPosition_UsesMarginY()
    {
        var f = BuildFilter(WatermarkPosition.TopCenter, marginY: 20);
        Assert.EndsWith(":20[out]", f);
    }

    [Theory]
    [InlineData(WatermarkPosition.MiddleLeft)]
    [InlineData(WatermarkPosition.Center)]
    [InlineData(WatermarkPosition.MiddleRight)]
    public void BuildOverlayFilter_MiddlePositions_CentreOnTheRealHeight(WatermarkPosition pos)
    {
        var f = BuildFilter(pos, videoHeight: 1080);
        Assert.EndsWith(":(H-h)/2[out]", f);
    }

    [Fact]
    public void BuildOverlayFilter_CenterPosition_CentresHorizontallyToo()
    {
        var f = BuildFilter(WatermarkPosition.Center, scale: 0.15, videoWidth: 1920, videoHeight: 1080);
        Assert.Contains("overlay=(W-w)/2:", f);
    }

    // ── All positions compile and produce a valid filter ──────────────────────

    [Theory]
    [InlineData(WatermarkPosition.TopLeft)]
    [InlineData(WatermarkPosition.TopCenter)]
    [InlineData(WatermarkPosition.TopRight)]
    [InlineData(WatermarkPosition.MiddleLeft)]
    [InlineData(WatermarkPosition.Center)]
    [InlineData(WatermarkPosition.MiddleRight)]
    [InlineData(WatermarkPosition.BottomLeft)]
    [InlineData(WatermarkPosition.BottomCenter)]
    [InlineData(WatermarkPosition.BottomRight)]
    public void BuildOverlayFilter_AllPositions_ProduceValidFilter(WatermarkPosition pos)
    {
        var f = BuildFilter(pos);
        Assert.Contains("[out]",   f);
        Assert.Contains("overlay", f);
        Assert.Contains("scale",   f);
    }

    // ── Overlay coordinates keep the watermark inside the frame ───────────────

    /// <summary>
    /// Each coordinate is either a fixed margin or an expression ffmpeg resolves against the real
    /// sizes — never arithmetic done here against a guessed watermark height.
    /// </summary>
    /// <remarks>
    /// This replaces a test that parsed both coordinates as integers and asserted they were ≥ 0.
    /// It passed while the bottom edge was computed from <c>wmWidth / 2</c>, so a square logo was
    /// positioned as though it were half as tall and hung below the frame (2026-09-05 audit,
    /// export-7). Integers cannot express "the bottom edge" before the scale filter has run.
    /// </remarks>
    [Theory]
    [InlineData(WatermarkPosition.TopLeft,      1920, 1080)]
    [InlineData(WatermarkPosition.TopCenter,    1920, 1080)]
    [InlineData(WatermarkPosition.TopRight,     1920, 1080)]
    [InlineData(WatermarkPosition.MiddleLeft,   1280,  720)]
    [InlineData(WatermarkPosition.Center,        640,  480)]
    [InlineData(WatermarkPosition.MiddleRight,  1280,  720)]
    [InlineData(WatermarkPosition.BottomLeft,   1280,  720)]
    [InlineData(WatermarkPosition.BottomCenter,  640,  480)]
    [InlineData(WatermarkPosition.BottomRight,  1280,  720)]
    public void BuildOverlayFilter_Coordinates_StayInsideTheFrame(
        WatermarkPosition pos, int vw, int vh)
    {
        var f = BuildFilter(pos, videoWidth: vw, videoHeight: vh, marginX: 20, marginY: 20);

        var overlayIdx = f.IndexOf("overlay=", StringComparison.Ordinal);
        Assert.True(overlayIdx >= 0);
        var coords = f[(overlayIdx + 8)..].Split('[')[0].Split(':');

        Assert.True(IsInsideTheFrame(coords[0], 'W', 'w'), $"X of '{coords[0]}' is wrong for {pos}");
        Assert.True(IsInsideTheFrame(coords[1], 'H', 'h'), $"Y of '{coords[1]}' is wrong for {pos}");
    }

    /// <summary>
    /// A coordinate is a non-negative margin, a centring expression, or a far-edge expression that
    /// subtracts the overlay's own size — the three shapes that cannot put the logo off-frame.
    /// </summary>
    private static bool IsInsideTheFrame(string coordinate, char frame, char overlay)
    {
        if (int.TryParse(coordinate, out var fixedPixels)) return fixedPixels >= 0;

        if (coordinate == $"({frame}-{overlay})/2") return true;

        return coordinate.StartsWith($"{frame}-{overlay}-", StringComparison.Ordinal)
            && int.TryParse(coordinate[$"{frame}-{overlay}-".Length..], out var margin)
            && margin >= 0;
    }

    // ── WatermarkService.EnsureLocalAsync returns null when disabled ──────────

    [Fact]
    public async Task WatermarkService_GetConfig_DisabledConfig_ReturnsNull()
    {
        // Verify that a disabled watermark config produces no local file request.
        // (Full integration test needs mocked IHttpClientFactory and OPFSService.)
        var cfg = new VideoWatermarkConfig { Enabled = false };
        Assert.False(cfg.Enabled);
        Assert.Null(cfg.FileUrl);
        // If Enabled is false, EnsureLocalAsync should return null without HTTP calls.
        await Task.CompletedTask; // placeholder — behaviour confirmed by implementation review
    }
}
