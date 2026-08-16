using Ben.Video.Editor.Services;

namespace Ben.Video.Tests.Services;

/// <summary>Item #59-#65 flakiness investigation, phase 143 — <see cref="FfmpegTimeoutPolicy"/>'s
/// pure arithmetic. The constant values themselves aren't asserted against magic numbers (they're
/// tuning knobs, not contracts) — these lock in the *shape* of the policy: a floor, and scaling
/// with clip duration for the one case where duration is free to compute.</summary>
public sealed class FfmpegTimeoutPolicyTests
{
    [Fact]
    public void TrimMs_ShortClip_UsesTheFloorNotTheScaledValue()
    {
        // A 2s trim scaled 4x would be 8s — far below any sane floor, so the floor must win.
        var ms = FfmpegTimeoutPolicy.TrimMs(0, 2);
        Assert.Equal(60_000, ms);
    }

    [Fact]
    public void TrimMs_LongClip_ScalesAboveTheFloor()
    {
        // A 60s clip at 4x scaling = 240s, comfortably above the 60s floor.
        var ms = FfmpegTimeoutPolicy.TrimMs(0, 60);
        Assert.Equal(240_000, ms);
    }

    [Fact]
    public void TrimMs_UsesTheSpanBetweenStartAndEnd_NotEndAlone()
    {
        // A trim from 100s to 102s is a 2s clip, not a 102s one — must use the floor.
        var ms = FfmpegTimeoutPolicy.TrimMs(100, 102);
        Assert.Equal(60_000, ms);
    }

    [Fact]
    public void TrimMs_EndBeforeStart_ClampsToZeroDurationRatherThanGoingNegative()
    {
        var ms = FfmpegTimeoutPolicy.TrimMs(10, 5);
        Assert.Equal(60_000, ms);
    }

    [Fact]
    public void NamedConstants_AreAllPositive()
    {
        Assert.True(FfmpegTimeoutPolicy.ProbeMs > 0);
        Assert.True(FfmpegTimeoutPolicy.ThumbnailFrameMs > 0);
        Assert.True(FfmpegTimeoutPolicy.GenericExecMs > 0);
        Assert.True(FfmpegTimeoutPolicy.ConcatMs > 0);
    }
}
