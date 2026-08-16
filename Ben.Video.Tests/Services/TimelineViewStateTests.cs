using Ben.Video.Editor.Models;
using Ben.Video.Editor.Services;

namespace Ben.Video.Tests.Services;

public sealed class TimelineViewStateTests
{
    // ── ZoomScale defaults ──────────────────────────────────────────────────

    [Fact]
    public void Default_ZoomScale_IsOne()
    {
        var s = new TimelineViewState();
        Assert.Equal(1.0, s.ZoomScale);
    }

    [Fact]
    public void PxPerSecond_AtDefaultZoom_IsBasePxPerSecond()
    {
        var s = new TimelineViewState();
        Assert.Equal(TimelineViewState.BasePxPerSecond, s.PxPerSecond);
    }

    [Fact]
    public void CanvasWidth_MultipliesZoomedPxPerSecond()
    {
        var s = new TimelineViewState { ZoomScale = 2.0 };
        Assert.Equal(TimelineViewState.BasePxPerSecond * 2.0 * 10.0, s.CanvasWidth(10.0));
    }

    // ── ResetZoom ───────────────────────────────────────────────────────────

    [Fact]
    public void ResetZoom_SetsZoomToOne()
    {
        var s = new TimelineViewState { ZoomScale = 5.0 };
        s.ResetZoom();
        Assert.Equal(1.0, s.ZoomScale);
    }

    // ── FitToView ───────────────────────────────────────────────────────────

    [Fact]
    public void FitToView_ZeroTotalDuration_NoChange()
    {
        var s = new TimelineViewState { ZoomScale = 3.0 };
        s.FitToView(0, 800);
        Assert.Equal(3.0, s.ZoomScale);  // unchanged
    }

    [Fact]
    public void FitToView_ZeroWidth_NoChange()
    {
        var s = new TimelineViewState { ZoomScale = 3.0 };
        s.FitToView(60, 0);
        Assert.Equal(3.0, s.ZoomScale);  // unchanged
    }

    [Fact]
    public void FitToView_LongClip_ZoomsOut()
    {
        var s = new TimelineViewState();
        // 300s clip (5 min), 800px visible width
        s.FitToView(300, 800);
        // Needed: 800 * 0.95 / 300 / 80 ≈ 0.0317
        Assert.True(s.ZoomScale < 1.0, "Should zoom out below 1× for 5-min clip");
        Assert.True(s.ZoomScale >= 0.05, "Should not go below minimum 0.05×");
    }

    [Fact]
    public void FitToView_ShortClip_WideWindow_Clamps_To_MaxZoom()
    {
        var s = new TimelineViewState();
        // 1s clip, 10000px visible — would need extremely high zoom
        s.FitToView(1, 10000);
        Assert.Equal(20.0, s.ZoomScale);  // clamped to max 20×
    }

    [Fact]
    public void FitToView_TypicalClip_FitsWithMargin()
    {
        var s = new TimelineViewState();
        // 30s clip, 1000px visible — expected zoom = 1000*0.95 / 30 / 80 ≈ 0.396×
        s.FitToView(30, 1000);
        var expected = (1000 * 0.95) / 30.0 / TimelineViewState.BasePxPerSecond;
        Assert.Equal(expected, s.ZoomScale, precision: 4);
    }

    [Fact]
    public void FitToView_ClipsWithin5PercentMargin()
    {
        var s = new TimelineViewState();
        s.FitToView(60, 800);
        // After fit, total canvas width should be ≤ 800px and ≥ 800*0.9
        var canvasW = s.CanvasWidth(60);
        Assert.True(canvasW <= 800, "Canvas should fit within visible width");
        Assert.True(canvasW >= 800 * 0.9, "Canvas should use most of visible width");
    }

    // ── Tick computation ────────────────────────────────────────────────────

    [Fact]
    public void ComputeTicks_ZeroDuration_ReturnsEmpty()
    {
        var s = new TimelineViewState();
        var ticks = s.ComputeTicks(0);
        Assert.Empty(ticks);
    }

    [Fact]
    public void ComputeTicks_ShortDuration_FirstTickIsZero()
    {
        var s = new TimelineViewState();
        var ticks = s.ComputeTicks(5.0);
        Assert.True(ticks.Count > 0);
        Assert.Equal(0.0, ticks[0]);
    }

    [Fact]
    public void ComputeTicks_LongDuration_ContainsLargerInterval()
    {
        var s = new TimelineViewState { ZoomScale = 1.0 };
        // 5-minute clip at 1× zoom — verify ticks are reasonable (not every second)
        var ticks = s.ComputeTicks(300.0);
        Assert.True(ticks.Count > 1);
        var interval = ticks[1] - ticks[0];
        // At 80px/s × 1× = 80px per second; minSpacing=60px → interval ≥ 1s.
        // For a 300s clip the interval should be 5s, 10s, or larger to stay readable.
        Assert.True(interval >= 1.0, $"Expected ≥1s interval, got {interval}s");
    }

    [Fact]
    public void ComputeTicks_HighZoom_HasSubSecondInterval()
    {
        var s = new TimelineViewState { ZoomScale = 20.0 };
        var ticks = s.ComputeTicks(2.0);
        Assert.True(ticks.Count > 1);
        var interval = ticks[1] - ticks[0];
        Assert.True(interval <= 1.0, "Should use sub-second intervals at high zoom");
    }

    // ── FormatTick ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0,    TimelineDisplayMode.Time, "0.0s")]   // < 1 min: s.f format
    [InlineData(1.5,  TimelineDisplayMode.Time, "1.5s")]
    [InlineData(61,   TimelineDisplayMode.Time, "1:01")]  // ≥ 1 min: m:ss
    [InlineData(3661, TimelineDisplayMode.Time, "1:01:01")] // ≥ 1 hour
    public void FormatTick_TimeMode_FormatsCorrectly(double seconds, TimelineDisplayMode mode, string expected)
    {
        var s = new TimelineViewState { DisplayMode = mode };
        Assert.Equal(expected, s.FormatTick(seconds));
    }
}
