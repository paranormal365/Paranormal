using Ben.Video.Editor.Services;

namespace Ben.Video.Tests.Services;

public sealed class PreviewSegmentCacheTests
{
    [Fact]
    public void TryGet_UnknownSignature_ReturnsFalse()
    {
        var cache = new PreviewSegmentCache();

        Assert.False(cache.TryGet("sig-a", out _));
    }

    [Fact]
    public void SetThenTryGet_ReturnsTheCachedFilename()
    {
        var cache = new PreviewSegmentCache();

        cache.Set("sig-a", "seg1.mp4");

        Assert.True(cache.TryGet("sig-a", out var name));
        Assert.Equal("seg1.mp4", name);
    }

    [Fact]
    public void Set_SameSignatureTwice_OverwritesFilename()
    {
        var cache = new PreviewSegmentCache();
        cache.Set("sig-a", "seg1.mp4");

        cache.Set("sig-a", "seg2.mp4");

        Assert.True(cache.TryGet("sig-a", out var name));
        Assert.Equal("seg2.mp4", name);
    }

    // ── EvictOrphans ─────────────────────────────────────────────────────────

    [Fact]
    public void EvictOrphans_SignatureStillLive_IsKept()
    {
        var cache = new PreviewSegmentCache();
        cache.Set("sig-a", "seg1.mp4");

        var evicted = cache.EvictOrphans(["sig-a"]);

        Assert.Empty(evicted);
        Assert.True(cache.TryGet("sig-a", out _));
    }

    [Fact]
    public void EvictOrphans_SignatureNoLongerLive_IsRemovedAndReturned()
    {
        var cache = new PreviewSegmentCache();
        cache.Set("sig-a", "seg1.mp4");

        var evicted = cache.EvictOrphans([]);

        Assert.Equal(["seg1.mp4"], evicted);
        Assert.False(cache.TryGet("sig-a", out _));
    }

    [Fact]
    public void EvictOrphans_MixOfLiveAndDead_OnlyEvictsDead()
    {
        var cache = new PreviewSegmentCache();
        cache.Set("sig-a", "seg1.mp4");
        cache.Set("sig-b", "seg2.mp4");
        cache.Set("sig-c", "seg3.mp4");

        var evicted = cache.EvictOrphans(["sig-b"]);

        Assert.Equal(["seg1.mp4", "seg3.mp4"], evicted.OrderBy(x => x));
        Assert.False(cache.TryGet("sig-a", out _));
        Assert.True(cache.TryGet("sig-b", out _));
        Assert.False(cache.TryGet("sig-c", out _));
    }

    [Fact]
    public void EvictOrphans_CalledTwiceInARow_SecondCallReturnsNothingNew()
    {
        var cache = new PreviewSegmentCache();
        cache.Set("sig-a", "seg1.mp4");
        cache.EvictOrphans([]);

        var secondEviction = cache.EvictOrphans([]);

        Assert.Empty(secondEviction);
    }

    // ── Clear ────────────────────────────────────────────────────────────────

    [Fact]
    public void Clear_RemovesAllEntries()
    {
        var cache = new PreviewSegmentCache();
        cache.Set("sig-a", "seg1.mp4");
        cache.Set("sig-b", "seg2.mp4");

        cache.Clear();

        Assert.False(cache.TryGet("sig-a", out _));
        Assert.False(cache.TryGet("sig-b", out _));
    }
}
