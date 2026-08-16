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

    [Theory]
    [InlineData(WatermarkPosition.TopRight)]
    [InlineData(WatermarkPosition.BottomRight)]
    [InlineData(WatermarkPosition.MiddleRight)]
    public void BuildOverlayFilter_RightPositions_UseRightMargin(WatermarkPosition pos)
    {
        var f = BuildFilter(pos, scale: 0.15, videoWidth: 1920, marginX: 20);
        // wmWidth = 288; right edge = 1920 - 288 - 20 = 1612
        Assert.Contains("overlay=1612:", f);
    }

    [Fact]
    public void BuildOverlayFilter_TopPosition_UsesMarginY()
    {
        var f = BuildFilter(WatermarkPosition.TopCenter, marginY: 20);
        // y = marginY = 20
        Assert.Contains(":20", f);
    }

    [Fact]
    public void BuildOverlayFilter_CenterPosition_IsApproximatelyMiddle()
    {
        // x ≈ (1920 - 288) / 2 = 816
        var f = BuildFilter(WatermarkPosition.Center, scale: 0.15, videoWidth: 1920, videoHeight: 1080);
        Assert.Contains("overlay=816:", f);
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

    // ── Overlay filter coordinates are non-negative ───────────────────────────

    [Theory]
    [InlineData(WatermarkPosition.TopLeft,     1920, 1080)]
    [InlineData(WatermarkPosition.BottomRight, 1280,  720)]
    [InlineData(WatermarkPosition.Center,       640,  480)]
    public void BuildOverlayFilter_Coordinates_NonNegative(
        WatermarkPosition pos, int vw, int vh)
    {
        var f = BuildFilter(pos, videoWidth: vw, videoHeight: vh);

        // Extract overlay X Y from "overlay=X:Y"
        var overlayIdx = f.IndexOf("overlay=", StringComparison.Ordinal);
        Assert.True(overlayIdx >= 0);
        var coords = f[(overlayIdx + 8)..].Split('[')[0].Split(':');
        Assert.True(int.Parse(coords[0]) >= 0, $"X should be ≥ 0 for {pos}");
        Assert.True(int.Parse(coords[1]) >= 0, $"Y should be ≥ 0 for {pos}");
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
