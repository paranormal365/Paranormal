using Ben.Video.Editor.Models;
using Ben.Video.Editor.Services;
using Microsoft.Extensions.Options;

namespace Ben.Video.Tests.Services;

/// <summary>
/// Item #63 fix — <see cref="ClipStore.CommitMotionKeyframeEdit"/> is the plumbing that lets a
/// mutation owned by a different scoped service (<c>MotionKeyframeService</c>) participate in
/// <see cref="ClipStore"/>'s own undo/redo stack. Uses plain local state instead of a real
/// <c>MotionKeyframeService</c> to keep the test focused on the undo/redo wiring itself, matching
/// how the real callers use it (arbitrary apply/revert closures, no dependency on what they touch).
/// </summary>
public sealed class ClipStoreMotionKeyframeUndoTests
{
    private static ClipStore CreateStore(Action<VideoEditorOptions>? configure = null)
    {
        var options = new VideoEditorOptions();
        configure?.Invoke(options);
        return new ClipStore(Options.Create(options));
    }

    [Fact]
    public void CommitMotionKeyframeEdit_PushesUndoEntry()
    {
        var store = CreateStore();
        Assert.False(store.CanUndo);

        store.CommitMotionKeyframeEdit("Edit keyframe", apply: () => { }, revert: () => { });

        Assert.True(store.CanUndo);
        Assert.Equal("Edit keyframe", store.UndoDescription);
    }

    [Fact]
    public void CommitMotionKeyframeEdit_Undo_CallsRevert()
    {
        var store = CreateStore();
        var value = "new";

        store.CommitMotionKeyframeEdit("Edit keyframe",
            apply:  () => value = "new",
            revert: () => value = "old");

        store.Undo();

        Assert.Equal("old", value);
    }

    [Fact]
    public void CommitMotionKeyframeEdit_UndoThenRedo_CallsApplyAgain()
    {
        var store = CreateStore();
        var value = "new";

        store.CommitMotionKeyframeEdit("Edit keyframe",
            apply:  () => value = "new",
            revert: () => value = "old");

        store.Undo();
        Assert.Equal("old", value);

        store.Redo();
        Assert.Equal("new", value);
    }

    [Fact]
    public void CommitMotionKeyframeEdit_RevertToRemoval_SupportsNullOldKeyframePattern()
    {
        // Mirrors the real caller shape: when no keyframe existed at this time before the edit,
        // "revert" means removing the just-created keyframe entirely, not restoring some prior
        // snapshot. Modeled here as a nullable slot rather than an actual MotionKeyframe/service.
        var store = CreateStore();
        string? slot = null;

        // Matches every real caller: the mutation already happened before Commit* is called —
        // CommitMotionKeyframeEdit only registers apply/revert for future redo/undo, it doesn't
        // invoke apply() itself (mirrors ClipStore's other Commit* methods, e.g. CommitCalloutUpdate).
        slot = "created";
        store.CommitMotionKeyframeEdit("Edit keyframe",
            apply:  () => slot = "created",
            revert: () => slot = null);
        Assert.Equal("created", slot);

        store.Undo();
        Assert.Null(slot);

        store.Redo();
        Assert.Equal("created", slot);
    }

    [Fact]
    public void CommitMotionKeyframeEdit_InterleavesCorrectlyWithOtherUndoableActions()
    {
        // The whole point of item #63: a keyframe edit and an ordinary ClipStore mutation must
        // share ONE coherent undo stack, undoing most-recent-first regardless of which service
        // actually owned the data that changed.
        var store = CreateStore(o => o.MultiTrack = true);

        var kfValue = "kf-new";
        store.CommitMotionKeyframeEdit("Edit keyframe",
            apply:  () => kfValue = "kf-new",
            revert: () => kfValue = "kf-old");

        store.AddVideoTrack();
        Assert.Equal(2, store.VideoTracks.Count());

        store.Undo(); // undoes AddVideoTrack (most recent), not the keyframe edit
        Assert.Single(store.VideoTracks);
        Assert.Equal("kf-new", kfValue); // untouched — keyframe edit wasn't the top of the stack

        store.Undo(); // now undoes the keyframe edit
        Assert.Equal("kf-old", kfValue);
    }
}
