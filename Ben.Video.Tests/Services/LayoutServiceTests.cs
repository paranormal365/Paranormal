using Ben.Video.Editor.Services;

namespace Ben.Video.Tests.Services;

/// <summary>
/// The editor's layout: how tall the timeline is, how wide the panel is, and what survives a reload.
/// </summary>
/// <remarks>
/// <para>The preview deliberately has no height here. It used to, and the timeline sat beside it
/// asking for <c>height: 100%</c>, which in a column flexbox makes the timeline's flex-basis the
/// whole editor: the two then shrank in proportion to those bases and the preview kept about
/// two-thirds of whatever it was given, ending as a 38-pixel strip under 700 pixels of empty
/// timeline. The timeline is the thing with a size; the picture takes the rest (2026-09-05 audit,
/// F4).</para>
/// </remarks>
public sealed class LayoutServiceTests
{
    // ── Defaults ──────────────────────────────────────────────────────────────

    [Fact]
    public void Defaults_AreTheExpectedSizes()
    {
        var layout = new LayoutService();

        Assert.Equal(LayoutService.DefaultPanelWidth, layout.PanelWidth);
        Assert.Equal(LayoutService.DefaultTimelineHeight, layout.TimelineHeight);
        Assert.False(layout.PanelCollapsed);
        Assert.Equal("media", layout.PanelTab);
        Assert.False(layout.TimelineHeightUserSet);
    }

    // ── Resize ────────────────────────────────────────────────────────────────

    [Fact]
    public void SetPanelWidth_ValidValue_SetsExact()
    {
        var layout = new LayoutService();

        layout.SetPanelWidth(420);

        Assert.Equal(420, layout.PanelWidth);
    }

    [Theory]
    [InlineData(0, LayoutService.PanelMinWidth)]
    [InlineData(9999, LayoutService.PanelMaxWidth)]
    public void SetPanelWidth_OutOfRange_Clamps(int requested, int expected)
    {
        var layout = new LayoutService();

        layout.SetPanelWidth(requested);

        Assert.Equal(expected, layout.PanelWidth);
    }

    [Theory]
    [InlineData(0, LayoutService.TimelineMinHeight)]
    [InlineData(9999, LayoutService.TimelineMaxHeight)]
    public void SetTimelineHeight_OutOfRange_Clamps(int requested, int expected)
    {
        var layout = new LayoutService();

        layout.SetTimelineHeight(requested);

        Assert.Equal(expected, layout.TimelineHeight);
    }

    // ── Auto-fit defers to the person ─────────────────────────────────────────

    [Fact]
    public void AutoFitTimeline_SizesToTheTracks_BeforeAnyDrag()
    {
        var layout = new LayoutService();

        layout.AutoFitTimeline(400);

        Assert.Equal(400, layout.TimelineHeight);
    }

    /// <summary>
    /// The rule that makes auto-fit a courtesy rather than a fight: once somebody has dragged the
    /// seam, adding a track must not undo their choice.
    /// </summary>
    [Fact]
    public void AutoFitTimeline_IsIgnoredAfterTheSeamIsDragged()
    {
        var layout = new LayoutService();
        layout.SetTimelineHeight(180);

        layout.AutoFitTimeline(520);

        Assert.Equal(180, layout.TimelineHeight);
        Assert.True(layout.TimelineHeightUserSet);
    }

    [Fact]
    public void AutoFitTimeline_ClampsLikeADrag()
    {
        var layout = new LayoutService();

        layout.AutoFitTimeline(5000);

        Assert.Equal(LayoutService.TimelineMaxHeight, layout.TimelineHeight);
    }

    [Fact]
    public void AutoFitTimeline_DoesNotNotifyWhenNothingChanges()
    {
        var layout = new LayoutService();
        var raised = 0;
        layout.OnChanged += () => raised++;

        layout.AutoFitTimeline(layout.TimelineHeight);

        Assert.Equal(0, raised);
    }

    // ── Panel ─────────────────────────────────────────────────────────────────

    [Fact]
    public void TogglePanel_FlipsCollapsed()
    {
        var layout = new LayoutService();

        layout.TogglePanel();
        Assert.True(layout.PanelCollapsed);

        layout.TogglePanel();
        Assert.False(layout.PanelCollapsed);
    }

    [Fact]
    public void SetPanelTab_IgnoresBlankAndUnchanged()
    {
        var layout = new LayoutService();
        var raised = 0;
        layout.OnChanged += () => raised++;

        layout.SetPanelTab("media");   // already current
        layout.SetPanelTab("  ");      // nonsense

        Assert.Equal(0, raised);
        Assert.Equal("media", layout.PanelTab);

        layout.SetPanelTab("props");
        Assert.Equal(1, raised);
        Assert.Equal("props", layout.PanelTab);
    }

    // ── Change notification ───────────────────────────────────────────────────

    [Theory]
    [InlineData("panel width")]
    [InlineData("timeline height")]
    [InlineData("collapse")]
    public void EveryChange_RaisesOnChanged(string change)
    {
        var layout = new LayoutService();
        var raised = 0;
        layout.OnChanged += () => raised++;

        switch (change)
        {
            case "panel width":     layout.SetPanelWidth(400); break;
            case "timeline height": layout.SetTimelineHeight(300); break;
            case "collapse":        layout.TogglePanel(); break;
        }

        Assert.Equal(1, raised);
    }

    // ── Persistence ───────────────────────────────────────────────────────────

    [Fact]
    public void Snapshot_RoundTripsThroughJson()
    {
        var saved = new LayoutService();
        saved.SetPanelWidth(430);
        saved.SetTimelineHeight(310);
        saved.SetPanelTab("props");
        saved.TogglePanel();

        var restored = new LayoutService();
        restored.Apply(LayoutService.Deserialise(saved.Serialise()));

        Assert.Equal(430, restored.PanelWidth);
        Assert.Equal(310, restored.TimelineHeight);
        Assert.Equal("props", restored.PanelTab);
        Assert.True(restored.PanelCollapsed);

        // The drag is remembered too, so a restored layout is not re-fitted out from under them
        // on the first clip that lands.
        Assert.True(restored.TimelineHeightUserSet);
    }

    /// <summary>
    /// Everything stored is optional, because an older build wrote fewer fields.
    /// </summary>
    [Fact]
    public void Apply_TakesAPartialSnapshotAndLeavesTheRestAlone()
    {
        var layout = new LayoutService();

        layout.Apply(LayoutService.Deserialise("""{"panelWidth":500}"""));

        Assert.Equal(500, layout.PanelWidth);
        Assert.Equal(LayoutService.DefaultTimelineHeight, layout.TimelineHeight);
        Assert.Equal("media", layout.PanelTab);
    }

    /// <summary>
    /// A hand-edited or corrupted entry must give a usable editor, never a zero-height timeline.
    /// </summary>
    [Theory]
    [InlineData("""{"panelWidth":-40,"timelineHeight":0}""")]
    [InlineData("""{"panelWidth":99999,"timelineHeight":99999}""")]
    public void Apply_ClampsWhateverTheBrowserHandsBack(string json)
    {
        var layout = new LayoutService();

        layout.Apply(LayoutService.Deserialise(json));

        Assert.InRange(layout.PanelWidth, LayoutService.PanelMinWidth, LayoutService.PanelMaxWidth);
        Assert.InRange(layout.TimelineHeight, LayoutService.TimelineMinHeight, LayoutService.TimelineMaxHeight);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    public void Deserialise_TreatsRubbishAsNoPreference(string? stored)
    {
        var snapshot = LayoutService.Deserialise(stored);

        var layout = new LayoutService();
        layout.Apply(snapshot);

        Assert.Equal(LayoutService.DefaultTimelineHeight, layout.TimelineHeight);
    }
}
