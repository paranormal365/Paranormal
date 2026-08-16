namespace Ben.Video.Sidecar.Jobs;

/// <summary>
/// Tracks how many ffmpeg jobs are currently running, for <c>GET /v1/status</c> and (from phase
/// 123) enforcing <see cref="SidecarOptions.MaxConcurrentJobs"/>. Deliberately minimal in phase
/// 122 — no job ever actually runs yet, this just gives the status endpoint something real to
/// report (0) and gives phase 123 an existing seam to extend rather than a v2 endpoint change.
/// </summary>
public sealed class JobRegistry
{
    private int _activeCount;

    public int ActiveCount => Volatile.Read(ref _activeCount);

    /// <summary>Identity of this sidecar *process*, fresh on every start — item #70 phase 158.
    /// Reported by <c>GET /v1/capabilities</c> so the browser can tell a reconnect-to-the-same-
    /// process apart from a reconnect-to-a-restarted-one. Phase 160's retained-segment ids are
    /// only meaningful within one process lifetime, so a changed id means "drop the whole
    /// index".</summary>
    public Guid InstanceId { get; } = Guid.NewGuid();

    /// <summary>Set once at startup from the health check's own <c>ffmpeg -version</c> probe, so
    /// <c>/v1/status</c> doesn't need to re-run it on every call.</summary>
    public string? LastKnownFfmpegVersion { get; set; }

    public IDisposable EnterJob()
    {
        Interlocked.Increment(ref _activeCount);
        return new JobScope(this);
    }

    private sealed class JobScope(JobRegistry owner) : IDisposable
    {
        public void Dispose() => Interlocked.Decrement(ref owner._activeCount);
    }
}
