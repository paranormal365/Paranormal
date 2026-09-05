using Ben.Video.Editor.Models;

namespace Ben.Video.Editor.Services;

/// <summary>
/// Scoped service that owns the shared preview/playback state for the editor session.
///
/// Components subscribe to <see cref="OnStateChanged"/> to re-render when playback
/// progresses or the loaded source changes. All mutations go through this service
/// so the state stays consistent across <c>VideoPreview</c>, <c>VideoTimeline</c>,
/// and <c>VideoEditor</c>.
/// </summary>
public sealed class PlaybackService : IDisposable
{
    // ── State ────────────────────────────────────────────────────────────────

    private PlaybackState _state = new();

    /// <summary>Current immutable snapshot of playback state.</summary>
    public PlaybackState State => _state;

    // ── Events ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Raised whenever any part of <see cref="State"/> changes.
    /// Components call <c>StateHasChanged()</c> in their handler.
    /// </summary>
    public event Action? OnStateChanged;

    /// <summary>
    /// Raised when a component (e.g. the timeline ruler) requests a seek to a specific time.
    /// <c>VideoPreview</c> subscribes and forwards the seek to the underlying &lt;video&gt; element.
    /// </summary>
    public event Action<double>? OnSeekRequested;

    // ── Mutations ────────────────────────────────────────────────────────────

    /// <summary>
    /// Called when a new source (clip or assembled timeline) is loaded into the preview.
    /// Resets time to 0 and marks the preview as paused.
    /// </summary>
    public void NotifyLoaded(PlaybackMode mode, double duration)
    {
        _state = _state with
        {
            Mode       = mode,
            Duration   = duration,
            CurrentTime = 0,
            IsPlaying  = false,
            // Loading the timeline assembly puts the playhead at its start; loading one clip for a
            // look does not move the timeline's playhead at all.
            TimelineTime = mode == PlaybackMode.Timeline ? 0 : _state.TimelineTime,
        };
        Notify();
    }

    /// <summary>
    /// Called on every video <c>timeupdate</c> event from <c>VideoPreview</c>.
    /// </summary>
    public void NotifyTimeUpdate(double currentTime)
    {
        _state = _state with
        {
            CurrentTime  = currentTime,
            TimelineTime = _state.Mode == PlaybackMode.Clip ? _state.TimelineTime : currentTime,
        };
        Notify();
    }

    /// <summary>
    /// Moves the timeline's own playhead without touching whatever the preview happens to be
    /// showing.
    /// </summary>
    /// <remarks>
    /// Used when selecting a clip on the timeline: the playhead goes to that clip's start so the
    /// next split or marker acts where the person is looking, while a clip preview loaded in the
    /// Working Window keeps playing its own thing.
    /// </remarks>
    public void SetTimelineTime(double seconds)
    {
        var clamped = Math.Max(0, seconds);
        if (Math.Abs(clamped - _state.TimelineTime) < 0.0005) return;

        _state = _state with { TimelineTime = clamped };
        Notify();
    }

    /// <summary>Called when the video starts playing.</summary>
    public void NotifyPlaying()
    {
        _state = _state with { IsPlaying = true };
        Notify();
    }

    /// <summary>Called when the video is paused or ends.</summary>
    public void NotifyPaused()
    {
        _state = _state with { IsPlaying = false };
        Notify();
    }

    /// <summary>
    /// Called when the preview is cleared (e.g. all clips removed).
    /// </summary>
    public void NotifyCleared()
    {
        _state = new PlaybackState();
        Notify();
    }

    // ── Session FPS ──────────────────────────────────────────────────────────

    /// <summary>
    /// Working frame rate for the current editing session.
    /// Used by VideoPreview to display frame numbers and for single-frame stepping.
    /// Kept in sync with ExportDialog's frame-rate picker.
    /// Default: 30 fps.
    /// </summary>
    public int SessionFps { get; private set; } = 24;

    /// <summary>Update the working frame rate and notify subscribers.</summary>
    public void SetSessionFps(int fps)
    {
        if (fps == SessionFps) return;
        SessionFps = fps;
        Notify();
    }

    // ── Requests a seek to a specific time ───────────────────────────────────

    /// <summary>
    /// Requests the preview player to seek to <paramref name="seconds"/>.
    /// Raises <see cref="OnSeekRequested"/> so <c>VideoPreview</c> can forward
    /// the seek to the underlying &lt;video&gt; element via JS interop.
    /// Also optimistically updates <see cref="State"/> so the timeline playhead
    /// moves immediately without waiting for a <c>timeupdate</c> event.
    /// </summary>
    public void RequestSeek(double seconds)
    {
        seconds = Math.Max(0, _state.Duration > 0 ? Math.Min(seconds, _state.Duration) : seconds);
        _state  = _state with
        {
            CurrentTime  = seconds,
            TimelineTime = _state.Mode == PlaybackMode.Clip ? _state.TimelineTime : seconds,
        };
        Notify();
        OnSeekRequested?.Invoke(seconds);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Human-readable timecode for a duration in seconds, e.g. "1:23" or "1:02:34".
    /// </summary>
    public static string FormatTime(double seconds)
    {
        if (seconds < 0) seconds = 0;
        var ts = TimeSpan.FromSeconds(seconds);
        return ts.TotalHours >= 1
            ? ts.ToString(@"h\:mm\:ss")
            : ts.ToString(@"m\:ss");
    }

    private void Notify() => OnStateChanged?.Invoke();

    public void Dispose() { /* no unmanaged resources */ }
}
