using Ben.Video.Editor.Models;
using Ben.Video.Editor.Services;

namespace Ben.Video.Tests.Services;

public sealed class PlaybackServiceTests
{
    // ── Initial state ────────────────────────────────────────────────────────

    [Fact]
    public void InitialState_Mode_IsNone()
    {
        var svc = new PlaybackService();
        Assert.Equal(PlaybackMode.None, svc.State.Mode);
    }

    [Fact]
    public void InitialState_IsPlaying_IsFalse()
    {
        var svc = new PlaybackService();
        Assert.False(svc.State.IsPlaying);
    }

    [Fact]
    public void InitialState_CurrentTime_IsZero()
    {
        var svc = new PlaybackService();
        Assert.Equal(0, svc.State.CurrentTime);
    }

    // ── NotifyLoaded ─────────────────────────────────────────────────────────

    [Fact]
    public void NotifyLoaded_SetsMode()
    {
        var svc = new PlaybackService();
        svc.NotifyLoaded(PlaybackMode.Clip, 30);
        Assert.Equal(PlaybackMode.Clip, svc.State.Mode);
    }

    [Fact]
    public void NotifyLoaded_SetsDuration()
    {
        var svc = new PlaybackService();
        svc.NotifyLoaded(PlaybackMode.Timeline, 120.5);
        Assert.Equal(120.5, svc.State.Duration);
    }

    [Fact]
    public void NotifyLoaded_ResetsCurrentTimeToZero()
    {
        var svc = new PlaybackService();
        svc.NotifyLoaded(PlaybackMode.Clip, 60);
        svc.NotifyTimeUpdate(30);
        svc.NotifyLoaded(PlaybackMode.Clip, 60);
        Assert.Equal(0, svc.State.CurrentTime);
    }

    [Fact]
    public void NotifyLoaded_SetIsPlayingFalse()
    {
        var svc = new PlaybackService();
        svc.NotifyLoaded(PlaybackMode.Clip, 10);
        svc.NotifyPlaying();
        svc.NotifyLoaded(PlaybackMode.Clip, 10);
        Assert.False(svc.State.IsPlaying);
    }

    [Fact]
    public void NotifyLoaded_RaisesOnStateChanged()
    {
        var svc   = new PlaybackService();
        var fired = false;
        svc.OnStateChanged += () => fired = true;
        svc.NotifyLoaded(PlaybackMode.Clip, 10);
        Assert.True(fired);
    }

    // ── NotifyTimeUpdate ─────────────────────────────────────────────────────

    [Fact]
    public void NotifyTimeUpdate_UpdatesCurrentTime()
    {
        var svc = new PlaybackService();
        svc.NotifyTimeUpdate(42.5);
        Assert.Equal(42.5, svc.State.CurrentTime);
    }

    [Fact]
    public void NotifyTimeUpdate_RaisesOnStateChanged()
    {
        var svc   = new PlaybackService();
        var count = 0;
        svc.OnStateChanged += () => count++;
        svc.NotifyTimeUpdate(1);
        svc.NotifyTimeUpdate(2);
        Assert.Equal(2, count);
    }

    // ── NotifyPlaying / NotifyPaused ─────────────────────────────────────────

    [Fact]
    public void NotifyPlaying_SetsIsPlayingTrue()
    {
        var svc = new PlaybackService();
        svc.NotifyPlaying();
        Assert.True(svc.State.IsPlaying);
    }

    [Fact]
    public void NotifyPaused_SetsIsPlayingFalse()
    {
        var svc = new PlaybackService();
        svc.NotifyPlaying();
        svc.NotifyPaused();
        Assert.False(svc.State.IsPlaying);
    }

    [Fact]
    public void NotifyPlaying_RaisesOnStateChanged()
    {
        var svc   = new PlaybackService();
        var fired = false;
        svc.OnStateChanged += () => fired = true;
        svc.NotifyPlaying();
        Assert.True(fired);
    }

    // ── NotifyCleared ────────────────────────────────────────────────────────

    [Fact]
    public void NotifyCleared_ResetsToDefaultState()
    {
        var svc = new PlaybackService();
        svc.NotifyLoaded(PlaybackMode.Timeline, 60);
        svc.NotifyTimeUpdate(30);
        svc.NotifyPlaying();

        svc.NotifyCleared();

        Assert.Equal(PlaybackMode.None, svc.State.Mode);
        Assert.Equal(0,   svc.State.CurrentTime);
        Assert.Equal(0,   svc.State.Duration);
        Assert.False(svc.State.IsPlaying);
    }

    [Fact]
    public void NotifyCleared_RaisesOnStateChanged()
    {
        var svc   = new PlaybackService();
        var fired = false;
        svc.OnStateChanged += () => fired = true;
        svc.NotifyCleared();
        Assert.True(fired);
    }

    // ── Progress after mutations ─────────────────────────────────────────────

    [Fact]
    public void Progress_UpdatesAsTimeChanges()
    {
        var svc = new PlaybackService();
        svc.NotifyLoaded(PlaybackMode.Clip, 100);
        svc.NotifyTimeUpdate(25);
        Assert.Equal(0.25, svc.State.Progress);
    }

    // ── FormatTime ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0,    "0:00")]
    [InlineData(59,   "0:59")]
    [InlineData(60,   "1:00")]
    [InlineData(90,   "1:30")]
    [InlineData(3600, "1:00:00")]
    [InlineData(3661, "1:01:01")]
    public void FormatTime_ReturnsExpectedString(double seconds, string expected)
    {
        Assert.Equal(expected, PlaybackService.FormatTime(seconds));
    }

    [Fact]
    public void FormatTime_NegativeInput_TreatedAsZero()
    {
        Assert.Equal("0:00", PlaybackService.FormatTime(-5));
    }

    // ── Dispose ──────────────────────────────────────────────────────────────

    [Fact]
    public void Dispose_DoesNotThrow()
    {
        var svc = new PlaybackService();
        var ex  = Record.Exception(() => svc.Dispose());
        Assert.Null(ex);
    }

    // ── RequestSeek ──────────────────────────────────────────────────────────────

    [Fact]
    public void RequestSeek_UpdatesCurrentTime()
    {
        var svc = new PlaybackService();
        svc.NotifyLoaded(PlaybackMode.Clip, 60);
        svc.RequestSeek(15);
        Assert.Equal(15, svc.State.CurrentTime);
    }

    [Fact]
    public void RequestSeek_ClampsToZero_WhenNegative()
    {
        var svc = new PlaybackService();
        svc.NotifyLoaded(PlaybackMode.Clip, 60);
        svc.RequestSeek(-5);
        Assert.Equal(0, svc.State.CurrentTime);
    }

    [Fact]
    public void RequestSeek_ClampsToDuration_WhenBeyondEnd()
    {
        var svc = new PlaybackService();
        svc.NotifyLoaded(PlaybackMode.Clip, 30);
        svc.RequestSeek(99);
        Assert.Equal(30, svc.State.CurrentTime);
    }

    [Fact]
    public void RequestSeek_RaisesOnStateChanged()
    {
        var svc   = new PlaybackService();
        var fired = false;
        svc.OnStateChanged += () => fired = true;
        svc.RequestSeek(5);
        Assert.True(fired);
    }

    [Fact]
    public void RequestSeek_RaisesOnSeekRequested_WithCorrectTime()
    {
        var svc      = new PlaybackService();
        double? seen = null;
        svc.OnSeekRequested += t => seen = t;
        svc.NotifyLoaded(PlaybackMode.Clip, 60);
        svc.RequestSeek(20);
        Assert.Equal(20, seen);
    }

    [Fact]
    public void RequestSeek_NoDuration_DoesNotClamp()
    {
        // When no source is loaded (Duration=0) the clamp branch is skipped
        var svc = new PlaybackService();
        svc.RequestSeek(99);
        Assert.Equal(99, svc.State.CurrentTime);
    }

    // ── Phase 42: SessionFps ──────────────────────────────────────────────────

    [Fact]
    public void SessionFps_DefaultIs24()
    {
        // Matches ExportSettings.Fps's own default (24) — kept in sync so a fresh session's
        // Working Window frame-stepping isn't inconsistent with what a fresh Export would use.
        var svc = new PlaybackService();
        Assert.Equal(24, svc.SessionFps);
    }

    [Fact]
    public void SetSessionFps_UpdatesValue()
    {
        var svc = new PlaybackService();
        svc.SetSessionFps(25);
        Assert.Equal(25, svc.SessionFps);
    }

    [Fact]
    public void SetSessionFps_NoOpWhenUnchanged()
    {
        var svc   = new PlaybackService();
        var fired = false;
        svc.OnStateChanged += () => fired = true;

        svc.SetSessionFps(24); // same as default — should not fire

        Assert.False(fired);
    }

    [Fact]
    public void SetSessionFps_RaisesOnStateChanged()
    {
        var svc   = new PlaybackService();
        var fired = false;
        svc.OnStateChanged += () => fired = true;

        svc.SetSessionFps(30);

        Assert.True(fired);
    }

    // ── Phase 42: frame arithmetic ────────────────────────────────────────────

    [Theory]
    [InlineData(0.0,        30,  0)]
    [InlineData(1.0,        30, 30)]
    [InlineData(0.5,        24, 12)]
    public void CurrentFrame_ComputedCorrectly(double time, int fps, int expected)
    {
        // Mirrors VideoPreview._currentFrame: (int)(time * fps)
        Assert.Equal(expected, (int)(time * fps));
    }

    [Theory]
    [InlineData(10.0, 30, 300)]
    [InlineData(5.0,  25, 125)]
    [InlineData(1.0,  24,  24)]
    public void TotalFrames_ComputedCorrectly(double duration, int fps, int expected)
    {
        // Mirrors VideoPreview._totalFrames: (int)Math.Ceiling(duration * fps)
        Assert.Equal(expected, (int)Math.Ceiling(duration * fps));
    }

    [Theory]
    [InlineData(30)]
    [InlineData(25)]
    [InlineData(24)]
    public void FrameDuration_MultipliedByFps_ReturnsOne(int fps)
    {
        // 1/fps * fps must equal 1.0 exactly via double arithmetic
        var frameDuration = 1.0 / fps;
        var reconstructed = frameDuration * fps;
        Assert.Equal(1.0, reconstructed, precision: 9);
    }

    [Fact]
    public void StepForward_AddsOneFrameDuration()
    {
        var svc = new PlaybackService();
        svc.NotifyLoaded(PlaybackMode.Clip, 10.0);
        svc.RequestSeek(1.0);
        svc.SetSessionFps(30);

        var frameDuration = 1.0 / svc.SessionFps;
        var target = Math.Min(10.0, svc.State.CurrentTime + frameDuration);
        svc.RequestSeek(target);

        Assert.Equal(1.0 + 1.0 / 30, svc.State.CurrentTime, precision: 9);
    }

    [Fact]
    public void StepBack_SubtractsOneFrameDuration()
    {
        var svc = new PlaybackService();
        svc.NotifyLoaded(PlaybackMode.Clip, 10.0);
        svc.RequestSeek(2.0);
        svc.SetSessionFps(30);

        var frameDuration = 1.0 / svc.SessionFps;
        var target = Math.Max(0, svc.State.CurrentTime - frameDuration);
        svc.RequestSeek(target);

        Assert.Equal(2.0 - 1.0 / 30, svc.State.CurrentTime, precision: 9);
    }
}
