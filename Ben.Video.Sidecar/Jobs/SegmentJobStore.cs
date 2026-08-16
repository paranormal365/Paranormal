using System.Collections.Concurrent;
using Ben.Video.Core.SidecarContracts;

namespace Ben.Video.Sidecar.Jobs;

/// <summary>Mutable state for one in-flight or finished segment-render job, keyed by
/// <see cref="Id"/>. Lives only in memory — a sidecar restart drops every job, which is fine: the
/// browser always re-polls <c>GET /v1/jobs/{id}</c> and treats a 404 as "gone, resubmit" (item
/// #38 phase F, <c>NativeSidecarBackend</c>).</summary>
public sealed class SegmentJobRecord
{
    public required Guid Id { get; init; }
    public JobState State { get; set; } = JobState.Running;
    public int ProgressPercent { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ResultPath { get; set; }
    public long? ResultSizeBytes { get; set; }

    /// <summary>
    /// For multi-file job kinds (thumbnails, item #70 phase 159) — the ordered set of produced
    /// files, all inside this job's own working directory. Null for single-file kinds (segment),
    /// which keep using <see cref="ResultPath"/> and stream their one file directly, so the
    /// phase-123 result contract is completely unchanged.
    ///
    /// <para>This list is also the <b>authorization list</b> for
    /// <c>GET /v1/jobs/{id}/result/{name}</c>: a requested name is served only if it appears here
    /// verbatim, so a traversal attempt can never resolve to a path this job didn't produce.</para>
    /// </summary>
    public IReadOnlyList<string>? ResultFileNames { get; set; }

    /// <summary>Item #70 phase 160 — set when the job asked for retention and the sidecar kept its
    /// own copy of the output. Surfaced additively on <see cref="JobStatusInfo"/>.</summary>
    public Guid? RetainedSegmentId { get; set; }

    /// <summary>Absolute directory the files in <see cref="ResultFileNames"/> live in.</summary>
    public string? ResultDirectory { get; set; }

    /// <summary>Cancelled by <c>DELETE /v1/jobs/{id}</c> so an abandoned job's ffmpeg process is
    /// actually killed (via <see cref="FfmpegRunner"/>'s existing kill-tree-on-cancellation path)
    /// instead of running to completion for nothing.</summary>
    public CancellationTokenSource Cts { get; } = new();
}

/// <summary>In-memory registry of segment-render jobs — singleton, thread-safe. Deliberately no
/// persistence and no cross-process visibility: a job only ever matters to the one browser tab
/// that created it, for the lifetime of one sidecar process run.</summary>
public sealed class SegmentJobStore
{
    private readonly ConcurrentDictionary<Guid, SegmentJobRecord> _jobs = new();

    public SegmentJobRecord Create()
    {
        var record = new SegmentJobRecord { Id = Guid.NewGuid() };
        _jobs[record.Id] = record;
        return record;
    }

    public SegmentJobRecord? Get(Guid id) => _jobs.GetValueOrDefault(id);

    public bool Remove(Guid id) => _jobs.TryRemove(id, out _);
}
