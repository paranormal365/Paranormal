using Ben.Video.Editor.Services;

namespace Ben.Video.Tests.Services;

public sealed class TrimEditCalculatorTests
{
    // ── ClampSlipDelta ───────────────────────────────────────────────────────

    [Fact]
    public void ClampSlipDelta_WithinBounds_ReturnsUnclamped()
    {
        // StartTrim=2, EndTrim=8, sourceDuration=20 → room to slip +2 (endTrim -> 10 <= 20) fine.
        var result = TrimEditCalculator.ClampSlipDelta(delta: 2, startTrim: 2, endTrim: 8, sourceDuration: 20);
        Assert.Equal(2.0, result, precision: 9);
    }

    [Fact]
    public void ClampSlipDelta_PositiveDelta_ClampsToSourceEnd()
    {
        // EndTrim=8, sourceDuration=10 → max room is +2.
        var result = TrimEditCalculator.ClampSlipDelta(delta: 5, startTrim: 2, endTrim: 8, sourceDuration: 10);
        Assert.Equal(2.0, result, precision: 9);
    }

    [Fact]
    public void ClampSlipDelta_NegativeDelta_ClampsToSourceStart()
    {
        // StartTrim=3 → max backward room is -3.
        var result = TrimEditCalculator.ClampSlipDelta(delta: -10, startTrim: 3, endTrim: 8, sourceDuration: 20);
        Assert.Equal(-3.0, result, precision: 9);
    }

    [Fact]
    public void ClampSlipDelta_NoRoom_ReturnsZero()
    {
        // Clip already uses its entire source length — no room either direction.
        var result = TrimEditCalculator.ClampSlipDelta(delta: 5, startTrim: 0, endTrim: 20, sourceDuration: 20);
        Assert.Equal(0.0, result, precision: 9);
    }

    // ── ClampBoundaryShift ───────────────────────────────────────────────────

    [Fact]
    public void ClampBoundaryShift_WithinBounds_ReturnsUnclamped()
    {
        // Left: EndTrim=10, sourceDuration=20 (room +10), TrimmedDuration=10 (room -10).
        // Right: StartTrim=5 (room -5), TrimmedDuration=8 (room +8).
        var result = TrimEditCalculator.ClampBoundaryShift(
            delta: 3, leftEndTrim: 10, leftSourceDuration: 20, leftTrimmedDuration: 10,
            rightStartTrim: 5, rightTrimmedDuration: 8);
        Assert.Equal(3.0, result, precision: 9);
    }

    [Fact]
    public void ClampBoundaryShift_PositiveDelta_ClampedByLeftSourceRoom()
    {
        // Left only has 2s of source media left to grow into.
        var result = TrimEditCalculator.ClampBoundaryShift(
            delta: 10, leftEndTrim: 18, leftSourceDuration: 20, leftTrimmedDuration: 10,
            rightStartTrim: 5, rightTrimmedDuration: 8);
        Assert.Equal(2.0, result, precision: 9);
    }

    [Fact]
    public void ClampBoundaryShift_PositiveDelta_ClampedByRightDurationRoom()
    {
        // Right only has 3s of on-timeline duration to give up before hitting zero.
        var result = TrimEditCalculator.ClampBoundaryShift(
            delta: 10, leftEndTrim: 5, leftSourceDuration: 100, leftTrimmedDuration: 10,
            rightStartTrim: 5, rightTrimmedDuration: 3);
        Assert.Equal(3.0, result, precision: 9);
    }

    [Fact]
    public void ClampBoundaryShift_NegativeDelta_ClampedByLeftDurationRoom()
    {
        // Left only has 4s of its own duration to give up before hitting zero.
        var result = TrimEditCalculator.ClampBoundaryShift(
            delta: -10, leftEndTrim: 10, leftSourceDuration: 100, leftTrimmedDuration: 4,
            rightStartTrim: 20, rightTrimmedDuration: 8);
        Assert.Equal(-4.0, result, precision: 9);
    }

    [Fact]
    public void ClampBoundaryShift_NegativeDelta_ClampedByRightSourceStartRoom()
    {
        // Right only has 2s of source media before its own StartTrim before hitting 0.
        var result = TrimEditCalculator.ClampBoundaryShift(
            delta: -10, leftEndTrim: 10, leftSourceDuration: 100, leftTrimmedDuration: 10,
            rightStartTrim: 2, rightTrimmedDuration: 8);
        Assert.Equal(-2.0, result, precision: 9);
    }

    [Fact]
    public void ClampBoundaryShift_NoRoomEitherDirection_ReturnsZero()
    {
        var result = TrimEditCalculator.ClampBoundaryShift(
            delta: 5, leftEndTrim: 20, leftSourceDuration: 20, leftTrimmedDuration: 0,
            rightStartTrim: 0, rightTrimmedDuration: 0);
        Assert.Equal(0.0, result, precision: 9);
    }
}
