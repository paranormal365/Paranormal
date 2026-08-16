namespace Ben.Video.RenderService;

/// <summary>
/// Tracks byte size and last-touch time for every background-rendered segment currently held in
/// the main ffmpeg instance's MEMFS, and decides what to evict when the running total exceeds a
/// cap — item #36 design doc §8 / item #38 phase C, the segment-cache cap+LRU that was designed
/// but never built. Pure bookkeeping, no storage access — matching <see cref="RenderRegionTracker"/>
/// and <c>PreviewSegmentCache</c>'s existing "we don't delete anything ourselves" convention, so
/// this stays fully unit-testable without any JS interop.
/// </summary>
public sealed class SegmentBudget
{
    private sealed record Entry(Guid ClipId, long SizeBytes, DateTimeOffset LastTouched, bool IsRough);

    private readonly Dictionary<string, Entry> _bySegmentName = [];

    public long TotalBytes { get; private set; }

    /// <summary>Registers a newly-produced segment, or re-registers one under a new size (replacing
    /// any prior entry of the same name).</summary>
    public void Track(string segmentName, Guid clipId, long sizeBytes, bool isRough)
    {
        if (_bySegmentName.TryGetValue(segmentName, out var existing))
            TotalBytes -= existing.SizeBytes;
        _bySegmentName[segmentName] = new Entry(clipId, sizeBytes, DateTimeOffset.UtcNow, isRough);
        TotalBytes += sizeBytes;
    }

    /// <summary>Marks a tracked segment as recently used (e.g. consumed by a Preview assembly)
    /// without changing its size — keeps it LRU-fresh even when it isn't being re-rendered.
    /// No-op if the segment isn't tracked.</summary>
    public void Touch(string segmentName)
    {
        if (!_bySegmentName.TryGetValue(segmentName, out var existing)) return;
        _bySegmentName[segmentName] = existing with { LastTouched = DateTimeOffset.UtcNow };
    }

    /// <summary>Stops tracking a segment — call whenever it's actually deleted. No-op if unknown
    /// (already deleted, or never tracked in the first place).</summary>
    public void Untrack(string segmentName)
    {
        if (_bySegmentName.Remove(segmentName, out var existing))
            TotalBytes -= existing.SizeBytes;
    }

    /// <summary>
    /// Returns the (segment name, owning clip id) pairs to evict to bring <see cref="TotalBytes"/>
    /// at or under <paramref name="capBytes"/>, skipping anything whose clip id is in
    /// <paramref name="protectedClipIds"/> (the region under the playhead and any region currently
    /// mid-render — item #36 §8's "never evict" rules). Ordered oldest-touched-first within two
    /// tiers: every Rough-pass segment is considered before any Fine-pass segment, since a rough
    /// segment is cheaper to regenerate and represents less completed work. Does NOT mutate state —
    /// the caller must call <see cref="Untrack"/> for each name it actually deletes.
    /// </summary>
    public List<(string SegmentName, Guid ClipId)> PickEvictions(long capBytes, IReadOnlyCollection<Guid> protectedClipIds)
    {
        if (TotalBytes <= capBytes) return [];

        var protectedSet = protectedClipIds as HashSet<Guid> ?? new HashSet<Guid>(protectedClipIds);
        var candidates = _bySegmentName
            .Where(kv => !protectedSet.Contains(kv.Value.ClipId))
            .OrderBy(kv => kv.Value.IsRough ? 0 : 1)
            .ThenBy(kv => kv.Value.LastTouched)
            .ToList();

        var toEvict  = new List<(string, Guid)>();
        var remaining = TotalBytes;
        foreach (var (name, entry) in candidates)
        {
            if (remaining <= capBytes) break;
            toEvict.Add((name, entry.ClipId));
            remaining -= entry.SizeBytes;
        }
        return toEvict;
    }
}
