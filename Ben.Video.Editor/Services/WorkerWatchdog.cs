namespace Ben.Video.Editor.Services;

/// <summary>
/// Item #59-#65 flakiness investigation, phase 143 — detects a genuinely wedged ffmpeg worker
/// command: one that is in flight AND has produced no log line and no progress tick for
/// <see cref="WedgeThreshold"/>. A legitimately slow-but-healthy command (a long export, a big
/// concat) keeps resetting this clock constantly via its own ffmpeg log/progress spew — ffmpeg
/// is chatty during real work — so this only fires for actual silence, which is the rare,
/// diagnostic case: a worker message that's never coming back at all (the failure mode
/// <see cref="FfmpegTimeoutPolicy"/> can't reach, because <c>writeFile</c>/<c>readFile</c>/
/// <c>mount</c>/<c>unmount</c> have no timeout parameter at the library level — only
/// <c>exec</c>/<c>ffprobe</c> do).
///
/// Pure C#, timestamp-driven rather than wall-clock/timer-driven, specifically so it's testable
/// without real waiting — the caller (<see cref="FfmpegService"/>) owns the actual timer that
/// calls <see cref="Evaluate"/> periodically; this class only does the bookkeeping and threshold
/// logic.
/// </summary>
public sealed class WorkerWatchdog
{
    public static readonly TimeSpan DefaultWedgeThreshold = TimeSpan.FromSeconds(45);

    private readonly TimeSpan _wedgeThreshold;
    private DateTime? _commandStartedAtUtc;
    private DateTime _lastActivityUtc;

    public WorkerWatchdog(TimeSpan? wedgeThreshold = null)
    {
        _wedgeThreshold = wedgeThreshold ?? DefaultWedgeThreshold;
        _lastActivityUtc = DateTime.UtcNow;
    }

    /// <summary>True once <see cref="Evaluate"/> has detected a wedge, until <see cref="Reset"/>
    /// or the next <see cref="CommandStarted"/>/<see cref="CommandFinished"/> clears it.</summary>
    public bool IsWedged { get; private set; }

    /// <summary>Fires the moment <see cref="Evaluate"/> transitions from not-wedged to wedged —
    /// not on every subsequent Evaluate call while still wedged.</summary>
    public event Action? OnWedged;

    /// <summary>Call when a worker-touching command begins.</summary>
    public void CommandStarted(DateTime? nowUtc = null)
    {
        var now = nowUtc ?? DateTime.UtcNow;
        _commandStartedAtUtc = now;
        _lastActivityUtc = now;
        IsWedged = false;
    }

    /// <summary>Call when a worker-touching command ends, success or failure alike.</summary>
    public void CommandFinished(DateTime? nowUtc = null)
    {
        _commandStartedAtUtc = null;
        _lastActivityUtc = nowUtc ?? DateTime.UtcNow;
        IsWedged = false;
    }

    /// <summary>Call on any liveness signal from the worker — a log line or a progress tick.</summary>
    public void RecordActivity(DateTime? nowUtc = null) => _lastActivityUtc = nowUtc ?? DateTime.UtcNow;

    /// <summary>
    /// Call periodically. Returns true if a command is currently flagged wedged (including if it
    /// already was before this call) — false if nothing is in flight or activity is recent enough.
    /// </summary>
    public bool Evaluate(DateTime? nowUtc = null)
    {
        var now = nowUtc ?? DateTime.UtcNow;
        if (_commandStartedAtUtc is null) return false;
        if (now - _lastActivityUtc < _wedgeThreshold) return false;

        if (!IsWedged)
        {
            IsWedged = true;
            OnWedged?.Invoke();
        }
        return true;
    }

    /// <summary>Clears any in-flight/wedged tracking — used after a worker reset.</summary>
    public void Reset()
    {
        _commandStartedAtUtc = null;
        _lastActivityUtc = DateTime.UtcNow;
        IsWedged = false;
    }
}
