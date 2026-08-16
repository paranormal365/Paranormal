using Ben.Video.Editor.Models;

namespace Ben.Video.Tests.Models;

public sealed class PlaybackStateTests
{
    [Fact]
    public void Default_Mode_IsNone()
    {
        var s = new PlaybackState();
        Assert.Equal(PlaybackMode.None, s.Mode);
    }

    [Fact]
    public void Default_CurrentTime_IsZero()
    {
        var s = new PlaybackState();
        Assert.Equal(0, s.CurrentTime);
    }

    [Fact]
    public void Default_Duration_IsZero()
    {
        var s = new PlaybackState();
        Assert.Equal(0, s.Duration);
    }

    [Fact]
    public void Default_IsPlaying_IsFalse()
    {
        var s = new PlaybackState();
        Assert.False(s.IsPlaying);
    }

    [Fact]
    public void Progress_WhenDurationZero_ReturnsZero()
    {
        var s = new PlaybackState { CurrentTime = 5, Duration = 0 };
        Assert.Equal(0, s.Progress);
    }

    [Fact]
    public void Progress_WhenHalfWay_ReturnsPointFive()
    {
        var s = new PlaybackState { CurrentTime = 5, Duration = 10 };
        Assert.Equal(0.5, s.Progress);
    }

    [Fact]
    public void Progress_AtEnd_ReturnsOne()
    {
        var s = new PlaybackState { CurrentTime = 10, Duration = 10 };
        Assert.Equal(1.0, s.Progress);
    }

    [Theory]
    [InlineData(PlaybackMode.None,     "")]
    [InlineData(PlaybackMode.Clip,     "Clip Preview")]
    [InlineData(PlaybackMode.Timeline, "Timeline Preview")]
    public void ModeLabel_ReturnsExpected(PlaybackMode mode, string expected)
    {
        var s = new PlaybackState { Mode = mode };
        Assert.Equal(expected, s.ModeLabel);
    }

    [Fact]
    public void Record_WithExpression_ProducesNewInstance()
    {
        var a = new PlaybackState { CurrentTime = 0, Duration = 30 };
        var b = a with { CurrentTime = 15 };

        Assert.Equal(0,  a.CurrentTime);
        Assert.Equal(15, b.CurrentTime);
        Assert.Equal(30, b.Duration);
    }

    [Fact]
    public void RecordEquality_SameValues_AreEqual()
    {
        var a = new PlaybackState { Mode = PlaybackMode.Clip, Duration = 10, CurrentTime = 5 };
        var b = new PlaybackState { Mode = PlaybackMode.Clip, Duration = 10, CurrentTime = 5 };
        Assert.Equal(a, b);
    }

    [Fact]
    public void RecordEquality_DifferentValues_AreNotEqual()
    {
        var a = new PlaybackState { CurrentTime = 1 };
        var b = new PlaybackState { CurrentTime = 2 };
        Assert.NotEqual(a, b);
    }
}
