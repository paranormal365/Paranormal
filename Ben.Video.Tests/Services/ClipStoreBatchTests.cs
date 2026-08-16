using Ben.Video.Editor.Models;
using Ben.Video.Editor.Services;
using Microsoft.Extensions.Options;

namespace Ben.Video.Tests.Services;

/// <summary>Item #59-#65 flakiness investigation, phase 145 — <see cref="ClipStore.BeginBatch"/>,
/// which coalesces every <see cref="ClipStore.OnChange"/> raised during a multi-file import loop
/// into at most one, fired when the batch scope disposes.</summary>
public sealed class ClipStoreBatchTests
{
    private static ClipStore CreateStore() => new(Options.Create(new VideoEditorOptions()));

    [Fact]
    public void BeginBatch_SuppressesOnChangeUntilDisposed()
    {
        var store = CreateStore();
        var fireCount = 0;
        store.OnChange += () => fireCount++;

        using (store.BeginBatch())
        {
            store.AddClip(new VideoClip { Name = "a.mp4" });
            store.AddClip(new VideoClip { Name = "b.mp4" });
            store.AddClip(new VideoClip { Name = "c.mp4" });
            Assert.Equal(0, fireCount); // nothing fired yet — still inside the batch
        }

        Assert.Equal(1, fireCount); // exactly one, on dispose
    }

    [Fact]
    public void BeginBatch_WithNoChangesInside_FiresNothingOnDispose()
    {
        var store = CreateStore();
        var fireCount = 0;
        store.OnChange += () => fireCount++;

        using (store.BeginBatch())
        {
            // no mutations
        }

        Assert.Equal(0, fireCount);
    }

    [Fact]
    public void NestedBeginBatch_OnlyTheOutermostScopeFlushes()
    {
        var store = CreateStore();
        var fireCount = 0;
        store.OnChange += () => fireCount++;

        using (store.BeginBatch())
        {
            store.AddClip(new VideoClip { Name = "a.mp4" });
            using (store.BeginBatch()) // nested — must be a no-op, not an early flush
            {
                store.AddClip(new VideoClip { Name = "b.mp4" });
            }
            Assert.Equal(0, fireCount); // inner dispose must NOT have flushed
        }

        Assert.Equal(1, fireCount); // only the outer dispose flushes
    }

    [Fact]
    public void OutsideABatch_OnChangeFiresNormallyPerMutation()
    {
        var store = CreateStore();
        var fireCount = 0;
        store.OnChange += () => fireCount++;

        store.AddClip(new VideoClip { Name = "a.mp4" });
        store.AddClip(new VideoClip { Name = "b.mp4" });

        Assert.Equal(2, fireCount); // unchanged, pre-existing behavior outside a batch
    }

    [Fact]
    public void BeginBatch_AfterAPriorBatchCompleted_WorksAgainNormally()
    {
        var store = CreateStore();
        var fireCount = 0;
        store.OnChange += () => fireCount++;

        using (store.BeginBatch()) { store.AddClip(new VideoClip { Name = "a.mp4" }); }
        Assert.Equal(1, fireCount);

        using (store.BeginBatch()) { store.AddClip(new VideoClip { Name = "b.mp4" }); }
        Assert.Equal(2, fireCount);
    }
}
