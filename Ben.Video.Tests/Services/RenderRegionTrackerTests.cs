using Ben.Video.RenderService;

namespace Ben.Video.Tests.Services;

public sealed class RenderRegionTrackerTests
{
    // ── Sync: new regions ────────────────────────────────────────────────────

    [Fact]
    public void Sync_NewClip_CreatesStaleRegion()
    {
        var tracker = new RenderRegionTracker();
        var id = Guid.NewGuid();

        tracker.Sync([new RenderRegionInput(id, 0.0, 5.0, "sig-a")]);

        var region = Assert.Single(tracker.Regions);
        Assert.Equal(id, region.ClipId);
        Assert.Equal(RenderRegionState.Stale, region.State);
        Assert.Equal("sig-a", region.Signature);
    }

    [Fact]
    public void Sync_NewClip_RaisesOnChanged()
    {
        var tracker = new RenderRegionTracker();
        var raised = false;
        tracker.OnChanged += () => raised = true;

        tracker.Sync([new RenderRegionInput(Guid.NewGuid(), 0.0, 5.0, "sig-a")]);

        Assert.True(raised);
    }

    // ── Sync: unchanged signature preserves state ───────────────────────────

    [Fact]
    public void Sync_SameSignature_PreservesRenderedState()
    {
        var tracker = new RenderRegionTracker();
        var id = Guid.NewGuid();
        tracker.Sync([new RenderRegionInput(id, 0.0, 5.0, "sig-a")]);
        tracker.MarkRendered(id, "sig-a");

        tracker.Sync([new RenderRegionInput(id, 0.0, 5.0, "sig-a")]);

        Assert.Equal(RenderRegionState.Fine, Assert.Single(tracker.Regions).State);
    }

    [Fact]
    public void Sync_SameSignatureDifferentPosition_PreservesRenderedState()
    {
        // TimelinePosition is deliberately excluded from staleness — dragging a clip must not
        // invalidate its cached render.
        var tracker = new RenderRegionTracker();
        var id = Guid.NewGuid();
        tracker.Sync([new RenderRegionInput(id, 0.0, 5.0, "sig-a")]);
        tracker.MarkRendered(id, "sig-a");

        tracker.Sync([new RenderRegionInput(id, 8.0, 5.0, "sig-a")]);

        var region = Assert.Single(tracker.Regions);
        Assert.Equal(RenderRegionState.Fine, region.State);
        Assert.Equal(8.0, region.Start);
    }

    // ── Sync: changed signature resets to stale ─────────────────────────────

    [Fact]
    public void Sync_ChangedSignature_ResetsToStale()
    {
        var tracker = new RenderRegionTracker();
        var id = Guid.NewGuid();
        tracker.Sync([new RenderRegionInput(id, 0.0, 5.0, "sig-a")]);
        tracker.MarkRendered(id, "sig-a");

        tracker.Sync([new RenderRegionInput(id, 0.0, 5.0, "sig-b")]);

        var region = Assert.Single(tracker.Regions);
        Assert.Equal(RenderRegionState.Stale, region.State);
        Assert.Equal("sig-b", region.Signature);
        Assert.Equal(0, region.ProgressPct);
    }

    // ── Sync: removed clip drops its region ─────────────────────────────────

    [Fact]
    public void Sync_ClipNoLongerPresent_RemovesRegion()
    {
        var tracker = new RenderRegionTracker();
        var id = Guid.NewGuid();
        tracker.Sync([new RenderRegionInput(id, 0.0, 5.0, "sig-a")]);

        tracker.Sync([]);

        Assert.Empty(tracker.Regions);
    }

    // ── MarkRendered ─────────────────────────────────────────────────────────

    [Fact]
    public void MarkRendered_MatchingSignature_TransitionsToFine()
    {
        var tracker = new RenderRegionTracker();
        var id = Guid.NewGuid();
        tracker.Sync([new RenderRegionInput(id, 0.0, 5.0, "sig-a")]);

        tracker.MarkRendered(id, "sig-a");

        var region = Assert.Single(tracker.Regions);
        Assert.Equal(RenderRegionState.Fine, region.State);
        Assert.Equal(100, region.ProgressPct);
    }

    [Fact]
    public void MarkRendered_StaleSignature_IsIgnored()
    {
        // Guards against a completion racing a newer edit — the region moved on to sig-b
        // before the sig-a render finished, so the sig-a result must not be applied.
        var tracker = new RenderRegionTracker();
        var id = Guid.NewGuid();
        tracker.Sync([new RenderRegionInput(id, 0.0, 5.0, "sig-a")]);
        tracker.Sync([new RenderRegionInput(id, 0.0, 5.0, "sig-b")]);

        tracker.MarkRendered(id, "sig-a");

        var region = Assert.Single(tracker.Regions);
        Assert.Equal(RenderRegionState.Stale, region.State);
        Assert.Equal("sig-b", region.Signature);
    }

    [Fact]
    public void MarkRendered_UnknownClip_DoesNothing()
    {
        var tracker = new RenderRegionTracker();
        var raised = false;
        tracker.OnChanged += () => raised = true;

        tracker.MarkRendered(Guid.NewGuid(), "sig-a");

        Assert.False(raised);
    }

    // ── Ordering ─────────────────────────────────────────────────────────────

    [Fact]
    public void Regions_AreOrderedByStart()
    {
        var tracker = new RenderRegionTracker();
        var late  = Guid.NewGuid();
        var early = Guid.NewGuid();

        tracker.Sync([
            new RenderRegionInput(late, 10.0, 5.0, "sig-late"),
            new RenderRegionInput(early, 0.0, 5.0, "sig-early"),
        ]);

        Assert.Equal([early, late], tracker.Regions.Select(r => r.ClipId));
    }

    // ── Orphaned-segment collection (item #36 phase D, backlog #38 leak fix) ──

    [Fact]
    public void Sync_SignatureChange_CollectsTheOldSegmentAsOrphaned()
    {
        var tracker = new RenderRegionTracker();
        var id = Guid.NewGuid();
        tracker.Sync([new RenderRegionInput(id, 0, 5, "sig-a")]);
        tracker.MarkRendered(id, "sig-a", "old-segment.mp4");

        tracker.Sync([new RenderRegionInput(id, 0, 5, "sig-b")]);

        Assert.Equal(["old-segment.mp4"], tracker.DrainOrphanedSegments());
    }

    [Fact]
    public void Sync_ClipRemoved_CollectsItsSegmentAsOrphaned()
    {
        var tracker = new RenderRegionTracker();
        var id = Guid.NewGuid();
        tracker.Sync([new RenderRegionInput(id, 0, 5, "sig-a")]);
        tracker.MarkRendered(id, "sig-a", "removed-segment.mp4");

        tracker.Sync([]);

        Assert.Equal(["removed-segment.mp4"], tracker.DrainOrphanedSegments());
    }

    [Fact]
    public void DrainOrphanedSegments_SecondCall_ReturnsEmpty()
    {
        var tracker = new RenderRegionTracker();
        var id = Guid.NewGuid();
        tracker.Sync([new RenderRegionInput(id, 0, 5, "sig-a")]);
        tracker.MarkRendered(id, "sig-a", "seg.mp4");
        tracker.Sync([]);

        Assert.Single(tracker.DrainOrphanedSegments());
        Assert.Empty(tracker.DrainOrphanedSegments());
    }

    [Fact]
    public void Sync_RegionWithNoSegment_CollectsNothing()
    {
        var tracker = new RenderRegionTracker();
        var id = Guid.NewGuid();
        tracker.Sync([new RenderRegionInput(id, 0, 5, "sig-a")]); // never rendered

        tracker.Sync([new RenderRegionInput(id, 0, 5, "sig-b")]);
        tracker.Sync([]);

        Assert.Empty(tracker.DrainOrphanedSegments());
    }

    // ── MarkEvicted (item #38 phase C) ──────────────────────────────────────

    [Fact]
    public void MarkEvicted_FineRegion_ResetsToStale()
    {
        var tracker = new RenderRegionTracker();
        var id = Guid.NewGuid();
        tracker.Sync([new RenderRegionInput(id, 0, 5, "sig-a")]);
        tracker.MarkRendered(id, "sig-a", "seg.mp4");

        tracker.MarkEvicted(id);

        var region = tracker.Regions.Single();
        Assert.Equal(RenderRegionState.Stale, region.State);
        Assert.Null(region.SegmentName);
        Assert.Equal(0, region.ProgressPct);
    }

    [Fact]
    public void MarkEvicted_PreservesSignature_SoTheSameContentRerendersRatherThanLoopingForever()
    {
        var tracker = new RenderRegionTracker();
        var id = Guid.NewGuid();
        tracker.Sync([new RenderRegionInput(id, 0, 5, "sig-a")]);
        tracker.MarkRendered(id, "sig-a", "seg.mp4");

        tracker.MarkEvicted(id);

        Assert.Equal("sig-a", tracker.Regions.Single().Signature);
    }

    [Fact]
    public void MarkEvicted_DoesNotAddToOrphanedSegments()
    {
        // The eviction routine already knows exactly which segment it's deleting and does so
        // directly — queuing it here too would cause a double-delete attempt.
        var tracker = new RenderRegionTracker();
        var id = Guid.NewGuid();
        tracker.Sync([new RenderRegionInput(id, 0, 5, "sig-a")]);
        tracker.MarkRendered(id, "sig-a", "seg.mp4");

        tracker.MarkEvicted(id);

        Assert.Empty(tracker.DrainOrphanedSegments());
    }

    [Fact]
    public void MarkEvicted_RaisesOnChanged()
    {
        var tracker = new RenderRegionTracker();
        var id = Guid.NewGuid();
        tracker.Sync([new RenderRegionInput(id, 0, 5, "sig-a")]);
        tracker.MarkRendered(id, "sig-a", "seg.mp4");
        var raised = false;
        tracker.OnChanged += () => raised = true;

        tracker.MarkEvicted(id);

        Assert.True(raised);
    }

    [Fact]
    public void MarkEvicted_UnknownClipId_IsHarmless()
    {
        var tracker = new RenderRegionTracker();
        tracker.MarkEvicted(Guid.NewGuid()); // must not throw
        Assert.Empty(tracker.Regions);
    }
}
