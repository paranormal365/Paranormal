using Ben.Video.Core.Services;

namespace Ben.Video.Tests.Services;

/// <summary>
/// Where a new callout, title or piece of clip art lands.
/// </summary>
/// <remarks>
/// Three ways to add an overlay had three answers to this question, and two of them were wrong
/// (2026-09-05 audit, callouts-7, titles-8 and phase 11).
/// </remarks>
public sealed class OverlayPlacementTests
{
    [Fact]
    public void An_overlay_lands_at_the_playhead()
    {
        var placement = OverlayPlacement.AtPlayhead(playheadTimelineTime: 12.5, timelineTotalDuration: 60);

        Assert.Equal(12.5, placement.Position);
    }

    [Fact]
    public void An_overlay_on_a_long_timeline_lasts_five_seconds()
    {
        var placement = OverlayPlacement.AtPlayhead(0, 60);

        Assert.Equal(5.0, placement.Duration);
    }

    /// <summary>
    /// An overlay hanging past the end of the video it annotates is never what somebody meant, and
    /// the export drops what falls beyond the last frame anyway.
    /// </summary>
    [Fact]
    public void An_overlay_is_never_longer_than_the_timeline()
    {
        var placement = OverlayPlacement.AtPlayhead(0, timelineTotalDuration: 3);

        Assert.Equal(3, placement.Duration);
    }

    /// <summary>
    /// Overlays are trimmed by their edges, and an edge under a second is hard to grab.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(0.2)]
    public void An_overlay_on_an_almost_empty_timeline_is_still_grabbable(double total)
    {
        var placement = OverlayPlacement.AtPlayhead(0, total);

        Assert.Equal(OverlayPlacement.MinimumDurationSeconds, placement.Duration);
    }

    /// <summary>
    /// The old rule: put it after the end of everything. A second overlay then landed five seconds
    /// beyond the first, off the end of the video.
    /// </summary>
    [Fact]
    public void An_overlay_does_not_land_after_the_end_of_the_timeline()
    {
        var placement = OverlayPlacement.AtPlayhead(playheadTimelineTime: 4, timelineTotalDuration: 30);

        Assert.True(placement.Position < 30);
        Assert.Equal(4, placement.Position);
    }

    /// <summary>Nothing lives before zero, however the playhead got there.</summary>
    [Fact]
    public void A_negative_playhead_places_at_the_start() =>
        Assert.Equal(0, OverlayPlacement.AtPlayhead(-3, 30).Position);

    /// <summary>
    /// A duration read off an empty preview can arrive as NaN, and an overlay at NaN is invisible,
    /// unselectable and impossible to remove.
    /// </summary>
    [Fact]
    public void A_playhead_or_length_that_is_not_a_number_still_gives_a_usable_overlay()
    {
        var placement = OverlayPlacement.AtPlayhead(double.NaN, double.NaN);

        Assert.Equal(0, placement.Position);
        Assert.Equal(OverlayPlacement.PreferredDurationSeconds, placement.Duration);
    }

    [Fact]
    public void An_infinite_length_does_not_make_an_infinite_overlay() =>
        Assert.Equal(
            OverlayPlacement.PreferredDurationSeconds,
            OverlayPlacement.AtPlayhead(0, double.PositiveInfinity).Duration);

    /// <summary>
    /// Two overlays added at the same playhead stack on top of each other rather than marching off
    /// down the timeline — row order is what separates them, and that is the design.
    /// </summary>
    [Fact]
    public void Two_overlays_added_at_the_same_playhead_land_together()
    {
        var first  = OverlayPlacement.AtPlayhead(7, 30);
        var second = OverlayPlacement.AtPlayhead(7, 30);

        Assert.Equal(first, second);
    }
}
