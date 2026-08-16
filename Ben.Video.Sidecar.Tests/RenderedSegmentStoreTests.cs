using Ben.Video.Sidecar;
using Ben.Video.Sidecar.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Ben.Video.Sidecar.Tests;

/// <summary>
/// Item #70 phase 160 — retained-segment quota/LRU/pinning, exercised directly rather than through
/// HTTP so a tiny quota can be set and eviction observed deterministically.
/// </summary>
public sealed class RenderedSegmentStoreTests : IDisposable
{
    private readonly string _home = Directory.CreateTempSubdirectory("benvideo-segstore-test-").FullName;

    private (RenderedSegmentStore Store, SidecarPaths Paths) Create(long quotaBytes)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Sidecar:HomeOverride"] = _home })
            .Build();
        var paths = new SidecarPaths(config);
        var options = Options.Create(new SidecarOptions { RetainedSegmentQuotaBytes = quotaBytes });
        return (new RenderedSegmentStore(options, paths), paths);
    }

    /// <summary>Writes a file of <paramref name="sizeBytes"/> in a scratch dir and retains it,
    /// mimicking a finished job's output being moved into the store.</summary>
    private static Guid RetainFile(RenderedSegmentStore store, SidecarPaths paths, int sizeBytes)
    {
        var scratch = Path.Combine(paths.JobsDir, $"scratch-{Guid.NewGuid():N}");
        Directory.CreateDirectory(scratch);
        var produced = Path.Combine(scratch, "output.mp4");
        File.WriteAllBytes(produced, new byte[sizeBytes]);
        return store.Retain(produced);
    }

    [Fact]
    public void Retain_MovesTheFileAndMakesItResolvable()
    {
        var (store, paths) = Create(quotaBytes: 1024 * 1024);
        var scratch = Path.Combine(paths.JobsDir, "scratch");
        Directory.CreateDirectory(scratch);
        var produced = Path.Combine(scratch, "output.mp4");
        File.WriteAllBytes(produced, new byte[128]);

        var id = store.Retain(produced);

        Assert.True(store.Exists(id));
        Assert.Equal(128, store.SizeBytes(id));
        // Moved, not copied — the job workspace is swept shortly after, and a copy would briefly
        // double the disk cost of every retained segment.
        Assert.False(File.Exists(produced));
    }

    [Fact]
    public void Delete_RemovesAndIsIdempotent()
    {
        var (store, paths) = Create(quotaBytes: 1024 * 1024);
        var id = RetainFile(store, paths, 64);

        Assert.True(store.Delete(id));
        Assert.False(store.Exists(id));
        Assert.False(store.Delete(id));                 // already gone
        Assert.False(store.Delete(Guid.NewGuid()));     // never existed
    }

    [Fact]
    public void GetPathIfExists_Unknown_ReturnsNull() =>
        Assert.Null(Create(1024).Store.GetPathIfExists(Guid.NewGuid()));

    [Fact]
    public void Quota_EvictsOldestFirst()
    {
        // Quota fits two 100-byte segments; retaining a third must evict the least-recently-written.
        var (store, paths) = Create(quotaBytes: 250);

        var oldest = RetainFile(store, paths, 100);
        Thread.Sleep(1100); // last-write timestamps have ~1s filesystem granularity
        var middle = RetainFile(store, paths, 100);
        Thread.Sleep(1100);
        var newest = RetainFile(store, paths, 100);

        Assert.False(store.Exists(oldest));
        Assert.True(store.Exists(middle));
        Assert.True(store.Exists(newest));
    }

    [Fact]
    public void Quota_NeverEvictsAPinnedSegment()
    {
        // The race this prevents: a concat job has checked that its inputs exist and is about to
        // open them when an unrelated render pushes the store over quota. Without pinning the LRU
        // could delete an input mid-job — a real failure that would only appear on large timelines
        // under memory pressure.
        var (store, paths) = Create(quotaBytes: 250);

        var pinned = RetainFile(store, paths, 100);
        store.MarkInUse(pinned);
        Thread.Sleep(1100);
        var second = RetainFile(store, paths, 100);
        Thread.Sleep(1100);
        var third = RetainFile(store, paths, 100);

        // `pinned` is the oldest and would normally have gone first.
        Assert.True(store.Exists(pinned));
        Assert.False(store.Exists(second));
        Assert.True(store.Exists(third));

        // Once unpinned it becomes evictable again like anything else.
        store.MarkNotInUse(pinned);
        Thread.Sleep(1100);
        RetainFile(store, paths, 100);
        Assert.False(store.Exists(pinned));
    }

    [Fact]
    public void TotalBytes_ReflectsRetainedContent()
    {
        var (store, paths) = Create(quotaBytes: 1024 * 1024);
        Assert.Equal(0, store.TotalBytes());

        RetainFile(store, paths, 100);
        RetainFile(store, paths, 50);

        Assert.Equal(150, store.TotalBytes());
    }

    public void Dispose()
    {
        try { Directory.Delete(_home, recursive: true); } catch { /* best-effort */ }
    }
}
