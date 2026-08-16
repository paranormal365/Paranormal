using Ben.Video.Editor.Models;

namespace Ben.Video.Tests.Models;

public sealed class TransitionDurationClampTests
{
    [Fact]
    public void MaxDurationSeconds_UsesNinetyPercentOfShorterClip()
    {
        Assert.Equal(4.5, TransitionDurationClamp.MaxDurationSeconds(5.0, 10.0), precision: 5);
        Assert.Equal(4.5, TransitionDurationClamp.MaxDurationSeconds(10.0, 5.0), precision: 5);
    }

    [Fact]
    public void MaxDurationSeconds_NeverBelowMinDuration()
    {
        // Two very short clips (0.1s each) would compute below MinDurationSeconds unclamped —
        // the floor must win.
        Assert.Equal(TransitionDurationClamp.MinDurationSeconds,
            TransitionDurationClamp.MaxDurationSeconds(0.1, 0.1));
    }

    [Theory]
    [InlineData(1.0, 5.0, 5.0, 1.0)]   // within range — unchanged
    [InlineData(0.05, 5.0, 5.0, TransitionDurationClamp.MinDurationSeconds)] // below floor — clamped up
    [InlineData(100.0, 5.0, 5.0, 4.5)] // above ceiling (0.9 * 5) — clamped down
    public void Clamp_KeepsRequestedWithinRange(double requested, double fromDur, double toDur, double expected)
    {
        Assert.Equal(expected, TransitionDurationClamp.Clamp(requested, fromDur, toDur), precision: 5);
    }
}
