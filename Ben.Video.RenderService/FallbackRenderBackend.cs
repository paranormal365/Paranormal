namespace Ben.Video.RenderService;

/// <summary>
/// Routes each <see cref="RenderJob"/> to <paramref name="primary"/> when
/// <paramref name="primaryAvailable"/> says it's usable, otherwise <paramref name="fallback"/> —
/// item #38 phase 123. Pure C#, no knowledge of what "primary"/"fallback" actually are (the real
/// wiring is native sidecar vs. wasm render worker, done by
/// <c>Ben.Video.Editor.Extensions.ServiceCollectionExtensions</c>), so it's directly unit
/// testable against two fake backends.
///
/// <paramref name="primaryAvailable"/> is re-checked at the start of every job, not cached — a job
/// already in flight against a primary that dies mid-transport just fails normally (the caller,
/// <see cref="BackgroundRenderService.ProcessOneAsync"/>, already turns any exception into a
/// <see cref="RenderJobResult.Failed"/> and applies its existing back-off), and the very next job
/// the queue picks up re-evaluates <paramref name="primaryAvailable"/> fresh and routes to the
/// fallback — so a killed sidecar mid-queue degrades to wasm within one job, not a whole session.
/// </summary>
public sealed class FallbackRenderBackend(
    IRenderBackend primary, IRenderBackend fallback, Func<bool> primaryAvailable) : IRenderBackend
{
    public Task<RenderJobResult> RenderAsync(RenderJob job, IProgress<int> progress, CancellationToken ct)
    {
        var backend = primaryAvailable() ? primary : fallback;
        return backend.RenderAsync(job, progress, ct);
    }

    /// <summary>Tries both backends — segments from either one ultimately live in the same
    /// main-instance MEMFS store, so the redundant call is a harmless already-deleted no-op on
    /// whichever backend didn't produce this particular segment (see each backend's own
    /// <see cref="IRenderBackend.DeleteSegmentAsync"/> doc comment for why that's safe).</summary>
    public async Task DeleteSegmentAsync(string segmentName)
    {
        await primary.DeleteSegmentAsync(segmentName);
        await fallback.DeleteSegmentAsync(segmentName);
    }
}
