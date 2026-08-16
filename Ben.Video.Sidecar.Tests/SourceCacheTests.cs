using Ben.Video.Sidecar.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Ben.Video.Sidecar.Tests;

public sealed class SourceCacheTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("benvideo-cache-test-").FullName;

    private SourceCache Create(long quotaBytes = 1024 * 1024)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Sidecar:HomeOverride"] = _dir })
            .Build();
        var paths = new SidecarPaths(config);
        var options = Options.Create(new SidecarOptions { SourceCacheQuotaBytes = quotaBytes });
        return new SourceCache(options, paths);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public async Task WriteThenTryGetEntry_ReturnsCorrectSize()
    {
        var cache = Create();
        var clipId = Guid.NewGuid();
        var bytes = new byte[500];

        await cache.WriteAsync(clipId, ".mp4", new MemoryStream(bytes), CancellationToken.None);

        Assert.True(cache.TryGetEntry(clipId, ".mp4", out var entry));
        Assert.Equal(500, entry.SizeBytes);
    }

    [Fact]
    public void TryGetEntry_NeverWritten_ReturnsFalse()
    {
        var cache = Create();
        Assert.False(cache.TryGetEntry(Guid.NewGuid(), ".mp4", out _));
    }

    [Fact]
    public async Task Delete_RemovesTheEntry()
    {
        var cache = Create();
        var clipId = Guid.NewGuid();
        await cache.WriteAsync(clipId, ".mp4", new MemoryStream([1, 2, 3]), CancellationToken.None);

        cache.Delete(clipId, ".mp4");

        Assert.False(cache.TryGetEntry(clipId, ".mp4", out _));
    }

    [Fact]
    public void Delete_NeverWritten_DoesNotThrow()
    {
        var cache = Create();
        cache.Delete(Guid.NewGuid(), ".mp4"); // must be a silent no-op
    }

    [Fact]
    public async Task WriteOverQuota_EvictsOldestFirst()
    {
        // Each write is 400 bytes; a 1000-byte quota fits two but not three.
        var cache = Create(quotaBytes: 1000);
        var oldest = Guid.NewGuid();
        var middle = Guid.NewGuid();
        var newest = Guid.NewGuid();

        await cache.WriteAsync(oldest, ".mp4", new MemoryStream(new byte[400]), CancellationToken.None);
        await Task.Delay(10);
        await cache.WriteAsync(middle, ".mp4", new MemoryStream(new byte[400]), CancellationToken.None);
        await Task.Delay(10);
        await cache.WriteAsync(newest, ".mp4", new MemoryStream(new byte[400]), CancellationToken.None);

        Assert.False(cache.TryGetEntry(oldest, ".mp4", out _)); // evicted — least recently touched
        Assert.True(cache.TryGetEntry(middle, ".mp4", out _));
        Assert.True(cache.TryGetEntry(newest, ".mp4", out _));
    }

    [Fact]
    public async Task WriteOverQuota_NeverEvictsAnInUseEntry()
    {
        var cache = Create(quotaBytes: 500);
        var protectedClip = Guid.NewGuid();
        var evictableClip = Guid.NewGuid();

        await cache.WriteAsync(protectedClip, ".mp4", new MemoryStream(new byte[400]), CancellationToken.None);
        cache.MarkInUse(protectedClip);
        await Task.Delay(10);

        await cache.WriteAsync(evictableClip, ".mp4", new MemoryStream(new byte[400]), CancellationToken.None);

        // Over quota now (800 > 500), but the in-use clip must survive even as the oldest entry.
        Assert.True(cache.TryGetEntry(protectedClip, ".mp4", out _));
    }

    [Fact]
    public void GetPathIfExists_UnknownClip_ReturnsNull()
    {
        var cache = Create();
        Assert.Null(cache.GetPathIfExists(Guid.NewGuid(), ".mp4"));
    }
}
