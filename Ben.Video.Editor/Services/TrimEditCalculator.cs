namespace Ben.Video.Editor.Services;

/// <summary>
/// Pure static helper for the three standard NLE trim variants beyond plain edge-trim and
/// whole-clip move/ripple-move (item #50): <b>slip</b> (change what part of the source media a
/// clip shows, without moving it or changing its on-timeline duration), <b>roll</b> (move the
/// shared edit point between two adjacent clips, extending one and shrinking the other by the
/// same amount, without changing their combined span), and <b>slide</b> (move a clip along the
/// timeline without changing its own trim, letting its neighbors absorb the move on either side —
/// governed by the same boundary math as roll, just applied to the moved clip's neighbors instead
/// of to the clip itself).
/// Isolated from Blazor/ClipStore so it can be unit-tested without a browser.
/// </summary>
public static class TrimEditCalculator
{
    /// <summary>
    /// Clamps a slip delta so the resulting source-trim window [<paramref name="startTrim"/> + delta,
    /// <paramref name="endTrim"/> + delta] stays within the source media's [0, <paramref name="sourceDuration"/>]
    /// bounds. Returns 0 if there's no room to slip at all (e.g. the clip already uses its full
    /// source length).
    /// </summary>
    public static double ClampSlipDelta(double delta, double startTrim, double endTrim, double sourceDuration)
    {
        var minDelta = -startTrim;
        var maxDelta = sourceDuration - endTrim;
        if (minDelta > maxDelta) return 0;
        return Math.Clamp(delta, minDelta, maxDelta);
    }

    /// <summary>
    /// Clamps a delta that extends a "left" segment's out-trim by that amount while shrinking (and
    /// re-positioning) an immediately-following "right" segment's in-trim by the same amount — the
    /// shared boundary math behind both <b>roll</b> (left/right are the two edited clips
    /// themselves) and <b>slide</b> (left/right are the moved clip's neighbors, which absorb the
    /// move). A positive delta shifts the boundary later (left grows, right shrinks); negative
    /// shifts it earlier (left shrinks, right grows). Returns 0 if there's no room to shift at all.
    /// </summary>
    /// <param name="leftEndTrim">The left segment's current out-point in its source media.</param>
    /// <param name="leftSourceDuration">The left segment's full source media length (room to grow into).</param>
    /// <param name="leftTrimmedDuration">The left segment's current on-timeline duration (room to shrink from).</param>
    /// <param name="rightStartTrim">The right segment's current in-point in its source media (room to grow backward into).</param>
    /// <param name="rightTrimmedDuration">The right segment's current on-timeline duration (room to shrink from).</param>
    public static double ClampBoundaryShift(
        double delta,
        double leftEndTrim,
        double leftSourceDuration,
        double leftTrimmedDuration,
        double rightStartTrim,
        double rightTrimmedDuration)
    {
        var maxDelta = Math.Min(leftSourceDuration - leftEndTrim, rightTrimmedDuration);
        var minDelta = -Math.Min(leftTrimmedDuration, rightStartTrim);
        if (minDelta > maxDelta) return 0;
        return Math.Clamp(delta, minDelta, maxDelta);
    }
}
