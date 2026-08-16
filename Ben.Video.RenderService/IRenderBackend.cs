namespace Ben.Video.RenderService;

/// <summary>Which encode profile a <see cref="RenderJob"/> should use — item #36 phase D.
/// <see cref="Rough"/> is the fast low-quality pass (same dimensions/fps/audio layout as fine,
/// only preset/CRF differ, so mixed rough/fine segments stay concat-compatible); <see cref="Fine"/>
/// is the current full preview quality.</summary>
public enum RenderPass { Rough, Fine }

/// <summary>One unit of background-render work: render the clip at <see cref="ClipId"/> whose
/// current content signature is <see cref="Signature"/>, at <see cref="Pass"/> quality. The
/// signature travels with the job so the caller can tell, once rendering finishes, whether the
/// region is still current or was edited again mid-render (see <see cref="BackgroundRenderService"/>'s
/// discard-stale-result handling). The pass is NOT part of the signature — a rough render and a
/// fine render of the same content share one signature, with the pass tracked via
/// <see cref="RenderRegion.State"/> instead.</summary>
public sealed record RenderJob(Guid ClipId, string Signature, RenderPass Pass);

/// <summary>Outcome of an <see cref="IRenderBackend"/> render. <see cref="SegmentName"/> is the
/// backend's own storage handle for the result (a MEMFS filename for the real ffmpeg-backed
/// implementation) — opaque to <see cref="BackgroundRenderService"/>, which only threads it
/// through to <see cref="RenderRegionTracker.MarkRendered"/>. <see cref="SizeBytes"/> (item #38
/// phase C) feeds <see cref="SegmentBudget"/>'s cap+LRU tracking — the real backend already has
/// this in hand when it moves the segment into place, so it costs nothing extra to report.</summary>
public sealed record RenderJobResult(bool Success, string? SegmentName = null, string? ErrorMessage = null, long? SizeBytes = null)
{
    public static RenderJobResult Ok(string segmentName, long? sizeBytes = null) => new(true, segmentName, SizeBytes: sizeBytes);
    public static RenderJobResult Failed(string errorMessage) => new(false, ErrorMessage: errorMessage);
}

/// <summary>
/// Abstraction over "actually render a job" — implemented by the real ffmpeg.wasm-backed render
/// worker in <c>Ben.Video.Editor</c>, and by a fake in tests, so <see cref="BackgroundRenderService"/>'s
/// queue/priority/discard/back-off logic is testable without any JS interop or real encoding.
/// </summary>
public interface IRenderBackend
{
    Task<RenderJobResult> RenderAsync(RenderJob job, IProgress<int> progress, CancellationToken ct);

    /// <summary>Deletes a previously produced segment from the backend's storage. Called by
    /// <see cref="BackgroundRenderService"/> when a segment is superseded (a fine render replacing
    /// the rough one, a re-render after an edit) or orphaned (its clip edited/removed while it sat
    /// cached) — without this, worker-side segment files leak for the life of the session (backlog
    /// item #38's memory concern). Must be safe to call with an already-deleted name.</summary>
    Task DeleteSegmentAsync(string segmentName);
}
