namespace Ben.Video.RenderService;

/// <summary>
/// Reconciles the host's per-clip <see cref="RenderRegionInput"/> snapshots into a stable list of
/// <see cref="RenderRegion"/>s, deriving staleness from signature changes rather than requiring the
/// host to annotate every mutation site with "what changed" — the blocker that made per-region
/// tracking unbuildable in an earlier pass (see item #36 design doc, item #13 history). Pure C#, no
/// UI/JS-interop dependency, so it's fully unit-testable in isolation.
/// </summary>
public sealed class RenderRegionTracker
{
    private readonly Dictionary<Guid, RenderRegion> _regions = [];
    private readonly List<string> _orphanedSegments = [];

    /// <summary>Current regions, ordered by <see cref="RenderRegion.Start"/> for bar layout.</summary>
    public IReadOnlyList<RenderRegion> Regions => _regions.Values.OrderBy(r => r.Start).ToList();

    /// <summary>
    /// Returns (and clears) the segment names dropped by <see cref="Sync"/> since the last drain —
    /// a region's old segment when its signature changed, or a removed clip's segment. The tracker
    /// itself can't delete backend storage (it's deliberately backend-agnostic), so
    /// <see cref="BackgroundRenderService"/> drains this on every loop wake-up and forwards the
    /// names to <see cref="IRenderBackend.DeleteSegmentAsync"/>. Without this, worker-side segment
    /// files leak for the life of the session (backlog item #38).
    /// </summary>
    public List<string> DrainOrphanedSegments()
    {
        if (_orphanedSegments.Count == 0) return [];
        var drained = new List<string>(_orphanedSegments);
        _orphanedSegments.Clear();
        return drained;
    }

    /// <summary>Raised after <see cref="Sync"/> or <see cref="MarkRendered"/> actually changes state.</summary>
    public event Action? OnChanged;

    /// <summary>
    /// Reconciles against the host's current clip list. New clips start <see cref="RenderRegionState.Stale"/>;
    /// clips whose signature changed since last sync reset to <see cref="RenderRegionState.Stale"/>;
    /// clips no longer present are dropped. Unchanged clips keep their existing state untouched
    /// (including in-progress/cached state) even if their <see cref="RenderRegionInput.Start"/> moved.
    /// </summary>
    public void Sync(IReadOnlyList<RenderRegionInput> inputs)
    {
        var changed = false;
        var seen = new HashSet<Guid>(inputs.Count);

        foreach (var input in inputs)
        {
            seen.Add(input.ClipId);

            if (!_regions.TryGetValue(input.ClipId, out var region))
            {
                _regions[input.ClipId] = new RenderRegion
                {
                    ClipId    = input.ClipId,
                    Start     = input.Start,
                    Duration  = input.Duration,
                    Signature = input.Signature,
                    State     = RenderRegionState.Stale,
                };
                changed = true;
                continue;
            }

            if (region.Start != input.Start || region.Duration != input.Duration)
            {
                region.Start    = input.Start;
                region.Duration = input.Duration;
                changed = true; // layout-only change — still worth a re-render for bar position
            }

            if (region.Signature != input.Signature)
            {
                if (region.SegmentName is not null)
                    _orphanedSegments.Add(region.SegmentName);
                region.Signature   = input.Signature;
                region.State       = RenderRegionState.Stale;
                region.ProgressPct = 0;
                region.SegmentName = null;
                changed = true;
            }
        }

        var removed = _regions.Keys.Where(id => !seen.Contains(id)).ToList();
        foreach (var id in removed)
        {
            if (_regions[id].SegmentName is not null)
                _orphanedSegments.Add(_regions[id].SegmentName!);
            _regions.Remove(id);
            changed = true;
        }

        if (changed) OnChanged?.Invoke();
    }

    /// <summary>
    /// Marks a region rendered, guarded by signature so a completion racing a newer edit can't
    /// clobber the now-current (and now-stale-again) region with stale results.
    /// </summary>
    public void MarkRendered(Guid clipId, string signature, string? segmentName = null, RenderRegionState resultState = RenderRegionState.Fine)
    {
        if (!_regions.TryGetValue(clipId, out var region)) return;
        if (region.Signature != signature) return;
        if (region.State == resultState && region.ProgressPct == 100 && region.SegmentName == segmentName) return;

        region.State       = resultState;
        region.ProgressPct = 100;
        region.SegmentName = segmentName;
        OnChanged?.Invoke();
    }

    /// <summary>Progress update for an in-flight render — called by <see cref="BackgroundRenderService"/>
    /// while a job is running (phase C/D).</summary>
    public void MarkProgress(Guid clipId, RenderRegionState state, int progressPct)
    {
        if (!_regions.TryGetValue(clipId, out var region)) return;
        region.State       = state;
        region.ProgressPct = Math.Clamp(progressPct, 0, 100);
        OnChanged?.Invoke();
    }

    /// <summary>
    /// Drops a region's cached segment because <see cref="SegmentBudget"/> evicted it under memory
    /// pressure (item #38 phase C) — the same <see cref="RenderRegionState.Stale"/> reset
    /// <see cref="Sync"/> already performs on a content edit, so an evicted region honestly
    /// re-enters the render queue rather than silently claiming a segment that's no longer there.
    /// Deliberately does NOT add to the orphaned-segments list — the caller (the eviction routine
    /// itself) already knows exactly which segment it's about to delete and does so directly, so
    /// queuing it here too would double-delete. No-op if the clip is unknown (already removed).
    /// </summary>
    public void MarkEvicted(Guid clipId)
    {
        if (!_regions.TryGetValue(clipId, out var region)) return;
        region.State       = RenderRegionState.Stale;
        region.ProgressPct = 0;
        region.SegmentName = null;
        OnChanged?.Invoke();
    }
}
