using Ben.Video.Editor.Services;

namespace Ben.Video.Tests.Services;

public sealed class LayoutServiceTests
{
    // ── Default values ────────────────────────────────────────────────────────

    [Fact]
    public void Defaults_AreAllVisible_WithExpectedSizes()
    {
        var layout = new LayoutService();

        Assert.True(layout.ShowClipBrowser);
        Assert.True(layout.ShowPreview);
        Assert.True(layout.ShowTimeline);
        Assert.Equal(240, layout.BrowserWidth);
        Assert.Equal(220, layout.TimelineHeight);
    }

    // ── Toggle methods ────────────────────────────────────────────────────────

    [Fact]
    public void ToggleClipBrowser_FlipsState()
    {
        var layout = new LayoutService();

        layout.ToggleClipBrowser();
        Assert.False(layout.ShowClipBrowser);

        layout.ToggleClipBrowser();
        Assert.True(layout.ShowClipBrowser);
    }

    [Fact]
    public void TogglePreview_FlipsState()
    {
        var layout = new LayoutService();

        layout.TogglePreview();
        Assert.False(layout.ShowPreview);

        layout.TogglePreview();
        Assert.True(layout.ShowPreview);
    }

    [Fact]
    public void ToggleTimeline_FlipsState()
    {
        var layout = new LayoutService();

        layout.ToggleTimeline();
        Assert.False(layout.ShowTimeline);

        layout.ToggleTimeline();
        Assert.True(layout.ShowTimeline);
    }

    // ── SetBrowserWidth clamping ──────────────────────────────────────────────

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
    public void ToggleClipBrowser_RaisesOnChanged()
    {
        var layout = new LayoutService();
        var fired = false;
        layout.OnChanged += () => fired = true;

        layout.ToggleClipBrowser();

        Assert.True(fired);
    }

    [Fact]
    public void TogglePreview_RaisesOnChanged()
    {
        var layout = new LayoutService();
        var fired = false;
        layout.OnChanged += () => fired = true;

        layout.TogglePreview();

        Assert.True(fired);
    }

    [Fact]
    public void ToggleTimeline_RaisesOnChanged()
    {
        var layout = new LayoutService();
        var fired = false;
        layout.OnChanged += () => fired = true;

        layout.ToggleTimeline();

        Assert.True(fired);
    }

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
