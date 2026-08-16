namespace Ben.Video.Editor.Services;

/// <summary>
/// Pure static helper that converts an HTML drag-drop event's client X coordinate
/// into a timeline position in seconds.
/// Isolated from Blazor/JSInterop so it can be unit-tested without a browser.
/// </summary>
public static class TimelineDropCalculator
{
    /// <summary>
    /// Converts a pointer X position into a timeline position in seconds.
    /// </summary>
    /// <param name="dropClientX">
    /// The <c>clientX</c> value from the drop event (viewport pixels).
    /// </param>
    /// <param name="trackContentLeft">
    /// The left edge of the track content area in viewport pixels
    /// (i.e. the bounding-rect left of the scrollable clip area, after the label gutter).
    /// </param>
    /// <param name="trackContentWidth">
    /// The pixel width of the track content area.
    /// </param>
    /// <param name="totalDuration">
    /// Total timeline duration in seconds.
    /// </param>
    /// <returns>
    /// A clamped timeline position in seconds [0, <paramref name="totalDuration"/>].
    /// Returns 0 when <paramref name="totalDuration"/> or <paramref name="trackContentWidth"/>
    /// are zero or negative.
    /// </returns>
    /// <summary>
    /// Converts a pointer X position into a timeline position in seconds
    /// using the legacy fit-to-width (non-zoom) mode.
    /// Prefer <see cref="DropPositionSeconds(double,double,double,double)"/> (scrollLeft/pxPerSecond overload)
    /// for zoom-aware positioning.
    /// </summary>
    public static double DropPositionSecondsFit(
        double dropClientX,
        double trackContentLeft,
        double trackContentWidth,
        double totalDuration)
    {
        if (totalDuration <= 0 || trackContentWidth <= 0)
            return 0;

        var ratio    = (dropClientX - trackContentLeft) / trackContentWidth;
        var clamped  = Math.Clamp(ratio, 0.0, 1.0);
        return clamped * totalDuration;
    }

    /// <summary>
    /// Converts a pointer X position into a timeline position in seconds,
    /// accounting for the current zoom scale and horizontal scroll offset.
    /// </summary>
    /// <param name="dropClientX">The <c>clientX</c> value from the drop event (viewport pixels).</param>
    /// <param name="trackContentLeft">Viewport-relative left edge of the track content area.</param>
    /// <param name="scrollLeft">Current horizontal scroll offset of the track content area in pixels.</param>
    /// <param name="pxPerSecond">Rendered pixels per second at the current zoom (ZoomScale × BasePxPerSecond).</param>
    /// <returns>A clamped timeline position in seconds [0, ∞).</returns>
    public static double DropPositionSeconds(
        double dropClientX,
        double trackContentLeft,
        double scrollLeft,
        double pxPerSecond)
    {
        if (pxPerSecond <= 0)
            return 0;

        var offsetPx = dropClientX - trackContentLeft + scrollLeft;
        return Math.Max(0, offsetPx / pxPerSecond);
    }

    /// <summary>
    /// Finds the first position (in seconds) at which a clip of
    /// <paramref name="clipDuration"/> seconds fits without overlapping any existing
    /// clip on the track, starting from <paramref name="preferredPosition"/>.
    ///
    /// If the preferred position is free, it is returned unchanged.
    /// If not, the clip is placed immediately after the last occupied second on the track.
    /// </summary>
    /// <param name="preferredPosition">The desired drop position in seconds.</param>
    /// <param name="clipDuration">The effective duration of the clip being placed.</param>
    /// <param name="existingClips">
    /// Sequence of (startSeconds, durationSeconds) tuples for clips already on the track.
    /// </param>
    public static double ResolvePosition(
        double preferredPosition,
        double clipDuration,
        IEnumerable<(double Start, double Duration)> existingClips)
    {
        var clips = existingClips.OrderBy(c => c.Start).ToList();

        if (clips.Count == 0)
            return preferredPosition;

        // Check whether the preferred slot is free
        var end = preferredPosition + clipDuration;
        bool overlaps = clips.Any(c =>
            c.Start < end && (c.Start + c.Duration) > preferredPosition);

        if (!overlaps)
            return preferredPosition;

        // Fallback: place after the last occupied position
        var lastEnd = clips.Max(c => c.Start + c.Duration);
        return lastEnd;
    }

    /// <summary>
    /// True if a clip of <paramref name="duration"/> seconds placed at
    /// <paramref name="position"/> would overlap any of <paramref name="existingClips"/>.
    /// </summary>
    public static bool Overlaps(
        double position,
        double duration,
        IEnumerable<(double Start, double Duration)> existingClips)
    {
        var end = position + duration;
        return existingClips.Any(c => c.Start < end && (c.Start + c.Duration) > position);
    }

    /// <summary>
    /// Resolves where a newly-added clip (e.g. "Add to Timeline") should land based on the
    /// playhead, mirroring how a user would expect an insert-at-playhead edit to snap to
    /// adjacent clip edges rather than landing at an arbitrary mid-clip offset (item #25).
    ///
    /// If the playhead sits within <paramref name="edgeThresholdSeconds"/> of an existing clip's
    /// end, the new clip is anchored to start exactly there (touching, after). If it instead sits
    /// within the threshold of a clip's start, the new clip is anchored to start exactly there
    /// too (touching, before) — which by definition overlaps that clip, signaling the caller to
    /// treat this as an insert requiring <see cref="Overlaps"/>-driven "make room" handling. If
    /// the playhead isn't near any edge, it's used as-is (clamped to non-negative).
    /// </summary>
    public static double ResolvePlayheadAnchoredPosition(
        double playhead,
        double clipDuration,
        IEnumerable<(double Start, double Duration)> existingClips,
        double edgeThresholdSeconds = 0.15)
    {
        foreach (var c in existingClips.OrderBy(c => c.Start))
        {
            var end = c.Start + c.Duration;
            if (Math.Abs(playhead - end) <= edgeThresholdSeconds)
                return end;
            if (Math.Abs(playhead - c.Start) <= edgeThresholdSeconds)
                return c.Start;
        }

        return Math.Max(0, playhead);
    }
}
