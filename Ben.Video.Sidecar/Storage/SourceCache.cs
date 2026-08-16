using Microsoft.Extensions.Options;

namespace Ben.Video.Sidecar.Storage;

public sealed record SourceCacheEntry(long SizeBytes, DateTimeOffset LastModifiedUtc);

/// <summary>
/// The sidecar's copy of clip source bytes the browser has uploaded, keyed by clip GUID — item
/// #38 phase E/F. Filenames are always <c>{clipGuid:N}.{ext}</c>, both reformatted from parsed
/// GUIDs (never raw request path segments) and drawn from
/// <see cref="Ben.Video.Sidecar.SidecarOptions.AllowedSourceExtensions"/> (never an
/// arbitrary/attacker string) — see <see cref="Ben.Video.Sidecar.Validation.SpecValidator"/> for
/// why that matters. LRU-evicted against a disk quota; never evicts an id currently marked
/// in-use by <see cref="MarkInUse"/>/<see cref="MarkNotInUse"/> (an in-flight job's own source).
/// </summary>
public sealed class SourceCache(IOptions<SidecarOptions> options, SidecarPaths paths)
{
    private readonly SidecarOptions _options = options.Value;
    private readonly HashSet<Guid> _inUse = [];
    private readonly Lock _lock = new();

    private string PathFor(Guid clipId, string ext) =>
        Path.Combine(paths.SourcesDir, $"{clipId:N}{ext}");

    public bool TryGetEntry(Guid clipId, string ext, out SourceCacheEntry entry)
    {
        var path = PathFor(clipId, ext);
        if (File.Exists(path))
        {
            var info = new FileInfo(path);
            entry = new SourceCacheEntry(info.Length, info.LastWriteTimeUtc);
            return true;
        }
        entry = default!;
        return false;
    }

    /// <summary>Streams <paramref name="body"/> into the cache under a temp name, then atomically
    /// renames it into place — a reader can never observe a partially-written file.</summary>
    public async Task<long> WriteAsync(Guid clipId, string ext, Stream body, CancellationToken ct)
    {
        var finalPath = PathFor(clipId, ext);
        var tempPath = finalPath + $".tmp-{Guid.NewGuid():N}";

        long written;
        await using (var file = File.Create(tempPath))
        {
            await body.CopyToAsync(file, ct);
            written = file.Length;
        }

        File.Move(tempPath, finalPath, overwrite: true);
        EnforceQuota();
        return written;
    }

    /// <summary>Returns the file path if the source is cached, first bumping its last-write time
    /// so a recently-*read* source counts as recently-*touched* for LRU purposes — otherwise a
    /// source used every job but never re-uploaded would look perpetually stale.</summary>
    public string? GetPathIfExists(Guid clipId, string ext)
    {
        var path = PathFor(clipId, ext);
        if (!File.Exists(path)) return null;
        File.SetLastWriteTimeUtc(path, DateTimeOffset.UtcNow.UtcDateTime);
        return path;
    }

    public void Delete(Guid clipId, string ext)
    {
        var path = PathFor(clipId, ext);
        try { if (File.Exists(path)) File.Delete(path); } catch { /* best-effort */ }
    }

    public void MarkInUse(Guid clipId) { lock (_lock) { _inUse.Add(clipId); } }
    public void MarkNotInUse(Guid clipId) { lock (_lock) { _inUse.Remove(clipId); } }

    private void EnforceQuota()
    {
        var dir = new DirectoryInfo(paths.SourcesDir);
        var files = dir.GetFiles().Where(f => !f.Name.Contains(".tmp-")).ToList();
        var total = files.Sum(f => f.Length);
        if (total <= _options.SourceCacheQuotaBytes) return;

        List<Guid> inUseSnapshot;
        lock (_lock) { inUseSnapshot = [.. _inUse]; }

        var evictable = files
            .Where(f => !inUseSnapshot.Any(id => f.Name.StartsWith($"{id:N}", StringComparison.Ordinal)))
            .OrderBy(f => f.LastWriteTimeUtc)
            .ToList();

        foreach (var file in evictable)
        {
            if (total <= _options.SourceCacheQuotaBytes) break;
            try
            {
                total -= file.Length;
                file.Delete();
            }
            catch { /* best-effort; try the next one */ }
        }
    }
}
