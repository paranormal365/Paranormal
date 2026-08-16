using Ben.Video.Editor.Services;

namespace Ben.Video.Tests.Services;

public sealed class TimelineDropCalculatorTests
{
    // ── DropPositionSeconds ───────────────────────────────────────────────────

    [Fact]
    public void DropPositionSeconds_CenterDrop_ReturnsHalfDuration()
    {
        // Track content starts at x=200, width=400 → center drop at x=400
        var result = TimelineDropCalculator.DropPositionSecondsFit(
            dropClientX: 400, trackContentLeft: 200, trackContentWidth: 400, totalDuration: 60);

        Assert.Equal(30.0, result, precision: 9);
    }

    [Fact]
    public void DropPositionSeconds_DropAtLeft_ReturnsZero()
    {
        var result = TimelineDropCalculator.DropPositionSecondsFit(
            dropClientX: 200, trackContentLeft: 200, trackContentWidth: 400, totalDuration: 60);

        Assert.Equal(0.0, result, precision: 9);
    }

    [Fact]
    public void DropPositionSeconds_DropAtRight_ReturnsTotalDuration()
    {
        var result = TimelineDropCalculator.DropPositionSecondsFit(
            dropClientX: 600, trackContentLeft: 200, trackContentWidth: 400, totalDuration: 60);

        Assert.Equal(60.0, result, precision: 9);
    }

    [Fact]
    public void DropPositionSeconds_DropBeyondRight_ClampsToTotalDuration()
    {
        var result = TimelineDropCalculator.DropPositionSecondsFit(
            dropClientX: 9999, trackContentLeft: 200, trackContentWidth: 400, totalDuration: 60);

        Assert.Equal(60.0, result, precision: 9);
    }

    [Fact]
    public void DropPositionSeconds_DropBeyondLeft_ClampsToZero()
    {
        var result = TimelineDropCalculator.DropPositionSecondsFit(
            dropClientX: 0, trackContentLeft: 200, trackContentWidth: 400, totalDuration: 60);

        Assert.Equal(0.0, result, precision: 9);
    }

    [Fact]
    public void DropPositionSeconds_ZeroDuration_ReturnsZero()
    {
        var result = TimelineDropCalculator.DropPositionSecondsFit(
            dropClientX: 400, trackContentLeft: 200, trackContentWidth: 400, totalDuration: 0);

        Assert.Equal(0.0, result, precision: 9);
    }

    [Fact]
    public void DropPositionSeconds_ZeroTrackWidth_ReturnsZero()
    {
        var result = TimelineDropCalculator.DropPositionSecondsFit(
            dropClientX: 400, trackContentLeft: 200, trackContentWidth: 0, totalDuration: 60);

        Assert.Equal(0.0, result, precision: 9);
    }

    [Fact]
    public void DropPositionSeconds_QuarterDrop_ReturnsQuarterDuration()
    {
        // Drop at x=300 — 100px into a 400px track = 25% of 120s = 30s
        var result = TimelineDropCalculator.DropPositionSecondsFit(
            dropClientX: 300, trackContentLeft: 200, trackContentWidth: 400, totalDuration: 120);

        Assert.Equal(30.0, result, precision: 9);
    }

    // ── ResolvePosition ───────────────────────────────────────────────────────

    [Fact]
    public void ResolvePosition_EmptyTrack_ReturnsPreferred()
    {
        var result = TimelineDropCalculator.ResolvePosition(
            preferredPosition: 10.0,
            clipDuration: 5.0,
            existingClips: []);

        Assert.Equal(10.0, result, precision: 9);
    }

    [Fact]
    public void ResolvePosition_NoOverlap_ReturnsPreferred()
    {
        // Existing clip occupies 0–10s; preferred slot is 15–20s → no overlap
        var result = TimelineDropCalculator.ResolvePosition(
            preferredPosition: 15.0,
            clipDuration: 5.0,
            existingClips: [(Start: 0, Duration: 10)]);

        Assert.Equal(15.0, result, precision: 9);
    }

    [Fact]
    public void ResolvePosition_Overlap_FallsBackToEndOfTrack()
    {
        // Existing clip occupies 0–10s; preferred slot 5–10s overlaps
        var result = TimelineDropCalculator.ResolvePosition(
            preferredPosition: 5.0,
            clipDuration: 5.0,
            existingClips: [(Start: 0, Duration: 10)]);

        Assert.Equal(10.0, result, precision: 9);
    }

    [Fact]
    public void ResolvePosition_OverlapMultipleClips_FallsBackToEndOfLastClip()
    {
        // Two existing clips: 0–5s and 8–15s; preferred 3–8s overlaps first
        var result = TimelineDropCalculator.ResolvePosition(
            preferredPosition: 3.0,
            clipDuration: 5.0,
            existingClips: [(Start: 0, Duration: 5), (Start: 8, Duration: 7)]);

        Assert.Equal(15.0, result, precision: 9);
    }

    [Fact]
    public void ResolvePosition_PreferredAtExactGap_NoOverlap_ReturnsPreferred()
    {
        // Gap between 5–8s; dropping a 3s clip at position 5 fills exactly
        var result = TimelineDropCalculator.ResolvePosition(
            preferredPosition: 5.0,
            clipDuration: 3.0,
            existingClips: [(Start: 0, Duration: 5), (Start: 8, Duration: 5)]);

        Assert.Equal(5.0, result, precision: 9);
    }

    [Fact]
    public void ResolvePosition_PreferredAtZeroNoClips_ReturnsZero()
    {
        var result = TimelineDropCalculator.ResolvePosition(
            preferredPosition: 0.0,
            clipDuration: 10.0,
            existingClips: []);

        Assert.Equal(0.0, result, precision: 9);
    }

    [Fact]
    public void ResolvePosition_PartialOverlapAtEnd_FallsBack()
    {
        // Existing 0–10s; preferred 8s start, 5s clip → end at 13 overlaps
        var result = TimelineDropCalculator.ResolvePosition(
            preferredPosition: 8.0,
            clipDuration: 5.0,
            existingClips: [(Start: 0, Duration: 10)]);

        Assert.Equal(10.0, result, precision: 9);
    }

    // ── Overlaps ───────────────────────────────────────────────────────────────

    [Fact]
    public void Overlaps_NoExistingClips_ReturnsFalse()
    {
        Assert.False(TimelineDropCalculator.Overlaps(5.0, 5.0, []));
    }

    [Fact]
    public void Overlaps_NoOverlap_ReturnsFalse()
    {
        Assert.False(TimelineDropCalculator.Overlaps(15.0, 5.0, [(Start: 0, Duration: 10)]));
    }

    [Fact]
    public void Overlaps_PartialOverlap_ReturnsTrue()
    {
        Assert.True(TimelineDropCalculator.Overlaps(8.0, 5.0, [(Start: 0, Duration: 10)]));
    }

    [Fact]
    public void Overlaps_ExactlyTouchingEnd_ReturnsFalse()
    {
        // New clip starts exactly where the existing one ends — touching, not overlapping.
        Assert.False(TimelineDropCalculator.Overlaps(10.0, 5.0, [(Start: 0, Duration: 10)]));
    }

    // ── ResolvePlayheadAnchoredPosition ───────────────────────────────────────

    [Fact]
    public void ResolvePlayheadAnchoredPosition_NoClips_ReturnsPlayhead()
    {
        var result = TimelineDropCalculator.ResolvePlayheadAnchoredPosition(12.0, 5.0, []);
        Assert.Equal(12.0, result, precision: 9);
    }

    [Fact]
    public void ResolvePlayheadAnchoredPosition_NegativePlayhead_ClampsToZero()
    {
        var result = TimelineDropCalculator.ResolvePlayheadAnchoredPosition(-3.0, 5.0, []);
        Assert.Equal(0.0, result, precision: 9);
    }

    [Fact]
    public void ResolvePlayheadAnchoredPosition_PlayheadAtClipEnd_SnapsToTouchAfter()
    {
        // Playhead at 10.05s, existing clip ends at exactly 10s, within the default threshold.
        var result = TimelineDropCalculator.ResolvePlayheadAnchoredPosition(
            10.05, 5.0, [(Start: 0, Duration: 10)]);
        Assert.Equal(10.0, result, precision: 9);
    }

    [Fact]
    public void ResolvePlayheadAnchoredPosition_PlayheadAtClipStart_SnapsToTouchBefore()
    {
        // Playhead at 4.95s, existing clip starts at exactly 5s.
        var result = TimelineDropCalculator.ResolvePlayheadAnchoredPosition(
            4.95, 3.0, [(Start: 5, Duration: 10)]);
        Assert.Equal(5.0, result, precision: 9);
    }

    [Fact]
    public void ResolvePlayheadAnchoredPosition_PlayheadFarFromAnyEdge_ReturnsRawPlayhead()
    {
        var result = TimelineDropCalculator.ResolvePlayheadAnchoredPosition(
            20.0, 5.0, [(Start: 0, Duration: 10)]);
        Assert.Equal(20.0, result, precision: 9);
    }

    [Fact]
    public void ResolvePlayheadAnchoredPosition_CustomThreshold_Respected()
    {
        // 1s away from the clip end — outside the default 0.15s threshold but inside a wider one.
        var result = TimelineDropCalculator.ResolvePlayheadAnchoredPosition(
            11.0, 5.0, [(Start: 0, Duration: 10)], edgeThresholdSeconds: 1.5);
        Assert.Equal(10.0, result, precision: 9);
    }

    // ── DropPositionSeconds zoom/scroll overload ──────────────────────────────

    [Fact]
    public void DropPositionSeconds_Zoom_AtLeft_NoScroll_ReturnsZero()
    {
        // dropClientX == trackContentLeft, scrollLeft == 0 → position 0 s
        var result = TimelineDropCalculator.DropPositionSeconds(
            dropClientX: 200, trackContentLeft: 200, scrollLeft: 0, pxPerSecond: 80);

        Assert.Equal(0.0, result, precision: 9);
    }

    [Fact]
    public void DropPositionSeconds_Zoom_BasicPosition_NoScroll()
    {
        // 160 px past left edge at 80 px/s → 2 s
        var result = TimelineDropCalculator.DropPositionSeconds(
            dropClientX: 360, trackContentLeft: 200, scrollLeft: 0, pxPerSecond: 80);

        Assert.Equal(2.0, result, precision: 9);
    }

    [Fact]
    public void DropPositionSeconds_Zoom_WithScrollLeft_AddsOffset()
    {
        // Pointer at left edge (clientX 200, left 200), but scrollLeft is 160 px
        // → offset = 0 + 160 = 160 px / 80 px/s = 2 s
        var result = TimelineDropCalculator.DropPositionSeconds(
            dropClientX: 200, trackContentLeft: 200, scrollLeft: 160, pxPerSecond: 80);

        Assert.Equal(2.0, result, precision: 9);
    }

    [Fact]
    public void DropPositionSeconds_Zoom_ZeroPxPerSecond_ReturnsZero()
    {
        var result = TimelineDropCalculator.DropPositionSeconds(
            dropClientX: 400, trackContentLeft: 200, scrollLeft: 0, pxPerSecond: 0);

        Assert.Equal(0.0, result, precision: 9);
    }

    [Fact]
    public void DropPositionSeconds_Zoom_Zoom2x_HalvesDuration()
    {
        // At 2× zoom, 80 px = 0.5 s instead of 1 s
        var result = TimelineDropCalculator.DropPositionSeconds(
            dropClientX: 280, trackContentLeft: 200, scrollLeft: 0, pxPerSecond: 160);

        Assert.Equal(0.5, result, precision: 9);
    }
}
