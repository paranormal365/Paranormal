namespace Ben.Video.Editor.Services;

/// <summary>
/// Item #59-#65 flakiness investigation, phase 143 — timeout values (ms) passed straight through
/// to <c>@ffmpeg/ffmpeg</c>'s own <c>exec(args, timeoutMs)</c>/<c>ffprobe(args, timeoutMs)</c>,
/// which up to now were always called with the library's default of <c>-1</c> (infinite).
///
/// Live-verified 2026-08-13 (see README-phase-143.md): a core-level timeout abort resolves the
/// promise gracefully with a non-zero exit code — it does NOT reject, and the worker fully
/// survives (a follow-up command on the same instance succeeded in 7ms right after). That means
/// every timeout configured here is caught by the existing <c>ThrowIfFailed</c> path exactly like
/// any other failed command; no new state-machine branch was needed for "timed out" as distinct
/// from "failed". These values exist purely so a genuinely wedged command (the case the library's
/// own timeout mechanism can't self-heal — the underlying WASM/emscripten loop truly never
/// reaching its own periodic check) has a bound at all, rather than the previous <c>-1</c>.
///
/// Only <c>exec</c>/<c>ffprobe</c> accept a timeout at the library level — writeFile/readFile/
/// mount/unmount/deleteFile/rename have none. Those are covered by <see cref="WorkerWatchdog"/>
/// instead, not by a value here.
/// </summary>
public static class FfmpegTimeoutPolicy
{
    /// <summary>ffprobe metadata extraction — always a quick, bounded read of stream headers.</summary>
    public const int ProbeMs = 30_000;

    /// <summary>Per-frame budget for a single thumbnail extraction exec.</summary>
    public const int ThumbnailFrameMs = 15_000;

    /// <summary>
    /// Phase 145 — extractThumbnails now generates every frame in one exec call (multiple
    /// -ss/-frames:v groups after a single -i), not one exec per frame, so its budget scales with
    /// how many frames were requested rather than using <see cref="ThumbnailFrameMs"/> directly.
    /// </summary>
    public static int ThumbnailBatchMs(int frameCount) => Math.Max(ThumbnailFrameMs, frameCount * 8_000);

    /// <summary>
    /// Generic exec/filter-complex/xfade/drawtext/mixAudio calls where no clip duration is known
    /// at this layer — a flat, generous ceiling rather than an accurate k×duration estimate.
    /// </summary>
    public const int GenericExecMs = 90_000;

    /// <summary>
    /// Concat (re-encode or stream-copy) — segment durations aren't available at this layer either
    /// (only names), so this is a flat ceiling generous enough for a multi-minute timeline.
    /// </summary>
    public const int ConcatMs = 120_000;

    /// <summary>
    /// Trim re-encode — the one case where an accurate duration-based timeout is basically free,
    /// since <c>TrimClipAsync</c> already receives start/end seconds. Real-time re-encoding on a
    /// slow device can run slower than 1x, so this budgets a 4x multiplier with a 60s floor.
    /// </summary>
    public static int TrimMs(double startSec, double endSec)
    {
        var durationSec = Math.Max(0, endSec - startSec);
        return (int)Math.Max(60_000, durationSec * 1000 * 4);
    }
}
