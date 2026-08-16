namespace Ben.Video.RenderService;

/// <summary>
/// Owns the background-render work queue: a single consumer feeding one <see cref="IRenderBackend"/>,
/// with hybrid priority (explicit <see cref="RequestPriority"/> requests first, FIFO; then stale
/// regions ordered by distance from the playhead) — see item #36 design doc §6. Pure C#, no
/// UI/JS-interop — <see cref="PickNext"/> and <see cref="ProcessOneAsync"/> are directly unit
/// testable against a fake <see cref="IRenderBackend"/>, without any async loop or timing control.
/// </summary>
public sealed class BackgroundRenderService : IAsyncDisposable
{
    private readonly RenderRegionTracker _tracker;
    private readonly IRenderBackend _backend;
    private readonly Func<double> _getPlayheadTime;
    private readonly SegmentBudget _budget = new();

    private readonly List<Guid> _priorityQueue = [];
    private readonly HashSet<string> _failedSignatures = [];
    private readonly SemaphoreSlim _signal = new(0, int.MaxValue);
    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;
    private volatile bool _paused;

    public bool IsPaused => _paused;

    /// <summary>
    /// Raised when a single loop iteration (<see cref="PickNext"/>, orphan-segment cleanup, or a
    /// <see cref="ProcessOneAsync"/> call that somehow escapes its own broader try/catch) throws
    /// anything other than <see cref="OperationCanceledException"/> — item #9's "clip-art stall"
    /// finding: <see cref="LoopAsync"/> previously only caught cancellation at the outer loop
    /// level, so any other unhandled exception anywhere in a single iteration silently killed
    /// <c>_loopTask</c> for the rest of the session (nothing observes a faulted background task
    /// outside <see cref="DisposeAsync"/>, never called in normal operation) — matching the
    /// reported symptom exactly: one job completes, then permanently nothing, zero errors visible
    /// anywhere. This event exists so the host (which has real logging; this project deliberately
    /// stays dependency-free) can surface it instead of the failure being invisible again. The loop
    /// itself now always continues to the next iteration regardless.
    /// </summary>
    public event Action<Exception>? OnLoopError;

    /// <summary>
    /// Soft cap (item #36 §8 / item #38 phase C) on how many bytes of background-rendered segments
    /// stay resident in the main instance's MEMFS at once. Checked right after every successful
    /// render, evicting least-recently-touched segments — never the region under the current
    /// playhead, never a region mid-render. Default 256 MB, matching the original design doc's
    /// number. Runtime-mutable like <see cref="EnableRoughPass"/>.
    /// </summary>
    public long SegmentCapBytes { get; set; } = 256L * 1024 * 1024;

    /// <summary>Current tracked byte total across all background segments — exposed for tests and
    /// a possible future memory indicator.</summary>
    public long TrackedBytes => _budget.TotalBytes;

    /// <summary>
    /// When <c>false</c>, every stale region renders straight to its FINE pass — the ROUGH pass is
    /// skipped entirely, matching pre-phase-D single-pass behavior. Default <c>true</c>. Runtime-
    /// mutable like <see cref="Pause"/>/<see cref="Resume"/> so a Settings-Lab toggle can flip it
    /// on an already-running instance, not just at construction (item #36 phase E).
    /// </summary>
    public bool EnableRoughPass { get; set; } = true;

    public BackgroundRenderService(RenderRegionTracker tracker, IRenderBackend backend, Func<double> getPlayheadTime)
    {
        _tracker         = tracker;
        _backend         = backend;
        _getPlayheadTime = getPlayheadTime;
        _tracker.OnChanged += () => Signal();
    }

    /// <summary>Starts the background loop. Safe to call more than once — subsequent calls are a
    /// no-op while already running.</summary>
    public void Start()
    {
        if (_loopTask is not null) return;
        _loopCts = new CancellationTokenSource();
        _loopTask = LoopAsync(_loopCts.Token);
        Signal(); // pick up any regions already stale before Start() was called
    }

    /// <summary>Requests a specific clip be rendered next, ahead of ambient (playhead-distance)
    /// ordering — e.g. the user clicked a gray region, or Preview needs it right now. FIFO among
    /// multiple explicit requests. The entry persists through the rough pass and is only consumed
    /// when the clip's FINE pass starts — a priority request means "get this clip fully rendered",
    /// not just roughed. No-op if the clip has no renderable work.</summary>
    public void RequestPriority(Guid clipId)
    {
        if (!_priorityQueue.Contains(clipId))
            _priorityQueue.Add(clipId);
        Signal();
    }

    /// <summary>Stops starting new jobs (an in-flight job still finishes) — e.g. while Export is
    /// actively running, so the two don't compete for the same CPU on the user's machine.</summary>
    public void Pause() => _paused = true;

    public void Resume()
    {
        _paused = false;
        Signal();
    }

    private void Signal()
    {
        // Cap pending signals at 1 — PickNext() always reads live state, so multiple queued
        // signals would only cause redundant wake-ups, never additional work.
        if (_signal.CurrentCount == 0) _signal.Release();
    }

    /// <summary>
    /// Picks the next region to render, or null if there's nothing to do right now. Two-pass
    /// scheduling (item #36 phase D): every <see cref="RenderRegionState.Stale"/> region gets its
    /// ROUGH pass before any region gets a FINE pass — the whole timeline becomes playable at
    /// rough quality as fast as possible, then sharpens — with explicit priority requests honored
    /// first within each tier. The pass to run is derived from the region's state
    /// (Stale → rough, Rough → fine), not stored anywhere — except when
    /// <see cref="EnableRoughPass"/> is off, in which case <see cref="ProcessOneAsync"/> runs the
    /// FINE pass directly for a Stale region and it never visits <see cref="RenderRegionState.Rough"/>
    /// at all; this method's own region *selection* (which state is "actionable") is unaffected.
    /// </summary>
    internal RenderRegion? PickNext()
    {
        var regions = _tracker.Regions;

        // Tier 1: explicit requests, FIFO — a stale region needs its rough pass first;
        // a rough region needs its fine pass. Either counts as actionable.
        foreach (var clipId in _priorityQueue)
        {
            var region = regions.FirstOrDefault(r =>
                r.ClipId == clipId
                && r.State is RenderRegionState.Stale or RenderRegionState.Rough
                && !_failedSignatures.Contains(r.Signature));
            if (region is not null) return region;
        }

        // Tier 2: ambient stale regions (rough pass), nearest the playhead first.
        // Tier 3: ambient rough regions (fine pass), same ordering, only when nothing is stale.
        var playhead = _getPlayheadTime();
        foreach (var state in (ReadOnlySpan<RenderRegionState>)[RenderRegionState.Stale, RenderRegionState.Rough])
        {
            var candidates = regions.Where(r => r.State == state && !_failedSignatures.Contains(r.Signature)).ToList();
            if (candidates.Count > 0)
                return candidates
                    .OrderBy(r => DistanceFromPlayhead(r, playhead))
                    .ThenBy(r => r.Start)
                    .First();
        }

        return null;
    }

    private static double DistanceFromPlayhead(RenderRegion region, double playhead)
    {
        if (playhead < region.Start) return region.Start - playhead;
        var end = region.Start + region.Duration;
        if (playhead > end) return playhead - end;
        return 0; // playhead is inside the region
    }

    /// <summary>Runs exactly one job to completion (or discard) and updates the tracker. The pass
    /// is derived from the region's state: <see cref="RenderRegionState.Stale"/> → rough,
    /// <see cref="RenderRegionState.Rough"/> → fine. Exposed internally so tests can drive the
    /// queue deterministically without the perpetual loop.</summary>
    internal async Task ProcessOneAsync(RenderRegion region, CancellationToken ct)
    {
        var pass = region.State == RenderRegionState.Rough || !EnableRoughPass
            ? RenderPass.Fine
            : RenderPass.Rough;

        // A priority request means "get this clip fully rendered" — it survives the rough pass
        // and is consumed only when the fine pass (the work that completes the request) starts.
        if (pass == RenderPass.Fine)
            _priorityQueue.Remove(region.ClipId);

        var previousSegment = region.SegmentName; // the rough segment a fine pass will supersede
        var job = new RenderJob(region.ClipId, region.Signature, pass);
        var renderingState = pass == RenderPass.Rough ? RenderRegionState.RenderingRough : RenderRegionState.RenderingFine;
        _tracker.MarkProgress(region.ClipId, renderingState, 0);
        var progress = new Progress<int>(pct => _tracker.MarkProgress(region.ClipId, renderingState, pct));

        RenderJobResult result;
        try
        {
            result = await _backend.RenderAsync(job, progress, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            result = RenderJobResult.Failed(ex.Message);
        }

        // Discard if the region moved on (edited again) or vanished while this job was running —
        // the result no longer describes current content. The just-produced segment is deleted
        // here too: nothing will ever consume it, so keeping it is a pure leak.
        var current = _tracker.Regions.FirstOrDefault(r => r.ClipId == region.ClipId);
        if (current is null || current.Signature != job.Signature)
        {
            if (result.Success && result.SegmentName is not null)
                await TryDeleteSegmentAsync(result.SegmentName);
            return;
        }

        if (result.Success && result.SegmentName is not null)
        {
            var resultState = pass == RenderPass.Rough ? RenderRegionState.Rough : RenderRegionState.Fine;
            _tracker.MarkRendered(region.ClipId, job.Signature, result.SegmentName, resultState);
            _budget.Track(result.SegmentName, region.ClipId, result.SizeBytes ?? 0, isRough: pass == RenderPass.Rough);

            // A fine result supersedes the rough segment for the same content — delete it.
            if (previousSegment is not null && previousSegment != result.SegmentName)
                await TryDeleteSegmentAsync(previousSegment);

            await EnforceBudgetAsync();
        }
        else if (pass == RenderPass.Rough)
        {
            // Back off: mark this exact signature as failed so PickNext skips it until the user
            // changes the clip again (a new signature is never in _failedSignatures) — otherwise
            // a persistently-failing clip would be retried every loop iteration forever.
            _failedSignatures.Add(job.Signature);
            _tracker.MarkProgress(region.ClipId, RenderRegionState.Stale, 0);
        }
        else
        {
            // Fine pass failed but the rough segment is still valid content — keep the region
            // usable at rough quality rather than throwing that away. Same signature back-off
            // applies so the failing fine encode isn't retried until the clip changes.
            _failedSignatures.Add(job.Signature);
            _tracker.MarkRendered(region.ClipId, job.Signature, previousSegment, RenderRegionState.Rough);
        }
    }

    private int _deletionHoldDepth;
    private readonly List<string> _heldDeletions = [];

    /// <summary>
    /// Suspends segment deletion until <see cref="EndDeletionHoldAsync"/>. The Preview assembly
    /// path holds this while it runs: a region's fine pass completing MID-assembly would otherwise
    /// delete the superseded rough segment at the exact moment the concat is about to read it
    /// (live-found race). Deletions requested during a hold are queued and flushed on release.
    /// Re-entrant (depth-counted).
    /// </summary>
    public void BeginDeletionHold() => _deletionHoldDepth++;

    public async Task EndDeletionHoldAsync()
    {
        if (_deletionHoldDepth == 0 || --_deletionHoldDepth > 0) return;
        var held = new List<string>(_heldDeletions);
        _heldDeletions.Clear();
        foreach (var segment in held)
        {
            try { await _backend.DeleteSegmentAsync(segment); } catch { }
        }
    }

    private async Task TryDeleteSegmentAsync(string segmentName)
    {
        _budget.Untrack(segmentName);
        if (_deletionHoldDepth > 0)
        {
            _heldDeletions.Add(segmentName);
            return;
        }
        try { await _backend.DeleteSegmentAsync(segmentName); }
        catch { /* best-effort cleanup — a leaked segment is preferable to a crashed loop */ }
    }

    /// <summary>
    /// Marks a background segment as recently used without re-rendering it — called by the host
    /// (<c>VideoEditor.TryConsumeBackgroundSegment</c>) when a Preview assembly reuses an
    /// already-rendered segment, so LRU eviction doesn't treat "not recently re-rendered" as
    /// "not recently used." No-op if the segment isn't tracked (e.g. background rendering is off).
    /// </summary>
    public void TouchSegment(string segmentName) => _budget.Touch(segmentName);

    /// <summary>
    /// Evicts least-recently-touched background segments down to <see cref="SegmentCapBytes"/> —
    /// item #36 §8 / item #38 phase C. Skipped entirely while a deletion hold is active (deferred
    /// to the next successful render instead of reasoning through a new interaction with the
    /// existing Preview-assembly race guard); the region under the current playhead and any region
    /// currently mid-render are never eviction candidates.
    /// </summary>
    private async Task EnforceBudgetAsync()
    {
        if (_deletionHoldDepth > 0) return;

        var playhead = _getPlayheadTime();
        var protectedIds = new HashSet<Guid>();
        foreach (var r in _tracker.Regions)
        {
            if (r.State is RenderRegionState.RenderingRough or RenderRegionState.RenderingFine)
                protectedIds.Add(r.ClipId);
            else if (playhead >= r.Start && playhead <= r.Start + r.Duration)
                protectedIds.Add(r.ClipId);
        }

        foreach (var (segmentName, clipId) in _budget.PickEvictions(SegmentCapBytes, protectedIds))
        {
            await TryDeleteSegmentAsync(segmentName);
            _tracker.MarkEvicted(clipId);
        }
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                // Item #9 — a single bad iteration (an unexpected exception anywhere in the body
                // below: PickNext, orphan cleanup, or a ProcessOneAsync call that somehow escapes
                // its own broader try/catch) must never kill the whole loop for the rest of the
                // session — see OnLoopError's own doc comment for the full story. Cancellation
                // itself still propagates to the outer catch below, exactly as before.
                RenderRegion? inFlight = null;
                try
                {
                    // Bounded wait, not a pure signal-driven block: OnChanged fires (and Signal()
                    // runs) many times during a single render — once per progress tick — so a
                    // signal can already be "spent" catching up on stale progress noise at the
                    // exact moment a fresh edit's own Signal() call lands, with nothing left to
                    // wake the wait that edit needed. The periodic timeout re-checks PickNext()
                    // regardless, so a missed wake-up self-heals within IdlePollInterval instead
                    // of stalling indefinitely.
                    await _signal.WaitAsync(IdlePollInterval, ct);

                    // Delete worker-side segments orphaned by edits/removals since the last
                    // wake-up (see RenderRegionTracker.DrainOrphanedSegments) — closes a
                    // session-long leak.
                    foreach (var orphan in _tracker.DrainOrphanedSegments())
                        await TryDeleteSegmentAsync(orphan);

                    while (!ct.IsCancellationRequested && !_paused)
                    {
                        var next = PickNext();
                        if (next is null) break;
                        inFlight = next; // tracked so the catch below can un-stick it on failure
                        await ProcessOneAsync(next, ct);
                        inFlight = null;
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw; // Stop()/DisposeAsync() — let the outer catch below handle it.
                }
                catch (Exception ex)
                {
                    // Covers a backend throwing OperationCanceledException for its own internal
                    // reasons unrelated to our ct too (the filter above only re-throws when ct
                    // itself is actually cancelled) — otherwise that would look identical to a
                    // deliberate Stop() and silently kill the loop the same way item #9 did.
                    //
                    // If a region was mid-render when this happened, ProcessOneAsync already
                    // moved it to RenderingRough/RenderingFine (via MarkProgress) before the
                    // exception escaped — unlike every failure ProcessOneAsync catches itself
                    // (which resets to Stale + backs off via _failedSignatures), an exception that
                    // escapes ProcessOneAsync entirely never gets that treatment, leaving the
                    // region permanently stuck in a Rendering* state that PickNext never selects —
                    // a real second bug found while writing this fix's own regression test (the
                    // loop surviving isn't enough if the region it was working on becomes
                    // invisible forever). Mirror ProcessOneAsync's own rough-pass-failure handling
                    // exactly so this failure mode behaves identically to every other one.
                    if (inFlight is not null)
                    {
                        _failedSignatures.Add(inFlight.Signature);
                        _tracker.MarkProgress(inFlight.ClipId, RenderRegionState.Stale, 0);
                    }
                    OnLoopError?.Invoke(ex);
                }
            }
        }
        catch (OperationCanceledException) { /* Stop()/DisposeAsync() */ }
    }

    /// <summary>Upper bound on how long a missed wake-up signal can stall the queue — see
    /// <see cref="LoopAsync"/>. Internal + settable so tests can shrink it instead of waiting
    /// out the real interval.</summary>
    internal TimeSpan IdlePollInterval { get; set; } = TimeSpan.FromMilliseconds(750);

    public async ValueTask DisposeAsync()
    {
        _loopCts?.Cancel();
        if (_loopTask is not null)
        {
            try { await _loopTask; } catch { }
        }
        _loopCts?.Dispose();
        _signal.Dispose();
    }
}
