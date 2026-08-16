using Ben.Video.RenderService;

namespace Ben.Video.Tests.Services;

public sealed class BackgroundRenderServiceTests
{
    /// <summary>Controllable fake backend — completes immediately with a fixed result unless the
    /// test holds it open via <see cref="Gate"/>, for testing mid-render edits/removals.</summary>
    private sealed class FakeRenderBackend : IRenderBackend
    {
        public TaskCompletionSource<RenderJobResult>? Gate;
        public Func<RenderJob, RenderJobResult> ResultFor = job => RenderJobResult.Ok($"seg_{job.Pass}_{job.ClipId:N}");
        public List<RenderJob> Calls = [];
        public List<string> Deleted = [];

        public async Task<RenderJobResult> RenderAsync(RenderJob job, IProgress<int> progress, CancellationToken ct)
        {
            Calls.Add(job);
            if (Gate is not null) return await Gate.Task;
            return ResultFor(job);
        }

        public Task DeleteSegmentAsync(string segmentName)
        {
            Deleted.Add(segmentName);
            return Task.CompletedTask;
        }
    }

    private static (RenderRegionTracker Tracker, FakeRenderBackend Backend, BackgroundRenderService Service) Create(double playhead = 0)
    {
        var tracker = new RenderRegionTracker();
        var backend = new FakeRenderBackend();
        var service = new BackgroundRenderService(tracker, backend, () => playhead);
        return (tracker, backend, service);
    }

    // ── PickNext: basic cases ───────────────────────────────────────────────

    [Fact]
    public void PickNext_NoRegions_ReturnsNull()
    {
        var (_, _, service) = Create();

        Assert.Null(service.PickNext());
    }

    [Fact]
    public void PickNext_AllRegionsFine_ReturnsNull()
    {
        var (tracker, _, service) = Create();
        var id = Guid.NewGuid();
        tracker.Sync([new RenderRegionInput(id, 0, 5, "sig-a")]);
        tracker.MarkRendered(id, "sig-a");

        Assert.Null(service.PickNext());
    }

    [Fact]
    public void PickNext_OneStaleRegion_ReturnsIt()
    {
        var (tracker, _, service) = Create();
        var id = Guid.NewGuid();
        tracker.Sync([new RenderRegionInput(id, 0, 5, "sig-a")]);

        Assert.Equal(id, service.PickNext()?.ClipId);
    }

    // ── PickNext: playhead-distance ordering ────────────────────────────────

    [Fact]
    public void PickNext_MultipleStale_PicksNearestToPlayhead()
    {
        var (tracker, _, service) = Create(playhead: 12.0);
        var near = Guid.NewGuid();
        var far  = Guid.NewGuid();
        tracker.Sync([
            new RenderRegionInput(far, 0, 5, "sig-far"),    // [0,5]   distance = 7
            new RenderRegionInput(near, 10, 5, "sig-near"), // [10,15] distance = 0 (playhead inside)
        ]);

        Assert.Equal(near, service.PickNext()?.ClipId);
    }

    [Fact]
    public void PickNext_PlayheadBeforeAllRegions_PicksEarliestStart()
    {
        var (tracker, _, service) = Create(playhead: 0.0);
        var first  = Guid.NewGuid();
        var second = Guid.NewGuid();
        tracker.Sync([
            new RenderRegionInput(second, 10, 5, "sig-2"),
            new RenderRegionInput(first, 5, 5, "sig-1"),
        ]);

        Assert.Equal(first, service.PickNext()?.ClipId);
    }

    // ── PickNext: explicit priority requests ────────────────────────────────

    [Fact]
    public void PickNext_ExplicitRequest_WinsOverPlayheadDistance()
    {
        var (tracker, _, service) = Create(playhead: 0.0);
        var near = Guid.NewGuid();
        var far  = Guid.NewGuid();
        tracker.Sync([
            new RenderRegionInput(near, 0, 5, "sig-near"),
            new RenderRegionInput(far, 100, 5, "sig-far"),
        ]);

        service.RequestPriority(far);

        Assert.Equal(far, service.PickNext()?.ClipId);
    }

    [Fact]
    public void PickNext_MultipleExplicitRequests_AreFifo()
    {
        var (tracker, _, service) = Create();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        tracker.Sync([
            new RenderRegionInput(a, 0, 5, "sig-a"),
            new RenderRegionInput(b, 10, 5, "sig-b"),
        ]);

        service.RequestPriority(b);
        service.RequestPriority(a);

        Assert.Equal(b, service.PickNext()?.ClipId); // requested first
    }

    [Fact]
    public void PickNext_PriorityRequestForNonStaleClip_IsIgnored()
    {
        var (tracker, _, service) = Create();
        var id = Guid.NewGuid();
        tracker.Sync([new RenderRegionInput(id, 0, 5, "sig-a")]);
        tracker.MarkRendered(id, "sig-a"); // already Fine

        service.RequestPriority(id);

        Assert.Null(service.PickNext());
    }

    // ── Two-pass scheduling (item #36 phase D) ──────────────────────────────

    [Fact]
    public void PickNext_StaleAndRoughBothPresent_StaleWins()
    {
        // Every region gets its rough pass before any region gets a fine pass — the whole
        // timeline becomes playable at rough quality first, then sharpens.
        var (tracker, backend, service) = Create();
        var roughed = Guid.NewGuid();
        var stale   = Guid.NewGuid();
        tracker.Sync([
            new RenderRegionInput(roughed, 0, 5, "sig-r"),
            new RenderRegionInput(stale, 10, 5, "sig-s"),
        ]);
        tracker.MarkRendered(roughed, "sig-r", "rough.mp4", RenderRegionState.Rough);

        Assert.Equal(stale, service.PickNext()?.ClipId);
    }

    [Fact]
    public void PickNext_OnlyRoughRegionsLeft_ReturnsOneForitsFinePass()
    {
        var (tracker, _, service) = Create();
        var id = Guid.NewGuid();
        tracker.Sync([new RenderRegionInput(id, 0, 5, "sig-a")]);
        tracker.MarkRendered(id, "sig-a", "rough.mp4", RenderRegionState.Rough);

        Assert.Equal(id, service.PickNext()?.ClipId);
    }

    // ── ProcessOneAsync: success ─────────────────────────────────────────────

    [Fact]
    public async Task ProcessOneAsync_StaleRegion_RunsRoughPassAndMarksRough()
    {
        var (tracker, backend, service) = Create();
        var id = Guid.NewGuid();
        tracker.Sync([new RenderRegionInput(id, 0, 5, "sig-a")]);
        backend.ResultFor = _ => RenderJobResult.Ok("rough123.mp4");

        await service.ProcessOneAsync(tracker.Regions.Single(), CancellationToken.None);

        var region = tracker.Regions.Single();
        Assert.Equal(RenderPass.Rough, backend.Calls.Single().Pass);
        Assert.Equal(RenderRegionState.Rough, region.State);
        Assert.Equal("rough123.mp4", region.SegmentName);
        Assert.Equal(100, region.ProgressPct);
    }

    [Fact]
    public async Task ProcessOneAsync_RoughRegion_RunsFinePassAndDeletesTheRoughSegment()
    {
        var (tracker, backend, service) = Create();
        var id = Guid.NewGuid();
        tracker.Sync([new RenderRegionInput(id, 0, 5, "sig-a")]);
        backend.ResultFor = _ => RenderJobResult.Ok("rough123.mp4");
        await service.ProcessOneAsync(tracker.Regions.Single(), CancellationToken.None);

        backend.ResultFor = _ => RenderJobResult.Ok("fine123.mp4");
        await service.ProcessOneAsync(tracker.Regions.Single(), CancellationToken.None);

        var region = tracker.Regions.Single();
        Assert.Equal(RenderPass.Fine, backend.Calls[1].Pass);
        Assert.Equal(RenderRegionState.Fine, region.State);
        Assert.Equal("fine123.mp4", region.SegmentName);
        Assert.Contains("rough123.mp4", backend.Deleted); // superseded rough segment cleaned up
    }

    [Fact]
    public async Task ProcessOneAsync_PriorityRequest_SurvivesRoughPassAndIsConsumedByFinePass()
    {
        // A priority request means "get this clip fully rendered": after its rough pass the entry
        // must still be queued (so its fine pass jumps ahead of other clips' rough passes), and
        // only the fine pass consumes it.
        var (tracker, _, service) = Create(playhead: 0.0);
        var priority = Guid.NewGuid();
        var other    = Guid.NewGuid();
        tracker.Sync([
            new RenderRegionInput(other, 0, 5, "sig-b"),
            new RenderRegionInput(priority, 100, 5, "sig-a"),
        ]);
        service.RequestPriority(priority);

        await service.ProcessOneAsync(service.PickNext()!, CancellationToken.None); // rough pass for `priority`
        Assert.Equal(priority, service.PickNext()?.ClipId);                          // still first: its fine pass

        await service.ProcessOneAsync(service.PickNext()!, CancellationToken.None);  // fine pass consumes the entry
        Assert.Equal(other, service.PickNext()?.ClipId);                             // queue empty — ambient order
    }

    // ── ProcessOneAsync: failure / back-off ──────────────────────────────────

    [Fact]
    public async Task ProcessOneAsync_Failure_LeavesRegionStale()
    {
        var (tracker, backend, service) = Create();
        var id = Guid.NewGuid();
        tracker.Sync([new RenderRegionInput(id, 0, 5, "sig-a")]);
        backend.ResultFor = _ => RenderJobResult.Failed("boom");

        await service.ProcessOneAsync(tracker.Regions.Single(), CancellationToken.None);

        Assert.Equal(RenderRegionState.Stale, tracker.Regions.Single().State);
    }

    [Fact]
    public async Task ProcessOneAsync_Failure_BacksOff_PickNextSkipsSameSignature()
    {
        var (tracker, backend, service) = Create();
        var id = Guid.NewGuid();
        tracker.Sync([new RenderRegionInput(id, 0, 5, "sig-a")]);
        backend.ResultFor = _ => RenderJobResult.Failed("boom");

        await service.ProcessOneAsync(tracker.Regions.Single(), CancellationToken.None);

        // Still Stale, but PickNext must not offer it again — otherwise a permanently-failing
        // clip would hot-loop the queue forever.
        Assert.Null(service.PickNext());
    }

    [Fact]
    public async Task ProcessOneAsync_Failure_ThenClipEdited_IsRetriedWithNewSignature()
    {
        var (tracker, backend, service) = Create();
        var id = Guid.NewGuid();
        tracker.Sync([new RenderRegionInput(id, 0, 5, "sig-a")]);
        backend.ResultFor = _ => RenderJobResult.Failed("boom");
        await service.ProcessOneAsync(tracker.Regions.Single(), CancellationToken.None);
        Assert.Null(service.PickNext()); // backed off

        tracker.Sync([new RenderRegionInput(id, 0, 5, "sig-b")]); // user edited the clip again

        Assert.Equal(id, service.PickNext()?.ClipId); // new signature — not in the failed set
    }

    [Fact]
    public async Task ProcessOneAsync_FinePassFailure_KeepsTheRoughSegmentUsable()
    {
        // The rough segment is still valid content — a failed fine pass must not throw it away
        // (that would regress the region from playable-at-rough-quality back to nothing).
        var (tracker, backend, service) = Create();
        var id = Guid.NewGuid();
        tracker.Sync([new RenderRegionInput(id, 0, 5, "sig-a")]);
        backend.ResultFor = _ => RenderJobResult.Ok("rough123.mp4");
        await service.ProcessOneAsync(tracker.Regions.Single(), CancellationToken.None);

        backend.ResultFor = _ => RenderJobResult.Failed("fine encode boom");
        await service.ProcessOneAsync(tracker.Regions.Single(), CancellationToken.None);

        var region = tracker.Regions.Single();
        Assert.Equal(RenderRegionState.Rough, region.State);
        Assert.Equal("rough123.mp4", region.SegmentName);
        Assert.DoesNotContain("rough123.mp4", backend.Deleted);
        Assert.Null(service.PickNext()); // backed off — not retried until the clip changes
    }

    [Fact]
    public async Task ProcessOneAsync_ThrowingBackend_IsTreatedAsFailure()
    {
        var tracker = new RenderRegionTracker();
        var id = Guid.NewGuid();
        tracker.Sync([new RenderRegionInput(id, 0, 5, "sig-a")]);
        var throwingBackend = new ThrowingBackend();
        var service = new BackgroundRenderService(tracker, throwingBackend, () => 0);

        await service.ProcessOneAsync(tracker.Regions.Single(), CancellationToken.None);

        Assert.Equal(RenderRegionState.Stale, tracker.Regions.Single().State);
    }

    private sealed class ThrowingBackend : IRenderBackend
    {
        public Task<RenderJobResult> RenderAsync(RenderJob job, IProgress<int> progress, CancellationToken ct)
            => throw new InvalidOperationException("simulated backend crash");

        public Task DeleteSegmentAsync(string segmentName) => Task.CompletedTask;
    }

    // ── ProcessOneAsync: discard stale result ───────────────────────────────

    [Fact]
    public async Task ProcessOneAsync_RegionEditedMidRender_DiscardsResultWithoutMarkingRendered()
    {
        var (tracker, backend, service) = Create();
        var id = Guid.NewGuid();
        tracker.Sync([new RenderRegionInput(id, 0, 5, "sig-a")]);
        var region = tracker.Regions.Single();
        backend.Gate = new TaskCompletionSource<RenderJobResult>();

        var processTask = service.ProcessOneAsync(region, CancellationToken.None);

        // Edit the clip while the "render" is still in flight.
        tracker.Sync([new RenderRegionInput(id, 0, 5, "sig-b")]);
        Assert.Equal(RenderRegionState.Stale, tracker.Regions.Single().State);

        // Now let the stale render finish successfully.
        backend.Gate.SetResult(RenderJobResult.Ok("stale-segment.mp4"));
        await processTask;

        // The sig-b region must be untouched by the sig-a result, and the orphaned segment
        // (which nothing will ever consume) must have been deleted, not leaked.
        var current = tracker.Regions.Single();
        Assert.Equal("sig-b", current.Signature);
        Assert.Equal(RenderRegionState.Stale, current.State);
        Assert.Null(current.SegmentName);
        Assert.Contains("stale-segment.mp4", backend.Deleted);
    }

    [Fact]
    public async Task ProcessOneAsync_RegionRemovedMidRender_DiscardsResultWithoutThrowing()
    {
        var (tracker, backend, service) = Create();
        var id = Guid.NewGuid();
        tracker.Sync([new RenderRegionInput(id, 0, 5, "sig-a")]);
        var region = tracker.Regions.Single();
        backend.Gate = new TaskCompletionSource<RenderJobResult>();

        var processTask = service.ProcessOneAsync(region, CancellationToken.None);

        tracker.Sync([]); // clip removed from the timeline entirely

        backend.Gate.SetResult(RenderJobResult.Ok("orphaned-segment.mp4"));
        await processTask; // must not throw

        Assert.Empty(tracker.Regions);
    }

    // ── Deletion hold (Preview-assembly race guard) ──────────────────────────

    [Fact]
    public async Task DeletionHold_DefersSupersedeDeletionUntilRelease()
    {
        // Preview assembly holds deletion while it runs: a fine pass completing mid-assembly must
        // not delete the superseded rough segment the concat is about to read.
        var (tracker, backend, service) = Create();
        var id = Guid.NewGuid();
        tracker.Sync([new RenderRegionInput(id, 0, 5, "sig-a")]);
        backend.ResultFor = _ => RenderJobResult.Ok("rough123.mp4");
        await service.ProcessOneAsync(tracker.Regions.Single(), CancellationToken.None);

        service.BeginDeletionHold();
        backend.ResultFor = _ => RenderJobResult.Ok("fine123.mp4");
        await service.ProcessOneAsync(tracker.Regions.Single(), CancellationToken.None);

        Assert.DoesNotContain("rough123.mp4", backend.Deleted); // held — not deleted yet

        await service.EndDeletionHoldAsync();

        Assert.Contains("rough123.mp4", backend.Deleted); // flushed on release
    }

    [Fact]
    public async Task DeletionHold_IsReentrant_OnlyOutermostReleaseFlushes()
    {
        var (tracker, backend, service) = Create();
        var id = Guid.NewGuid();
        tracker.Sync([new RenderRegionInput(id, 0, 5, "sig-a")]);
        backend.ResultFor = _ => RenderJobResult.Ok("rough123.mp4");
        await service.ProcessOneAsync(tracker.Regions.Single(), CancellationToken.None);

        service.BeginDeletionHold();
        service.BeginDeletionHold();
        backend.ResultFor = _ => RenderJobResult.Ok("fine123.mp4");
        await service.ProcessOneAsync(tracker.Regions.Single(), CancellationToken.None);

        await service.EndDeletionHoldAsync();
        Assert.DoesNotContain("rough123.mp4", backend.Deleted); // inner release — still held

        await service.EndDeletionHoldAsync();
        Assert.Contains("rough123.mp4", backend.Deleted);
    }

    [Fact]
    public async Task EndDeletionHold_WithoutBegin_IsHarmless()
    {
        var (_, backend, service) = Create();

        await service.EndDeletionHoldAsync(); // must not throw or underflow the depth counter

        service.BeginDeletionHold();          // and a subsequent normal hold still works
        await service.EndDeletionHoldAsync();
        Assert.Empty(backend.Deleted);
    }

    // ── Pause / Resume ───────────────────────────────────────────────────────

    [Fact]
    public void Pause_SetsIsPaused()
    {
        var (_, _, service) = Create();

        service.Pause();

        Assert.True(service.IsPaused);
    }

    [Fact]
    public void Resume_ClearsIsPaused()
    {
        var (_, _, service) = Create();
        service.Pause();

        service.Resume();

        Assert.False(service.IsPaused);
    }

    // ── EnableRoughPass (item #36 phase E) ──────────────────────────────────

    [Fact]
    public void EnableRoughPass_DefaultsToTrue()
    {
        var (_, _, service) = Create();
        Assert.True(service.EnableRoughPass);
    }

    [Fact]
    public async Task EnableRoughPass_False_StaleRegionRendersFineDirectly_SkipsRoughEntirely()
    {
        var (tracker, backend, service) = Create();
        service.EnableRoughPass = false;
        var id = Guid.NewGuid();
        tracker.Sync([new RenderRegionInput(id, 0, 5, "sig-a")]);
        backend.ResultFor = _ => RenderJobResult.Ok("fine123.mp4");

        await service.ProcessOneAsync(tracker.Regions.Single(), CancellationToken.None);

        var region = tracker.Regions.Single();
        Assert.Equal(RenderPass.Fine, backend.Calls.Single().Pass);
        Assert.Equal(RenderRegionState.Fine, region.State);
        Assert.Equal("fine123.mp4", region.SegmentName);
    }

    [Fact]
    public async Task EnableRoughPass_False_NoFurtherWorkAfterTheSingleFinePass()
    {
        // With rough disabled, a stale region goes straight to Fine and PickNext should have
        // nothing left to do — unlike the rough-enabled path, which still needs a second
        // (fine) pass after the first (rough) one.
        var (tracker, backend, service) = Create();
        service.EnableRoughPass = false;
        var id = Guid.NewGuid();
        tracker.Sync([new RenderRegionInput(id, 0, 5, "sig-a")]);

        await service.ProcessOneAsync(tracker.Regions.Single(), CancellationToken.None);

        Assert.Null(service.PickNext());
    }

    // ── End-to-end loop (Start) ──────────────────────────────────────────────

    [Fact]
    public async Task Start_AutomaticallyRendersStaleRegionsThroughBothPassesWithoutManualProcessOneAsync()
    {
        var (tracker, backend, service) = Create();
        var id = Guid.NewGuid();
        tracker.Sync([new RenderRegionInput(id, 0, 5, "sig-a")]);

        service.Start();

        // Poll briefly — the loop runs on its own background Task. Two-pass: the loop should
        // carry the region all the way Stale → Rough → Fine on its own.
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (tracker.Regions.Single().State != RenderRegionState.Fine && DateTime.UtcNow < deadline)
            await Task.Delay(10);

        Assert.Equal(RenderRegionState.Fine, tracker.Regions.Single().State);
        Assert.Equal([RenderPass.Rough, RenderPass.Fine], backend.Calls.Select(c => c.Pass).ToArray());
        await service.DisposeAsync();
    }

    [Fact]
    public async Task Start_OrphanedSegmentsFromEdits_AreDeletedByTheLoop()
    {
        // An edit nulls the region's SegmentName in the tracker; the loop must drain and delete
        // the orphaned worker-side file (backlog #38's leak) as well as re-render the new content.
        var (tracker, backend, service) = Create();
        var id = Guid.NewGuid();
        tracker.Sync([new RenderRegionInput(id, 0, 5, "sig-a")]);
        backend.ResultFor = job => RenderJobResult.Ok($"{job.Pass}-{job.Signature}.mp4");
        service.Start();

        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (tracker.Regions.Single().State != RenderRegionState.Fine && DateTime.UtcNow < deadline)
            await Task.Delay(10);

        tracker.Sync([new RenderRegionInput(id, 0, 5, "sig-b")]); // edit orphans Fine-sig-a.mp4

        deadline = DateTime.UtcNow.AddSeconds(2);
        while (!backend.Deleted.Contains("Fine-sig-a.mp4") && DateTime.UtcNow < deadline)
            await Task.Delay(10);

        Assert.Contains("Fine-sig-a.mp4", backend.Deleted);
        await service.DisposeAsync();
    }

    [Fact]
    public async Task Start_EditArrivingRightAsAJobCompletes_IsNotMissed()
    {
        // Regression test for a live-verified race: RenderRegionTracker.OnChanged fires (and
        // Signal() runs) on every progress tick during a render, which can "use up" the queue's
        // single pending-wakeup slot on stale progress noise at the exact moment a genuinely new
        // edit's own Signal() call needed to wake the loop — found live when a second Preview
        // edit was needed to un-stick a first one that silently sat Stale for 20+ seconds.
        var (tracker, backend, service) = Create();
        service.IdlePollInterval = TimeSpan.FromMilliseconds(20); // fast retry for the test
        var id = Guid.NewGuid();
        tracker.Sync([new RenderRegionInput(id, 0, 5, "sig-a")]);

        var firstJobGate = new TaskCompletionSource<RenderJobResult>();
        backend.Gate = firstJobGate;
        service.Start();

        // Wait for the loop to actually pick up the first job before racing the edit against it.
        var pickupDeadline = DateTime.UtcNow.AddSeconds(2);
        while (backend.Calls.Count == 0 && DateTime.UtcNow < pickupDeadline)
            await Task.Delay(5);
        Assert.Single(backend.Calls);

        // Simulate progress noise firing Signal() repeatedly while the job is "in flight",
        // exactly like MarkProgress does during a real render.
        for (var i = 0; i < 5; i++) tracker.MarkProgress(id, RenderRegionState.RenderingFine, i * 20);

        // Complete the first job, then — as close to that completion as this test can land —
        // apply a new edit. The bug this guards against is this edit's own Signal() being
        // dropped because the semaphore's one pending slot was already spent on progress noise.
        backend.Gate = null; // subsequent RenderAsync calls (the retry) resolve immediately
        firstJobGate.SetResult(RenderJobResult.Ok("first-segment.mp4"));
        tracker.Sync([new RenderRegionInput(id, 0, 5, "sig-b")]); // the edit

        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (tracker.Regions.Single().State != RenderRegionState.Fine && DateTime.UtcNow < deadline)
            await Task.Delay(10);

        var region = tracker.Regions.Single();
        Assert.Equal(RenderRegionState.Fine, region.State);
        Assert.Equal("sig-b", region.Signature); // rendered the NEW content, not stuck on sig-a
        await service.DisposeAsync();
    }

    [Fact]
    public async Task Start_UnhandledExceptionInLoopBody_DoesNotKillTheLoopAndUnsticksTheRegion()
    {
        // Item #9 — "clip-art stall" regression test, covering BOTH real bugs found while fixing
        // it. Bug 1: LoopAsync previously only caught OperationCanceledException at the outer
        // level, so ANY other unhandled exception in a single iteration silently killed
        // _loopTask for the rest of the session (matching the reported symptom: one job
        // completes, then permanently nothing, zero errors visible anywhere). Bug 2 (found while
        // writing this test — the naive fix for bug 1 alone still failed it): ProcessOneAsync
        // marks a region RenderingRough/RenderingFine *before* calling the backend, so an
        // exception that escapes ProcessOneAsync entirely (only OperationCanceledException does)
        // leaves the region stuck in that Rendering* state forever — PickNext only ever selects
        // Stale/Rough — invisible to all future work even though the loop itself survived.
        //
        // Simulates a backend that throws OperationCanceledException (backend-internal, unrelated
        // to our own ct) once, then behaves normally. Asserts: the loop survives (no crash), the
        // fault is surfaced via OnLoopError, and the region correctly backs off to Stale (not
        // stuck RenderingRough) — matching exactly how ProcessOneAsync's own caught-failure path
        // already behaves for every other exception type, so this failure mode isn't a special
        // case. Backoff means the SAME signature won't auto-retry (by design, matching
        // ProcessOneAsync_BackendThrows_MarksRegionStaleAndDoesNotThrow's existing behavior for
        // ordinary failures) — a follow-up edit (new signature) proves the loop is still fully
        // functional afterward, not just "not crashed."
        var (tracker, backend, service) = Create();
        service.IdlePollInterval = TimeSpan.FromMilliseconds(20);
        var id = Guid.NewGuid();
        tracker.Sync([new RenderRegionInput(id, 0, 5, "sig-a")]);

        Exception? observedError = null;
        service.OnLoopError += ex => observedError = ex;
        backend.ResultFor = _ => throw new OperationCanceledException("simulated backend-internal fault, unrelated to our ct");

        service.Start();

        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (observedError is null && DateTime.UtcNow < deadline)
            await Task.Delay(10);

        Assert.IsType<OperationCanceledException>(observedError);
        Assert.Equal(RenderRegionState.Stale, tracker.Regions.Single().State); // not stuck RenderingRough

        // Prove the loop is still fully alive: an edit (new signature) is picked up and renders
        // normally, without needing to restart anything.
        backend.ResultFor = job => RenderJobResult.Ok($"seg_{job.Pass}_{job.ClipId:N}");
        tracker.Sync([new RenderRegionInput(id, 0, 5, "sig-b")]);

        deadline = DateTime.UtcNow.AddSeconds(2);
        while (tracker.Regions.Single().State != RenderRegionState.Fine && DateTime.UtcNow < deadline)
            await Task.Delay(10);

        Assert.Equal(RenderRegionState.Fine, tracker.Regions.Single().State);
        await service.DisposeAsync();
    }

    [Fact]
    public async Task Start_BackendOperationCanceledException_BacksOffInsteadOfTightRetryLoop()
    {
        // Companion to the test above: with EVERY call failing (not just once), the existing
        // _failedSignatures backoff must still apply to this failure path exactly as it does for
        // ProcessOneAsync's own caught failures — proving LoopAsync's new catch doesn't bypass
        // that mechanism and spin in a tight, CPU-pinning retry loop.
        var (tracker, backend, service) = Create();
        service.IdlePollInterval = TimeSpan.FromMilliseconds(20);
        var failingId = Guid.NewGuid();
        tracker.Sync([new RenderRegionInput(failingId, 0, 5, "sig-a")]);
        backend.ResultFor = _ => throw new OperationCanceledException("backend-internal, not ours");

        var errorCount = 0;
        service.OnLoopError += _ => errorCount++;
        service.Start();

        // One failure, then the backed-off signature is never picked again — give it many idle
        // cycles' worth of time to prove errorCount settles at 1, not growing further.
        await Task.Delay(300);
        Assert.Equal(1, errorCount);

        // The loop itself must still be fully alive — a second, unrelated region renders normally.
        var otherId = Guid.NewGuid();
        backend.ResultFor = job => job.ClipId == failingId
            ? throw new OperationCanceledException("still failing, backed off")
            : RenderJobResult.Ok($"seg_{job.Pass}_{job.ClipId:N}");
        tracker.Sync([
            new RenderRegionInput(failingId, 0, 5, "sig-a"),
            new RenderRegionInput(otherId, 10, 5, "sig-other"),
        ]);

        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (tracker.Regions.First(r => r.ClipId == otherId).State != RenderRegionState.Fine && DateTime.UtcNow < deadline)
            await Task.Delay(10);

        Assert.Equal(RenderRegionState.Fine, tracker.Regions.First(r => r.ClipId == otherId).State);
        await service.DisposeAsync(); // must still shut down cleanly via real cancellation
    }

    [Fact]
    public async Task Start_CalledTwice_DoesNotDuplicateProcessing()
    {
        var (tracker, backend, service) = Create();
        var id = Guid.NewGuid();
        tracker.Sync([new RenderRegionInput(id, 0, 5, "sig-a")]);

        service.Start();
        service.Start(); // no-op second call

        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (tracker.Regions.Single().State != RenderRegionState.Fine && DateTime.UtcNow < deadline)
            await Task.Delay(10);

        // Exactly one rough + one fine — a duplicated loop would produce more.
        Assert.Equal(2, backend.Calls.Count);
        await service.DisposeAsync();
    }

    // ── Segment budget / cap+LRU (item #36 §8, item #38 phase C) ────────────

    [Fact]
    public async Task ProcessOneAsync_SuccessfulRender_TracksSizeInBudget()
    {
        var (tracker, backend, service) = Create();
        var id = Guid.NewGuid();
        tracker.Sync([new RenderRegionInput(id, 0, 5, "sig-a")]);
        backend.ResultFor = _ => RenderJobResult.Ok("seg.mp4", sizeBytes: 42);

        await service.ProcessOneAsync(tracker.Regions.Single(), CancellationToken.None);

        Assert.Equal(42, service.TrackedBytes);
    }

    [Fact]
    public async Task ProcessOneAsync_BudgetExceeded_EvictsOldestNonProtectedSegment()
    {
        var (tracker, backend, service) = Create(playhead: 1000); // away from both regions below
        service.SegmentCapBytes = 100;
        var older = Guid.NewGuid();
        var newer = Guid.NewGuid();
        tracker.Sync([
            new RenderRegionInput(older, 0, 5, "sig-older"),
            new RenderRegionInput(newer, 100, 5, "sig-newer"),
        ]);
        backend.ResultFor = job => RenderJobResult.Ok($"seg-{job.ClipId:N}.mp4", sizeBytes: 60);

        await service.ProcessOneAsync(tracker.Regions.First(r => r.ClipId == older), CancellationToken.None);
        await Task.Delay(5); // ensure distinct LastTouched timestamps — see SegmentBudgetTests
        await service.ProcessOneAsync(tracker.Regions.First(r => r.ClipId == newer), CancellationToken.None);

        // Total would be 120 > 100 cap — the older (LRU) segment is evicted, freeing it back to 60.
        Assert.Equal(RenderRegionState.Stale, tracker.Regions.Single(r => r.ClipId == older).State);
        Assert.Equal(RenderRegionState.Rough, tracker.Regions.Single(r => r.ClipId == newer).State);
        Assert.Equal(60, service.TrackedBytes);
    }

    [Fact]
    public async Task ProcessOneAsync_BudgetExceeded_NeverEvictsPlayheadRegion()
    {
        var (tracker, backend, service) = Create(playhead: 0.0); // inside `older`'s [0,5] region
        service.SegmentCapBytes = 100;
        var older = Guid.NewGuid();
        var newer = Guid.NewGuid();
        tracker.Sync([
            new RenderRegionInput(older, 0, 5, "sig-older"),
            new RenderRegionInput(newer, 100, 5, "sig-newer"),
        ]);
        backend.ResultFor = job => RenderJobResult.Ok($"seg-{job.ClipId:N}.mp4", sizeBytes: 60);

        await service.ProcessOneAsync(tracker.Regions.First(r => r.ClipId == older), CancellationToken.None);
        await service.ProcessOneAsync(tracker.Regions.First(r => r.ClipId == newer), CancellationToken.None);

        // `older` is under the playhead and must survive despite being LRU-oldest — `newer` is
        // evicted instead even though it's the more recently rendered of the two.
        Assert.Equal(RenderRegionState.Rough, tracker.Regions.Single(r => r.ClipId == older).State);
        Assert.Equal(RenderRegionState.Stale, tracker.Regions.Single(r => r.ClipId == newer).State);
    }

    [Fact]
    public async Task ProcessOneAsync_BudgetExceeded_NeverEvictsInFlightRegionsSegment()
    {
        var (tracker, backend, service) = Create(playhead: 1000); // away from both regions below
        service.SegmentCapBytes = 100; // above one segment (60) so the first render alone doesn't evict itself
        var inFlight = Guid.NewGuid();
        var other = Guid.NewGuid();
        tracker.Sync([
            new RenderRegionInput(inFlight, 0, 5, "sig-a"),
            new RenderRegionInput(other, 100, 5, "sig-b"),
        ]);
        backend.ResultFor = _ => RenderJobResult.Ok("rough-inflight.mp4", sizeBytes: 60);
        await service.ProcessOneAsync(tracker.Regions.First(r => r.ClipId == inFlight), CancellationToken.None);

        // Simulate inFlight now mid fine-pass — its rough segment is still the live SegmentName.
        tracker.MarkProgress(inFlight, RenderRegionState.RenderingFine, 50);

        backend.ResultFor = _ => RenderJobResult.Ok("rough-other.mp4", sizeBytes: 60);
        await service.ProcessOneAsync(tracker.Regions.First(r => r.ClipId == other), CancellationToken.None);

        Assert.Equal(RenderRegionState.RenderingFine, tracker.Regions.Single(r => r.ClipId == inFlight).State);
        Assert.DoesNotContain("rough-inflight.mp4", backend.Deleted);
    }

    [Fact]
    public async Task ProcessOneAsync_BudgetExceeded_DuringDeletionHold_DefersEviction()
    {
        var (tracker, backend, service) = Create(playhead: 1000); // away from both regions below
        service.SegmentCapBytes = 100; // above one segment (60) so the first render alone doesn't evict itself
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        tracker.Sync([
            new RenderRegionInput(a, 0, 5, "sig-a"),
            new RenderRegionInput(b, 100, 5, "sig-b"),
        ]);
        backend.ResultFor = job => RenderJobResult.Ok($"seg-{job.ClipId:N}.mp4", sizeBytes: 60);
        await service.ProcessOneAsync(tracker.Regions.First(r => r.ClipId == a), CancellationToken.None);

        service.BeginDeletionHold();
        await service.ProcessOneAsync(tracker.Regions.First(r => r.ClipId == b), CancellationToken.None);

        // Over cap now, but the hold is active — eviction must be skipped entirely, not queued.
        Assert.Equal(RenderRegionState.Rough, tracker.Regions.Single(r => r.ClipId == a).State);
        Assert.Empty(backend.Deleted);

        await service.EndDeletionHoldAsync(); // nothing was queued, so this flushes nothing
        Assert.Empty(backend.Deleted);
    }

    [Fact]
    public async Task Eviction_DoesNotFeedFailedSignaturesBackoff()
    {
        // Eviction is a memory-pressure decision, not a render failure — an evicted region must be
        // immediately re-offered by PickNext, not treated like a persistently-failing clip.
        var (tracker, backend, service) = Create(playhead: 1000); // away from both regions below
        service.SegmentCapBytes = 100; // above one segment (60) so the first render alone doesn't evict itself
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        tracker.Sync([
            new RenderRegionInput(a, 0, 5, "sig-a"),
            new RenderRegionInput(b, 100, 5, "sig-b"),
        ]);
        backend.ResultFor = job => RenderJobResult.Ok($"seg-{job.ClipId:N}.mp4", sizeBytes: 60);
        await service.ProcessOneAsync(tracker.Regions.First(r => r.ClipId == a), CancellationToken.None);
        await Task.Delay(5); // ensure distinct LastTouched timestamps — see SegmentBudgetTests
        await service.ProcessOneAsync(tracker.Regions.First(r => r.ClipId == b), CancellationToken.None);

        Assert.Equal(a, service.PickNext()?.ClipId);
    }

    [Fact]
    public async Task ProcessOneAsync_SegmentEvictedByOtherwiseNormalDeletion_UntracksFromBudget()
    {
        // Any deletion (supersede, discard-stale, orphan-drain) already funnels through
        // TryDeleteSegmentAsync — confirms the budget stays accurate without a second bookkeeping
        // path, using the existing rough→fine supersede flow as the trigger.
        var (tracker, backend, service) = Create();
        var id = Guid.NewGuid();
        tracker.Sync([new RenderRegionInput(id, 0, 5, "sig-a")]);
        backend.ResultFor = _ => RenderJobResult.Ok("rough.mp4", sizeBytes: 60);
        await service.ProcessOneAsync(tracker.Regions.Single(), CancellationToken.None);
        Assert.Equal(60, service.TrackedBytes);

        backend.ResultFor = _ => RenderJobResult.Ok("fine.mp4", sizeBytes: 90);
        await service.ProcessOneAsync(tracker.Regions.Single(), CancellationToken.None);

        // rough.mp4 (60) superseded and untracked; fine.mp4 (90) tracked — not 150.
        Assert.Equal(90, service.TrackedBytes);
    }

    [Fact]
    public void TouchSegment_UnknownSegment_IsHarmless()
    {
        var (_, _, service) = Create();
        service.TouchSegment("never-tracked.mp4"); // must not throw
    }
}
