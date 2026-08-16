namespace Ben.Video.Editor.Models;

/// <summary>
/// Tracks the live state of a single export run.
/// Passed to components so they can render progress without polling the service.
/// </summary>
public sealed class ExportJob
{
    // ── Identity ──────────────────────────────────────────────────────────────

    /// <summary>Unique ID for this job (useful for logging / future job queue).</summary>
    public Guid Id { get; } = Guid.NewGuid();

    /// <summary>Settings snapshot used to start this job.</summary>
    public ExportSettings Settings { get; init; } = new();

    // ── Progress ──────────────────────────────────────────────────────────────

    /// <summary>Overall 0–100 progress across all pipeline phases.</summary>
    public int OverallPercent { get; internal set; }

    /// <summary>Human-readable label for the current phase, e.g. "Trimming clip 2 of 5…".</summary>
    public string PhaseLabel { get; internal set; } = string.Empty;

    /// <summary>Pipeline phases that have completed.</summary>
    public List<string> CompletedPhases { get; } = [];

    // ── State ─────────────────────────────────────────────────────────────────

    public ExportJobState State { get; internal set; } = ExportJobState.Pending;

    /// <summary>Set to true to request cancellation after the current ffmpeg exec finishes.</summary>
    public bool CancelRequested { get; private set; }

    /// <summary>
    /// Audit #1 — a real token so cancellation is observable at every <c>await</c> in the pipeline,
    /// not just at the handful of explicit between-phase <c>ThrowIfCancelled</c> checks.
    ///
    /// <para><b>What this does and does not buy.</b> ffmpeg.wasm's worker runs each command
    /// synchronously with no abort channel (<c>exec()</c> takes only a timeout; the only way to
    /// stop a command mid-flight is <c>terminate()</c>, which destroys the worker and every cached
    /// MEMFS segment). So this token cannot interrupt a command that is already running — what it
    /// does is stop the pipeline at the <i>next</i> command boundary instead of the next
    /// <i>phase</i> boundary, which on a multi-clip export is the difference between "after this
    /// clip" and "after every clip has been trimmed".</para>
    ///
    /// <para>Sidecar-backed work is the exception: cancelling the token aborts the remote job via
    /// <c>DELETE /v1/jobs/{id}</c>, which really does kill the native ffmpeg process tree.</para>
    /// </summary>
    public CancellationToken CancellationToken => _cts.Token;

    private readonly CancellationTokenSource _cts = new();

    /// <summary>Error message if State == Failed.</summary>
    public string? ErrorMessage { get; internal set; }

    /// <summary>Size in bytes of the final rendered output (item #38 phase D — known for free from
    /// the MEMFS→OPFS move, no extra read needed).</summary>
    public long OutputSizeBytes { get; internal set; }

    /// <summary>Duration (seconds) of the final rendered output, from the pipeline's own final
    /// sanity probe — needed by callers that play the result back (item #36 phase 84's
    /// full-quality Preview) rather than just downloading it.</summary>
    public double Duration { get; internal set; }

    /// <summary>
    /// Blob URL for the rendered output, set only when this job ran with
    /// <c>downloadToDisk: false</c> (item #36 phase 84's full-resolution in-memory Preview) — the
    /// job never wrote to disk or triggered a download; the caller is responsible for revoking
    /// this URL (<see cref="FfmpegService.RevokePreviewUrlAsync"/>) once done with it.
    /// </summary>
    public string? PreviewBlobUrl { get; internal set; }

    // ── Timing ────────────────────────────────────────────────────────────────

    public DateTimeOffset StartedAt  { get; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? FinishedAt { get; internal set; }

    public TimeSpan Elapsed => (FinishedAt ?? DateTimeOffset.UtcNow) - StartedAt;

    // ── Actions ───────────────────────────────────────────────────────────────

    /// <summary>Request cancellation. The pipeline stops at the next command boundary (see
    /// <see cref="CancellationToken"/> for why it can't be sooner on the wasm path).</summary>
    public void Cancel()
    {
        CancelRequested = true;
        try { _cts.Cancel(); } catch (ObjectDisposedException) { /* already finished */ }
    }

    /// <summary>Called by the pipeline once the job reaches a terminal state.</summary>
    internal void DisposeCancellation()
    {
        try { _cts.Dispose(); } catch { /* best-effort */ }
    }

    // ── Events ────────────────────────────────────────────────────────────────

    /// <summary>Raised whenever OverallPercent or PhaseLabel changes.</summary>
    public event Action? OnProgress;

    public void NotifyProgress() => OnProgress?.Invoke();
}

public enum ExportJobState
{
    Pending,
    Running,
    Completed,
    Cancelled,
    Failed
}
