using Ben.Video.Editor.Services;

namespace Ben.Video.Tests.Services;

public sealed class LayoutServiceTests
{
    // ── Default values ────────────────────────────────────────────────────────

    [Fact]
    public void Defaults_AreTheExpectedSizes()
    {
        var layout = new LayoutService();

        Assert.Equal(240, layout.BrowserWidth);
        Assert.Equal(220, layout.TimelineHeight);
        Assert.Equal(180, layout.PreviewHeight);
    }

    // ── Resize ────────────────────────────────────────────────────────────────

    [Fact]
    public void SetBrowserWidth_ValidValue_SetsExact()
    {
        var layout = new LayoutService();

        layout.SetBrowserWidth(320);

        Assert.Equal(320, layout.BrowserWidth);
    }

    [Fact]
    public void SetBrowserWidth_BelowMin_ClampsToMin()
    {
        var layout = new LayoutService();

        layout.SetBrowserWidth(0);

        Assert.Equal(LayoutService.BrowserMinWidth, layout.BrowserWidth);
    }

    [Fact]
    public void SetBrowserWidth_AboveMax_ClampsToMax()
    {
        var layout = new LayoutService();

        layout.SetBrowserWidth(9999);

        Assert.Equal(LayoutService.BrowserMaxWidth, layout.BrowserWidth);
    }

    [Fact]
    public void SetBrowserWidth_AtBoundary_AcceptsBoundaryValues()
    {
        var layout = new LayoutService();

        layout.SetBrowserWidth(LayoutService.BrowserMinWidth);
        Assert.Equal(LayoutService.BrowserMinWidth, layout.BrowserWidth);

        layout.SetBrowserWidth(LayoutService.BrowserMaxWidth);
        Assert.Equal(LayoutService.BrowserMaxWidth, layout.BrowserWidth);
    }

    // ── SetTimelineHeight clamping ────────────────────────────────────────────

    [Fact]
    public void SetTimelineHeight_ValidValue_SetsExact()
    {
        var layout = new LayoutService();

        layout.SetTimelineHeight(300);

        Assert.Equal(300, layout.TimelineHeight);
    }

    [Fact]
    public void SetTimelineHeight_BelowMin_ClampsToMin()
    {
        var layout = new LayoutService();

        layout.SetTimelineHeight(0);

        Assert.Equal(LayoutService.TimelineMinHeight, layout.TimelineHeight);
    }

    [Fact]
    public void SetTimelineHeight_AboveMax_ClampsToMax()
    {
        var layout = new LayoutService();

        layout.SetTimelineHeight(9999);

        Assert.Equal(LayoutService.TimelineMaxHeight, layout.TimelineHeight);
    }

    [Fact]
    public void SetTimelineHeight_AtBoundary_AcceptsBoundaryValues()
    {
        var layout = new LayoutService();

        layout.SetTimelineHeight(LayoutService.TimelineMinHeight);
        Assert.Equal(LayoutService.TimelineMinHeight, layout.TimelineHeight);

        layout.SetTimelineHeight(LayoutService.TimelineMaxHeight);
        Assert.Equal(LayoutService.TimelineMaxHeight, layout.TimelineHeight);
    }

    // ── OnChanged event ───────────────────────────────────────────────────────

    [Fact]
    public void SetBrowserWidth_RaisesOnChanged()
    {
        var layout = new LayoutService();
        var fired = false;
        layout.OnChanged += () => fired = true;

        layout.SetBrowserWidth(300);

        Assert.True(fired);
    }

    [Fact]
    public void SetTimelineHeight_RaisesOnChanged()
    {
        var layout = new LayoutService();
        var fired = false;
        layout.OnChanged += () => fired = true;

        layout.SetTimelineHeight(280);

        Assert.True(fired);
    }
}
