using Microsoft.Extensions.Options;

namespace Ben.Video.Sidecar.Storage;

/// <summary>
/// Rendered segments the sidecar keeps <b>in addition to</b> the copy it already streamed back to
/// the browser — item #70 phase 160's "dual residency".
///
/// <para><b>Why keep a second copy at all?</b> A concat job (phase 160) or an export assembly
/// (phase 162) needs its inputs as real files next to the ffmpeg binary. Without retention the
/// browser would have to upload each segment back — segments it literally just downloaded — which
/// would move more bytes across the loopback than the offload saves. Retention makes the sidecar's
/// copy the one that stays put.</para>
///
/// <para><b>Why not move to sidecar-only residency?</b> Because the wasm fallback has to keep
/// working if the sidecar dies mid-session. Segments continue landing in MEMFS exactly as before;
/// this store is purely additive, so losing it costs a re-render at worst, never correctness.</para>
///
/// <para>Lifecycle mirrors <see cref="SourceCache"/> deliberately: LRU eviction against a quota,
/// with <see cref="MarkInUse"/>/<see cref="MarkNotInUse"/> pinning so a segment can't be evicted
/// out from under an in-flight job that's about to read it. The LRU is also the leak safety net —
/// a browser tab that closes without calling <c>DELETE /v1/segments/{id}</c> can't strand disk
/// forever.</para>
/// </summary>
public sealed class RenderedSegmentStore(IOptions<SidecarOptions> options, SidecarPaths paths)
{
    private readonly SidecarOptions _options = options.Value;
    private readonly HashSet<Guid> _inUse = [];
    private readonly Lock _lock = new();

    /// <summary>Always <c>{guid:N}.mp4</c> from a parsed GUID — never a raw request path segment,
    /// so nothing a caller sends can shape this path.</summary>
    private string PathFor(Guid segmentId) => Path.Combine(paths.SegmentsDir, $"{segmentId:N}.mp4");

    /// <summary>Moves a finished job's output into retention and returns its new id. Move, not
    /// copy: the job workspace is about to be swept anyway, and a copy would briefly double the
    /// disk cost of every retained segment.</summary>
    public Guid Retain(string producedPath)
    {
        var segmentId = Guid.NewGuid();
        var destination = PathFor(segmentId);

        File.Move(producedPath, destination, overwrite: true);
        EnforceQuota();
        return segmentId;
    }

    /// <summary>Returns the path if retained, bumping last-write first so a segment that's read
    /// often but never rewritten doesn't look stale to the LRU (same reasoning as
    /// <see cref="SourceCache.GetPathIfExists"/>).</summary>
    public string? GetPathIfExists(Guid segmentId)
    {
        var path = PathFor(segmentId);
        if (!File.Exists(path)) return null;
        try { File.SetLastWriteTimeUtc(path, DateTimeOffset.UtcNow.UtcDateTime); } catch { /* best-effort */ }
        return path;
    }

    public bool Exists(Guid segmentId) => File.Exists(PathFor(segmentId));

    public long? SizeBytes(Guid segmentId)
    {
        var path = PathFor(segmentId);
        return File.Exists(path) ? new FileInfo(path).Length : null;
    }

    /// <summary>True when the segment existed and is now gone. Idempotent — deleting an unknown or
    /// already-deleted id is a no-op, so a client retrying a cleanup never sees a spurious error.</summary>
    public bool Delete(Guid segmentId)
    {
        var path = PathFor(segmentId);
        try
        {
            if (!File.Exists(path)) return false;
            File.Delete(path);
            return true;
        }
        catch { return false; }
    }

    public void MarkInUse(Guid segmentId) { lock (_lock) { _inUse.Add(segmentId); } }
    public void MarkNotInUse(Guid segmentId) { lock (_lock) { _inUse.Remove(segmentId); } }

    /// <summary>Total retained bytes — surfaced for diagnostics and asserted by the quota tests.</summary>
    public long TotalBytes()
    {
        var dir = new DirectoryInfo(paths.SegmentsDir);
        return dir.Exists ? dir.GetFiles("*.mp4").Sum(f => f.Length) : 0;
    }

    private void EnforceQuota()
    {
        var dir = new DirectoryInfo(paths.SegmentsDir);
        if (!dir.Exists) return;

        var files = dir.GetFiles("*.mp4").ToList();
        var total = files.Sum(f => f.Length);
        if (total <= _options.RetainedSegmentQuotaBytes) return;

        List<Guid> inUseSnapshot;
        lock (_lock) { inUseSnapshot = [.. _inUse]; }
        var pinned = inUseSnapshot.Select(id => $"{id:N}.mp4").ToHashSet(StringComparer.Ordinal);

        foreach (var file in files.Where(f => !pinned.Contains(f.Name)).OrderBy(f => f.LastWriteTimeUtc))
        {
            if (total <= _options.RetainedSegmentQuotaBytes) break;
            try
            {
                total -= file.Length;
                file.Delete();
            }
            catch { /* best-effort; try the next one */ }
        }
    }
}
