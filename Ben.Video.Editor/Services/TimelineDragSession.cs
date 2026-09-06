namespace Ben.Video.Editor.Services;

/// <summary>
/// Where a chip has been dragged to, and whether it is currently snapped.
/// </summary>
/// <param name="Position">The clip's new timeline position in seconds, never negative.</param>
/// <param name="SnapGuidePx">
/// Where to draw the snap guide, in pixels from the start of the lane, or null when the drag is
/// not snapped to anything.
/// </param>
public readonly record struct TimelineDragPosition(double Position, double? SnapGuidePx);

/// <summary>
/// One drag of one chip along its lane.
/// </summary>
/// <remarks>
/// <para>The arithmetic is four lines and every one of them has been wrong at some point: a
/// missing clamp puts a clip at a negative position the ruler cannot draw, a stale origin makes
/// the chip jump on the first move, and the snap guide is drawn from the raw position rather than
/// the snapped one so the line sits beside the clip instead of under it.</para>
///
/// <para>It covers the body drag — the gesture that moves a whole clip. The trim handles do
/// related but different arithmetic against the clip's own edges, and they keep their own code in
/// the component until they are worth the same treatment (2026-09-05 audit, phase 11).</para>
/// </remarks>
public sealed class TimelineDragSession
{
    private readonly double _pointerOriginX;
    private readonly double _originalPosition;
    private readonly double _pxPerSecond;

    private TimelineDragSession(double pointerOriginX, double originalPosition, double pxPerSecond)
    {
        _pointerOriginX   = pointerOriginX;
        _originalPosition = originalPosition;
        _pxPerSecond      = pxPerSecond;
    }

    /// <summary>
    /// Starts a drag from where the pointer went down.
    /// </summary>
    /// <param name="pointerX">Client X of the pointerdown.</param>
    /// <param name="originalPosition">The clip's position in seconds before the drag.</param>
    /// <param name="pxPerSecond">The timeline's current zoom.</param>
    /// <remarks>
    /// The origin is the pointer, not the chip's left edge, so grabbing a clip in the middle moves
    /// it by how far the hand moved rather than snapping its start under the cursor.
    /// </remarks>
    public static TimelineDragSession Begin(double pointerX, double originalPosition, double pxPerSecond) =>
        new(pointerX, Math.Max(0, originalPosition), pxPerSecond);

    /// <summary>
    /// Where the clip sits with the pointer at <paramref name="pointerX"/>.
    /// </summary>
    /// <param name="pointerX">Client X of the pointermove.</param>
    /// <param name="snapTargets">Positions worth snapping to, or empty for none.</param>
    /// <param name="snapping">Whether snapping is switched on at all.</param>
    /// <param name="thresholdSeconds">How close counts as snapped.</param>
    public TimelineDragPosition Move(
        double pointerX,
        IReadOnlyList<double>? snapTargets,
        bool snapping,
        double thresholdSeconds)
    {
        // A zoom of zero would divide the whole drag into infinity; the lane is unusable at that
        // point anyway, so the clip simply stays where it was.
        if (_pxPerSecond <= 0) return new(_originalPosition, null);

        var deltaSeconds = (pointerX - _pointerOriginX) / _pxPerSecond;

        // Nothing lives before zero: a clip dragged off the left edge stops there rather than
        // taking a negative position the ruler cannot draw and export cannot order.
        var raw = Math.Max(0, _originalPosition + deltaSeconds);

        var targets = snapTargets ?? [];

        if (!snapping || targets.Count == 0)
            return new(raw, null);

        var snapped = SnapEngine.Snap(raw, targets, thresholdSeconds);
        var active  = SnapEngine.ActiveSnapTarget(raw, targets, thresholdSeconds);

        // The guide is drawn at the target, which is where the clip now is — drawing it at the raw
        // position would put the line a few pixels beside the edge it is claiming to align.
        return new(snapped, active is { } target ? target * _pxPerSecond : null);
    }
}
