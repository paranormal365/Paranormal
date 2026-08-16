using Ben.Video.Editor.Services;

namespace Ben.Video.Tests.Services;

/// <summary>Item #59-#65 flakiness investigation, phase 146 — <see cref="AnimatedOverlayBatchPlanner"/>'s
/// pure arithmetic: the batch-size calculator, and frame-index continuity across the batches it
/// produces (every frame appears exactly once, in order, across the whole sequence).</summary>
public sealed class AnimatedOverlayBatchPlannerTests
{
    // ── BatchSize ─────────────────────────────────────────────────────────────

    [Fact]
    public void BatchSize_SmallCanvas_ClampsToTheMaximum()
    {
        // A tiny canvas would otherwise compute an enormous batch size — must clamp.
        var size = AnimatedOverlayBatchPlanner.BatchSize(64, 64);
        Assert.Equal(AnimatedOverlayBatchPlanner.MaxBatchSize, size);
    }

    [Fact]
    public void BatchSize_HugeCanvas_ClampsToTheMinimum()
    {
        // A 4K canvas: 3840*2160*4 ≈ 33MB/frame — under the 64MB budget's raw quotient of 1,
        // must clamp up to the minimum rather than produce a degenerate batch of 0 or 1.
        var size = AnimatedOverlayBatchPlanner.BatchSize(3840, 2160);
        Assert.Equal(AnimatedOverlayBatchPlanner.MinBatchSize, size);
    }

    [Fact]
    public void BatchSize_TypicalHdCanvas_IsBetweenTheClamps()
    {
        var size = AnimatedOverlayBatchPlanner.BatchSize(1920, 1080);
        Assert.InRange(size, AnimatedOverlayBatchPlanner.MinBatchSize, AnimatedOverlayBatchPlanner.MaxBatchSize);
    }

    [Fact]
    public void BatchSize_LargerByteBudget_NeverProducesASmallerBatch()
    {
        var small = AnimatedOverlayBatchPlanner.BatchSize(1280, 720, byteBudget: 16 * 1024 * 1024);
        var large = AnimatedOverlayBatchPlanner.BatchSize(1280, 720, byteBudget: 128 * 1024 * 1024);
        Assert.True(large >= small);
    }

    [Fact]
    public void BatchSize_IsAlwaysPositive()
    {
        Assert.True(AnimatedOverlayBatchPlanner.BatchSize(1, 1) > 0);
        Assert.True(AnimatedOverlayBatchPlanner.BatchSize(7680, 4320) > 0); // 8K
    }

    // ── Batches: frame-index continuity ──────────────────────────────────────

    [Theory]
    [InlineData(1800, 7)]   // 60s@30fps at a typical HD batch size
    [InlineData(1, 7)]      // single-frame clip
    [InlineData(7, 7)]      // exactly one full batch, no remainder
    [InlineData(8, 7)]      // one full batch + a 1-frame remainder
    [InlineData(300, 240)]  // MaxBatchSize boundary
    public void Batches_CoverEveryFrameExactlyOnceInOrder(int frameCount, int batchSize)
    {
        var seen = new List<int>();
        foreach (var (_, start, count) in AnimatedOverlayBatchPlanner.Batches(frameCount, batchSize))
        {
            for (var i = 0; i < count; i++)
                seen.Add(start + i);
        }

        Assert.Equal(Enumerable.Range(0, frameCount), seen);
    }

    [Fact]
    public void Batches_EachBatchExceptPossiblyTheLast_IsFullSize()
    {
        var batches = AnimatedOverlayBatchPlanner.Batches(23, 7).ToList();

        for (var i = 0; i < batches.Count - 1; i++)
            Assert.Equal(7, batches[i].Count);

        Assert.True(batches[^1].Count <= 7);
        Assert.True(batches[^1].Count > 0);
    }

    [Fact]
    public void Batches_BatchIndexesAreSequentialStartingAtZero()
    {
        var batches = AnimatedOverlayBatchPlanner.Batches(50, 7).ToList();
        Assert.Equal(Enumerable.Range(0, batches.Count), batches.Select(b => b.BatchIndex));
    }

    [Fact]
    public void Batches_ZeroFrameCount_ProducesNoBatches()
    {
        Assert.Empty(AnimatedOverlayBatchPlanner.Batches(0, 7));
    }

    [Fact]
    public void Batches_ZeroBatchSize_ProducesNoBatchesRatherThanLoopingForever()
    {
        Assert.Empty(AnimatedOverlayBatchPlanner.Batches(100, 0));
    }
}
