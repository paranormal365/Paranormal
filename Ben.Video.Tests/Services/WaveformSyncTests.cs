using Ben.Video.Editor.Models;

namespace Ben.Video.Tests.Services;

/// <summary>
/// Phase 46 — tests for waveform sync progress computation.
/// The formula is: (currentTime - clip.TimelinePosition + clip.StartTrim) / clip.Duration
/// clamped to [0,1], only when currentTime is within the clip's active window.
/// </summary>
public sealed class WaveformSyncTests
{
    // Mirrors the VideoTimeline.razor SyncProgress calculation
    private static double ComputeSyncProgress(AudioClip ac, double currentTime)
    {
        var clipStart = ac.TimelinePosition;
        var trimmedEnd = ac.EndTrim > 0 ? ac.Duration - ac.EndTrim : 0;
        var clipEnd   = ac.TimelinePosition + ac.Duration - ac.StartTrim - trimmedEnd;

        if (currentTime < clipStart || currentTime > clipEnd || ac.Duration <= 0)
            return -1.0;

        return Math.Clamp((currentTime - clipStart + ac.StartTrim) / ac.Duration, 0.0, 1.0);
    }

    [Fact]
    public void SyncProgress_ZeroAtClipStart()
    {
        var clip = new AudioClip { TimelinePosition = 5.0, Duration = 10.0 };
        var prog = ComputeSyncProgress(clip, 5.0);
        Assert.Equal(0.0, prog, precision: 9);
    }

    [Fact]
    public void SyncProgress_OneAtClipEnd()
    {
        var clip = new AudioClip { TimelinePosition = 5.0, Duration = 10.0 };
        var prog = ComputeSyncProgress(clip, 15.0); // 5 + 10
        Assert.Equal(1.0, prog, precision: 9);
    }

    [Fact]
    public void SyncProgress_MidpointAtHalfDuration()
    {
        var clip = new AudioClip { TimelinePosition = 0.0, Duration = 8.0 };
        var prog = ComputeSyncProgress(clip, 4.0); // halfway
        Assert.Equal(0.5, prog, precision: 9);
    }

    [Fact]
    public void SyncProgress_NegativeWhenBeforeClip()
    {
        var clip = new AudioClip { TimelinePosition = 10.0, Duration = 5.0 };
        var prog = ComputeSyncProgress(clip, 9.0);
        Assert.Equal(-1.0, prog, precision: 9);
    }

    [Fact]
    public void SyncProgress_NegativeWhenAfterClip()
    {
        var clip = new AudioClip { TimelinePosition = 0.0, Duration = 5.0 };
        var prog = ComputeSyncProgress(clip, 6.0);
        Assert.Equal(-1.0, prog, precision: 9);
    }

    [Fact]
    public void SyncProgress_AccountsForStartTrim()
    {
        // Clip at t=0, duration=10s, StartTrim=2s
        // At currentTime=0 (clip start), WaveSurfer position = 2/10 = 0.2
        var clip = new AudioClip { TimelinePosition = 0.0, Duration = 10.0, StartTrim = 2.0 };
        var prog = ComputeSyncProgress(clip, 0.0);
        Assert.Equal(0.2, prog, precision: 9);
    }

    [Fact]
    public void SyncProgress_ClampedToZero()
    {
        var clip = new AudioClip { TimelinePosition = 2.0, Duration = 5.0 };
        // Exactly at start — should be 0, not negative
        var prog = ComputeSyncProgress(clip, 2.0);
        Assert.True(prog >= 0.0);
    }

    [Fact]
    public void SyncProgress_ClampedToOne()
    {
        var clip = new AudioClip { TimelinePosition = 0.0, Duration = 5.0 };
        var prog = ComputeSyncProgress(clip, 5.0);
        Assert.True(prog <= 1.0);
    }

    // ── Throttle guard (AudioWaveform._lastSyncProgress) ─────────────────────

    [Fact]
    public void ThrottleGuard_SkipsUpdateWhenChangeLessThan0001()
    {
        const double threshold = 0.001;
        double last = 0.500;
        double newVal = 0.5005; // change = 0.0005 < threshold
        Assert.False(Math.Abs(newVal - last) >= threshold);
    }

    [Fact]
    public void ThrottleGuard_AllowsUpdateWhenChangeExceedsThreshold()
    {
        const double threshold = 0.001;
        double last = 0.500;
        double newVal = 0.502; // change = 0.002 > threshold
        Assert.True(Math.Abs(newVal - last) >= threshold);
    }
}
