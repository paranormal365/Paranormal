using Ben.Video.Editor.Services;

namespace Ben.Video.Tests.Services;

/// <summary>
/// Dragging a clip along its lane.
/// </summary>
public sealed class TimelineDragSessionTests
{
    private const double Zoom = 50; // pixels per second

    [Fact]
    public void A_drag_that_has_not_moved_leaves_the_clip_alone()
    {
        var drag = TimelineDragSession.Begin(pointerX: 400, originalPosition: 3, pxPerSecond: Zoom);

        Assert.Equal(3, drag.Move(400, [], false, 0.2).Position);
    }

    [Fact]
    public void Moving_right_moves_the_clip_later()
    {
        var drag = TimelineDragSession.Begin(400, 3, Zoom);

        Assert.Equal(5, drag.Move(500, [], false, 0.2).Position, 6);
    }

    [Fact]
    public void Moving_left_moves_the_clip_earlier()
    {
        var drag = TimelineDragSession.Begin(400, 3, Zoom);

        Assert.Equal(1, drag.Move(300, [], false, 0.2).Position, 6);
    }

    /// <summary>
    /// Every position is measured from where the pointer went down, so grabbing a clip in the
    /// middle moves it by how far the hand moved instead of jumping its start under the cursor.
    /// </summary>
    [Fact]
    public void The_drag_is_measured_from_the_pointer_not_the_clips_edge()
    {
        var drag = TimelineDragSession.Begin(pointerX: 1000, originalPosition: 3, pxPerSecond: Zoom);

        Assert.Equal(3, drag.Move(1000, [], false, 0.2).Position);
    }

    /// <summary>
    /// Nothing lives before zero. A negative position is one the ruler cannot draw and the export
    /// order cannot make sense of.
    /// </summary>
    [Fact]
    public void A_clip_dragged_off_the_left_edge_stops_at_zero()
    {
        var drag = TimelineDragSession.Begin(400, 3, Zoom);

        Assert.Equal(0, drag.Move(0, [], false, 0.2).Position);
    }

    [Fact]
    public void A_clip_that_started_before_zero_is_brought_back()
    {
        var drag = TimelineDragSession.Begin(400, originalPosition: -5, pxPerSecond: Zoom);

        Assert.Equal(0, drag.Move(400, [], false, 0.2).Position);
    }

    /// <summary>
    /// A zoom of zero would divide the drag into infinity. The lane is unusable at that point, so
    /// the clip stays where it was rather than flying to NaN.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public void A_timeline_with_no_zoom_moves_nothing(double pxPerSecond)
    {
        var drag = TimelineDragSession.Begin(400, 3, pxPerSecond);

        var moved = drag.Move(900, [], false, 0.2);

        Assert.Equal(3, moved.Position);
        Assert.Null(moved.SnapGuidePx);
    }

    // ── Snapping ──────────────────────────────────────────────────────────────

    [Fact]
    public void A_drag_that_lands_near_a_target_snaps_to_it()
    {
        var drag = TimelineDragSession.Begin(400, 3, Zoom);

        // 400 → 505 is 2.1 s of travel, landing at 5.1 with a clip edge at 5.
        var moved = drag.Move(505, [5.0], snapping: true, thresholdSeconds: 0.2);

        Assert.Equal(5.0, moved.Position, 6);
    }

    /// <summary>
    /// The guide is drawn where the clip now is. Drawing it from the un-snapped position put the
    /// line a few pixels beside the edge it claimed to align with.
    /// </summary>
    [Fact]
    public void The_snap_guide_is_drawn_at_the_target()
    {
        var drag = TimelineDragSession.Begin(400, 3, Zoom);

        var moved = drag.Move(505, [5.0], snapping: true, thresholdSeconds: 0.2);

        Assert.Equal(5.0 * Zoom, moved.SnapGuidePx);
    }

    [Fact]
    public void A_drag_that_lands_far_from_every_target_does_not_snap()
    {
        var drag = TimelineDragSession.Begin(400, 3, Zoom);

        var moved = drag.Move(600, [5.0], snapping: true, thresholdSeconds: 0.2);

        Assert.Equal(7, moved.Position, 6);
        Assert.Null(moved.SnapGuidePx);
    }

    /// <summary>
    /// With snapping switched off the clip goes exactly where it was dragged, and no guide is
    /// drawn — a line under a clip that did not snap is a lie about what just happened.
    /// </summary>
    [Fact]
    public void Snapping_switched_off_lands_where_the_pointer_did()
    {
        var drag = TimelineDragSession.Begin(400, 3, Zoom);

        var moved = drag.Move(505, [5.0], snapping: false, thresholdSeconds: 0.2);

        Assert.Equal(5.1, moved.Position, 6);
        Assert.Null(moved.SnapGuidePx);
    }

    [Fact]
    public void No_targets_is_not_a_crash()
    {
        var drag = TimelineDragSession.Begin(400, 3, Zoom);

        Assert.Equal(5, drag.Move(500, null, snapping: true, thresholdSeconds: 0.2).Position, 6);
    }

    /// <summary>
    /// A clip snapped against the left edge still cannot go negative: the clamp comes first, so a
    /// target at zero is the only one it can reach there.
    /// </summary>
    [Fact]
    public void Clamping_happens_before_snapping()
    {
        var drag = TimelineDragSession.Begin(400, 3, Zoom);

        var moved = drag.Move(0, [-2.0, 0.0], snapping: true, thresholdSeconds: 0.2);

        Assert.Equal(0, moved.Position);
    }

    /// <summary>
    /// The session holds the gesture's origin, so a second move is measured from the pointerdown
    /// rather than accumulating from the previous move.
    /// </summary>
    [Fact]
    public void Successive_moves_do_not_accumulate()
    {
        var drag = TimelineDragSession.Begin(400, 3, Zoom);

        drag.Move(600, [], false, 0.2);
        drag.Move(700, [], false, 0.2);

        Assert.Equal(5, drag.Move(500, [], false, 0.2).Position, 6);
    }
}
