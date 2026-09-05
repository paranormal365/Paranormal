using Ben.Video.Editor.Models;
using Ben.Video.Editor.Services;
using Microsoft.Extensions.Options;

namespace Ben.Video.Tests.Services;

public sealed class ClipStoreTests
{
    private static ClipStore CreateStore(Action<VideoEditorOptions>? configure = null)
    {
        var options = new VideoEditorOptions();
        configure?.Invoke(options);
        return new ClipStore(Options.Create(options));
    }

    // ── Initialization ───────────────────────────────────────────────────────

    [Fact]
    public void Default_HasOneVideoTrack()
    {
        var store = CreateStore();

        Assert.Single(store.VideoTracks);
        Assert.Empty(store.AudioTracks);
    }

    [Fact]
    public void WithAudioTracksEnabled_HasOneAudioTrack()
    {
        var store = CreateStore(o => o.AudioTracks = true);

        Assert.Single(store.AudioTracks);
    }

    // ── Clip management ──────────────────────────────────────────────────────

    [Fact]
    public void AddClip_AppearsInClipsAndPrimaryTrack()
    {
        var store = CreateStore();
        var clip  = new VideoClip { Name = "test.mp4", Duration = 5 };

        store.AddClip(clip);

        Assert.Single(store.Clips);
        Assert.Single(store.PrimaryVideoTrack.VideoClips);
    }

    [Fact]
    public void RemoveClip_RemovesFromTrack()
    {
        var store = CreateStore();
        var clip  = new VideoClip { Name = "test.mp4" };
        store.AddClip(clip);

        store.RemoveClip(clip.Id);

        Assert.Empty(store.Clips);
    }

    [Fact]
    public void ReorderClips_UpdatesTrackItemOrder()
    {
        var store = CreateStore();
        var a     = new VideoClip { Name = "a.mp4" };
        var b     = new VideoClip { Name = "b.mp4" };
        store.AddClip(a);
        store.AddClip(b);

        store.ReorderClips([b, a]);

        var clips = store.Clips.ToList();
        Assert.Equal("b.mp4", clips[0].Name);
        Assert.Equal("a.mp4", clips[1].Name);
    }

    // ── Track management ─────────────────────────────────────────────────────

    [Fact]
    public void AddVideoTrack_WithMultiTrackEnabled_IncreasesTrackCount()
    {
        var store = CreateStore(o => o.MultiTrack = true);

        store.AddVideoTrack();

        Assert.Equal(2, store.VideoTracks.Count());
    }

    [Fact]
    public void AddVideoTrack_ExceedingMax_Throws()
    {
        var store = CreateStore(o => { o.MultiTrack = true; o.MaxVideoTracks = 1; });

        Assert.Throws<InvalidOperationException>(() => store.AddVideoTrack());
    }

    [Fact]
    public void AddAudioTrack_WithAudioEnabled_AddsTrack()
    {
        var store = CreateStore(o => o.AudioTracks = true);

        store.AddAudioTrack();

        Assert.Equal(2, store.AudioTracks.Count());
    }

    [Fact]
    public void RemoveTrack_PrimaryVideoTrack_Throws()
    {
        var store = CreateStore();

        Assert.Throws<InvalidOperationException>(() =>
            store.RemoveTrack(store.PrimaryVideoTrack.Id));
    }

    [Fact]
    public void AddVideoTrack_SupportsUndo()
    {
        var store = CreateStore(o => o.MultiTrack = true);

        store.AddVideoTrack();
        Assert.Equal(2, store.VideoTracks.Count());

        store.Undo();

        Assert.Single(store.VideoTracks);
    }

    [Fact]
    public void AddAudioTrack_SupportsUndo()
    {
        var store = CreateStore(o => o.AudioTracks = true);

        store.AddAudioTrack();
        Assert.Equal(2, store.AudioTracks.Count());

        store.Undo();

        Assert.Single(store.AudioTracks);
    }

    [Fact]
    public void RemoveTrack_SupportsUndo()
    {
        var store = CreateStore(o => o.MultiTrack = true);
        var track2 = store.AddVideoTrack();

        store.RemoveTrack(track2.Id);
        Assert.Single(store.VideoTracks);

        store.Undo();

        Assert.Equal(2, store.VideoTracks.Count());
        Assert.Contains(store.VideoTracks, t => t.Id == track2.Id);
    }

    [Fact]
    public void RemoveTrack_Undo_RestoresOriginalOrderIndex()
    {
        var store  = CreateStore(o => o.MultiTrack = true);
        var track2 = store.AddVideoTrack();
        var track3 = store.AddVideoTrack();

        store.RemoveTrack(track2.Id);
        store.Undo();

        var ordered = store.VideoTracks.ToList();
        Assert.Equal(track2.Id, ordered[1].Id);
        Assert.Equal(track3.Id, ordered[2].Id);
    }

    // ── Transition management ────────────────────────────────────────────────

    [Fact]
    public void AddTransition_CreatesTransitionItemOnTrack()
    {
        var store  = CreateStore(o => o.Transitions = true);
        var clipA  = new VideoClip { Name = "a.mp4", TimelinePosition = 0, Duration = 5 };
        var clipB  = new VideoClip { Name = "b.mp4", TimelinePosition = 5, Duration = 5 };
        store.AddClip(clipA);
        store.AddClip(clipB);
        var trackId = store.PrimaryVideoTrack.Id;

        store.AddTransition(trackId, clipA.Id, clipB.Id, TransitionStyle.Fade, 1.0);

        Assert.Single(store.AllTransitions);
    }

    // ── Text overlay management ──────────────────────────────────────────────

    [Fact]
    public void AddTextOverlay_AppearsInAllTextOverlays()
    {
        var store   = CreateStore(o => o.TextOverlays = true);
        var overlay = new TextOverlay
        {
            Name             = "Title",
            Text             = "Hello World",
            TimelinePosition = 2,
            Duration         = 3
        };

        store.AddTextOverlay(overlay);

        Assert.Single(store.AllTextOverlays);
        Assert.Equal("Hello World", store.AllTextOverlays.First().Text);
    }

    // ── OnChange event ───────────────────────────────────────────────────────

    [Fact]
    public void AddClip_RaisesOnChange()
    {
        var store = CreateStore();
        var raised = false;
        store.OnChange += () => raised = true;

        store.AddClip(new VideoClip());

        Assert.True(raised);
    }

    [Fact]
    public void RemoveClip_RaisesOnChange()
    {
        var store = CreateStore();
        var clip  = new VideoClip();
        store.AddClip(clip);
        var raised = false;
        store.OnChange += () => raised = true;

        store.RemoveClip(clip.Id);

        Assert.True(raised);
    }

    // ── TotalDuration ────────────────────────────────────────────────────────

    [Fact]
    public void TotalDuration_EmptyStore_ReturnsZero()
    {
        var store = CreateStore();

        Assert.Equal(0, store.TotalDuration);
    }

    [Fact]
    public void TotalDuration_WithClips_ReturnsMaxEndTime()
    {
        var store = CreateStore();
        store.AddClip(new VideoClip { TimelinePosition = 0, Duration = 10 });
        store.AddClip(new VideoClip { TimelinePosition = 10, Duration = 5 });

        Assert.Equal(15, store.TotalDuration);
    }

    // ── UpdateTrim ───────────────────────────────────────────────────────────

    [Fact]
    public void UpdateTrim_SetsStartAndEnd()
    {
        var store = CreateStore();
        var clip  = new VideoClip { Name = "a.mp4", Duration = 10 };
        store.AddClip(clip);

        store.UpdateTrim(clip.Id, 1.0, 8.0);

        var updated = store.PrimaryVideoTrack.VideoClips.Single();
        Assert.Equal(1.0, updated.StartTrim);
        Assert.Equal(8.0, updated.EndTrim);
    }

    [Fact]
    public void UpdateTrim_ClampsValuesToSourceDuration()
    {
        var store = CreateStore();
        var clip  = new VideoClip { Name = "a.mp4", Duration = 5 };
        store.AddClip(clip);

        store.UpdateTrim(clip.Id, -2.0, 99.0);

        var updated = store.PrimaryVideoTrack.VideoClips.Single();
        Assert.Equal(0.0, updated.StartTrim);
        Assert.Equal(5.0, updated.EndTrim);
    }

    [Fact]
    public void UpdateTrim_StartGreaterThanOrEqualEnd_DoesNotUpdate()
    {
        var store = CreateStore();
        var clip  = new VideoClip { Name = "a.mp4", Duration = 10, StartTrim = 2, EndTrim = 8 };
        store.AddClip(clip);

        store.UpdateTrim(clip.Id, 6.0, 6.0); // start == end — invalid

        var updated = store.PrimaryVideoTrack.VideoClips.Single();
        Assert.Equal(2.0, updated.StartTrim); // unchanged
        Assert.Equal(8.0, updated.EndTrim);   // unchanged
    }

    [Fact]
    public void UpdateTrim_RaisesOnChange()
    {
        var store  = CreateStore();
        var clip   = new VideoClip { Name = "a.mp4", Duration = 10 };
        store.AddClip(clip);
        var raised = false;
        store.OnChange += () => raised = true;

        store.UpdateTrim(clip.Id, 1.0, 9.0);

        Assert.True(raised);
    }

    [Fact]
    public void UpdateTrim_UnknownId_DoesNothing()
    {
        var store = CreateStore();
        // Should not throw for an unknown id
        store.UpdateTrim(Guid.NewGuid(), 1.0, 5.0);
    }

    // ── SplitClip ────────────────────────────────────────────────────────────

    [Fact]
    public void SplitClip_ReplacesSingleClipWithTwo()
    {
        var store = CreateStore();
        var clip  = new VideoClip { Name = "test.mp4", Duration = 10 };
        store.AddClip(clip);

        store.SplitClip(clip.Id, 4.0);

        Assert.Equal(2, store.PrimaryVideoTrack.Items.Count);
    }

    [Fact]
    public void SplitClip_FirstHasCorrectEndTrim()
    {
        var store = CreateStore();
        var clip  = new VideoClip { Name = "test.mp4", Duration = 10 };
        store.AddClip(clip);

        store.SplitClip(clip.Id, 4.0);

        var first = (VideoClip)store.PrimaryVideoTrack.Items[0];
        Assert.Equal(0.0, first.StartTrim);
        Assert.Equal(4.0, first.EndTrim);
    }

    [Fact]
    public void SplitClip_SecondHasCorrectStartTrim()
    {
        var store = CreateStore();
        var clip  = new VideoClip { Name = "test.mp4", Duration = 10 };
        store.AddClip(clip);

        store.SplitClip(clip.Id, 4.0);

        var second = (VideoClip)store.PrimaryVideoTrack.Items[1];
        Assert.Equal(4.0, second.StartTrim);
        Assert.Equal(10.0, second.EndTrim);
    }

    [Fact]
    public void SplitClip_SecondStartsWhereFirstEnds()
    {
        var store = CreateStore();
        var clip  = new VideoClip { Name = "test.mp4", Duration = 10, TimelinePosition = 0 };
        store.AddClip(clip);

        store.SplitClip(clip.Id, 4.0);

        var first  = store.PrimaryVideoTrack.Items[0];
        var second = store.PrimaryVideoTrack.Items[1];
        Assert.Equal(0.0, first.TimelinePosition);
        Assert.Equal(4.0, second.TimelinePosition);
    }

    [Fact]
    public void SplitClip_NamesAppendAandB()
    {
        var store = CreateStore();
        var clip  = new VideoClip { Name = "clip", Duration = 10 };
        store.AddClip(clip);

        store.SplitClip(clip.Id, 5.0);

        Assert.Equal("clip A", store.PrimaryVideoTrack.Items[0].Name);
        Assert.Equal("clip B", store.PrimaryVideoTrack.Items[1].Name);
    }

    [Fact]
    public void SplitClip_RespectsExistingTrims()
    {
        var store = CreateStore();
        var clip  = new VideoClip { Name = "c.mp4", Duration = 20, StartTrim = 5, EndTrim = 15 };
        store.AddClip(clip);

        store.SplitClip(clip.Id, 5.0); // 5s into the trimmed region = source offset 10

        var first  = (VideoClip)store.PrimaryVideoTrack.Items[0];
        var second = (VideoClip)store.PrimaryVideoTrack.Items[1];
        Assert.Equal(5.0,  first.StartTrim);
        Assert.Equal(10.0, first.EndTrim);
        Assert.Equal(10.0, second.StartTrim);
        Assert.Equal(15.0, second.EndTrim);
    }

    [Fact]
    public void SplitClip_SplitAtZero_Throws()
    {
        var store = CreateStore();
        var clip  = new VideoClip { Name = "c.mp4", Duration = 10 };
        store.AddClip(clip);

        Assert.Throws<ArgumentOutOfRangeException>(() => store.SplitClip(clip.Id, 0.0));
    }

    [Fact]
    public void SplitClip_SplitAtEnd_Throws()
    {
        var store = CreateStore();
        var clip  = new VideoClip { Name = "c.mp4", Duration = 10 };
        store.AddClip(clip);

        Assert.Throws<ArgumentOutOfRangeException>(() => store.SplitClip(clip.Id, 10.0));
    }

    [Fact]
    public void SplitClip_UnknownId_Throws()
    {
        var store = CreateStore();

        Assert.Throws<ArgumentException>(() => store.SplitClip(Guid.NewGuid(), 5.0));
    }

    [Fact]
    public void SplitClip_RaisesOnChange()
    {
        var store  = CreateStore();
        var clip   = new VideoClip { Name = "a.mp4", Duration = 10 };
        store.AddClip(clip);
        var raised = false;
        store.OnChange += () => raised = true;

        store.SplitClip(clip.Id, 5.0);

        Assert.True(raised);
    }

    [Fact]
    public void SplitClip_OrdersResultsCorrectly()
    {
        var store  = CreateStore();
        var clipA  = new VideoClip { Name = "a.mp4", Duration = 10 };
        var clipB  = new VideoClip { Name = "b.mp4", Duration = 8 };
        store.AddClip(clipA);
        store.AddClip(clipB);

        store.SplitClip(clipA.Id, 5.0);

        // Should be: clipA-A (order 0), clipA-B (order 1), clipB (order 2)
        var items = store.PrimaryVideoTrack.Items.OrderBy(i => i.Order).ToList();
        Assert.Equal("a.mp4 A", items[0].Name);
        Assert.Equal("a.mp4 B", items[1].Name);
        Assert.Equal("b.mp4",   items[2].Name);
    }

    [Fact]
    public void SplitClip_IsUndoable()
    {
        var store = CreateStore();
        var clip  = new VideoClip { Name = "test.mp4", Duration = 10 };
        store.AddClip(clip);

        store.SplitClip(clip.Id, 4.0);
        Assert.Equal(2, store.PrimaryVideoTrack.Items.Count);

        store.Undo();
        Assert.Single(store.PrimaryVideoTrack.Items);
        Assert.Equal("test.mp4", store.PrimaryVideoTrack.Items[0].Name);

        store.Redo();
        Assert.Equal(2, store.PrimaryVideoTrack.Items.Count);
    }

    // ── SplitClip (AudioClip) ────────────────────────────────────────────────

    [Fact]
    public void SplitClip_AudioClip_ReplacesSingleClipWithTwo()
    {
        var store = CreateStore(o => o.AudioTracks = true);
        var track = store.AudioTracks.First();
        var clip  = new AudioClip { Name = "a.mp3", Duration = 10 };
        store.AddClipToTrack(track.Id, clip);

        store.SplitClip(clip.Id, 4.0);

        Assert.Equal(2, track.Items.Count);
    }

    [Fact]
    public void SplitClip_AudioClip_PreservesTrimsAndTimelinePosition()
    {
        var store = CreateStore(o => o.AudioTracks = true);
        var track = store.AudioTracks.First();
        var clip  = new AudioClip { Name = "a.mp3", Duration = 20, StartTrim = 5, EndTrim = 15, TimelinePosition = 2 };
        store.AddClipToTrack(track.Id, clip);

        store.SplitClip(clip.Id, 5.0); // 5s into the trimmed region = source offset 10

        var first  = (AudioClip)track.Items[0];
        var second = (AudioClip)track.Items[1];
        Assert.Equal(5.0,  first.StartTrim);
        Assert.Equal(10.0, first.EndTrim);
        Assert.Equal(2.0,  first.TimelinePosition);
        Assert.Equal(10.0, second.StartTrim);
        Assert.Equal(15.0, second.EndTrim);
        Assert.Equal(7.0,  second.TimelinePosition);
    }

    [Fact]
    public void SplitClip_AudioClip_RedistributesVolumeAutomation()
    {
        var store = CreateStore(o => o.AudioTracks = true);
        var track = store.AudioTracks.First();
        var clip  = new AudioClip
        {
            Name = "a.mp3",
            Duration = 10,
            VolumeAutomation =
            [
                new VolumeKeyframe { Position = 0.0, Volume = 1.0 },  // t=0s  -> first half
                new VolumeKeyframe { Position = 0.2, Volume = 0.5 },  // t=2s  -> first half
                new VolumeKeyframe { Position = 0.8, Volume = 0.2 },  // t=8s  -> second half
                new VolumeKeyframe { Position = 1.0, Volume = 0.0 },  // t=10s -> second half
            ],
        };
        store.AddClipToTrack(track.Id, clip);

        store.SplitClip(clip.Id, 4.0); // split at t=4s

        var first  = (AudioClip)track.Items[0];
        var second = (AudioClip)track.Items[1];
        Assert.Equal(2, first.VolumeAutomation.Count);
        Assert.Equal(2, second.VolumeAutomation.Count);
        Assert.Equal(0.0,  first.VolumeAutomation[0].Position, 3);
        Assert.Equal(0.5,  first.VolumeAutomation[1].Position, 3); // t=2s within a 4s-long first half
        Assert.Equal(1.0,  second.VolumeAutomation[1].Position, 3); // t=10s within a 6s-long second half
    }

    [Fact]
    public void SplitClip_AudioClip_ClearsFadeAtInternalCutPoint()
    {
        var store = CreateStore(o => o.AudioTracks = true);
        var track = store.AudioTracks.First();
        var clip  = new AudioClip { Name = "a.mp3", Duration = 10, FadeInSeconds = 1.0, FadeOutSeconds = 1.0 };
        store.AddClipToTrack(track.Id, clip);

        store.SplitClip(clip.Id, 4.0);

        var first  = (AudioClip)track.Items[0];
        var second = (AudioClip)track.Items[1];
        Assert.Equal(1.0, first.FadeInSeconds);  // true start of source media — kept
        Assert.Equal(0.0, first.FadeOutSeconds); // now an internal cut — cleared
        Assert.Equal(0.0, second.FadeInSeconds); // now an internal cut — cleared
        Assert.Equal(1.0, second.FadeOutSeconds); // true end of source media — kept
    }

    [Fact]
    public void SplitClip_AudioClip_IsUndoable()
    {
        var store = CreateStore(o => o.AudioTracks = true);
        var track = store.AudioTracks.First();
        var clip  = new AudioClip { Name = "a.mp3", Duration = 10 };
        store.AddClipToTrack(track.Id, clip);

        store.SplitClip(clip.Id, 4.0);
        Assert.Equal(2, track.Items.Count);

        store.Undo();
        Assert.Single(track.Items);
        Assert.Equal("a.mp3", track.Items[0].Name);
    }

    // ── SplitClip (ImageClip) ────────────────────────────────────────────────

    [Fact]
    public void SplitClip_ImageClip_ReplacesSingleClipWithTwo()
    {
        var store = CreateStore();
        var clip  = new ImageClip { Name = "photo.png", Duration = 10 };
        store.AddImageClip(clip);

        store.SplitClip(clip.Id, 4.0);

        Assert.Equal(2, store.PrimaryVideoTrack.Items.Count);
    }

    [Fact]
    public void SplitClip_ImageClip_SplitsDurationAndPositionCorrectly()
    {
        var store = CreateStore();
        var clip  = new ImageClip { Name = "photo.png", Duration = 10, TimelinePosition = 3 };
        store.AddImageClip(clip);

        store.SplitClip(clip.Id, 4.0);

        var first  = (ImageClip)store.PrimaryVideoTrack.Items[0];
        var second = (ImageClip)store.PrimaryVideoTrack.Items[1];
        Assert.Equal(4.0, first.Duration);
        Assert.Equal(3.0, first.TimelinePosition);
        Assert.Equal(6.0, second.Duration);
        Assert.Equal(7.0, second.TimelinePosition);
    }

    [Fact]
    public void SplitClip_ImageClip_SplitAtZero_Throws()
    {
        var store = CreateStore();
        var clip  = new ImageClip { Name = "photo.png", Duration = 10 };
        store.AddImageClip(clip);

        Assert.Throws<ArgumentOutOfRangeException>(() => store.SplitClip(clip.Id, 0.0));
    }

    [Fact]
    public void SplitClip_ImageClip_IsUndoable()
    {
        var store = CreateStore();
        var clip  = new ImageClip { Name = "photo.png", Duration = 10 };
        store.AddImageClip(clip);

        store.SplitClip(clip.Id, 4.0);
        Assert.Equal(2, store.PrimaryVideoTrack.Items.Count);

        store.Undo();
        Assert.Single(store.PrimaryVideoTrack.Items);
        Assert.Equal("photo.png", store.PrimaryVideoTrack.Items[0].Name);
    }

    // ── Undo ─────────────────────────────────────────────────────────────────

    [Fact]
    public void CanUndo_IsFalse_WhenNoMutations()
    {
        var store = CreateStore();
        Assert.False(store.CanUndo);
    }

    [Fact]
    public void CanUndo_IsTrue_AfterRemoveClip()
    {
        var store = CreateStore();
        var clip  = new VideoClip { Name = "undo.mp4", Duration = 5 };
        store.AddClip(clip);

        store.RemoveClip(clip.Id);

        Assert.True(store.CanUndo);
    }

    [Fact]
    public void UndoLastRemove_RestoresClip_ToOriginalTrack()
    {
        var store = CreateStore();
        var clip  = new VideoClip { Name = "undo.mp4", Duration = 5 };
        store.AddClip(clip);
        var trackId = store.PrimaryVideoTrack.Id;

        store.RemoveClip(clip.Id);
        store.UndoLastRemove();

        // Clip is restored — the AddClip command is still on the undo stack
        Assert.Contains(store.PrimaryVideoTrack.Items, i => i.Id == clip.Id);
        Assert.True(store.CanUndo); // AddClip command remains
    }

    [Fact]
    public void UndoLastRemove_WhenNothingToUndo_DoesNotThrow()
    {
        var store = CreateStore();
        var ex    = Record.Exception(() => store.UndoLastRemove());
        Assert.Null(ex);
    }

    [Fact]
    public void UndoLastRemove_OnlyRestoresLastRemoval()
    {
        var store = CreateStore();
        var clip1 = new VideoClip { Name = "first.mp4",  Duration = 3 };
        var clip2 = new VideoClip { Name = "second.mp4", Duration = 3 };
        store.AddClip(clip1);
        store.AddClip(clip2);

        store.RemoveClip(clip1.Id);
        store.RemoveClip(clip2.Id);

        // Only clip2 (last removed) should come back on a single undo
        store.UndoLastRemove();

        Assert.Contains(store.PrimaryVideoTrack.Items, i => i.Id == clip2.Id);
        Assert.DoesNotContain(store.PrimaryVideoTrack.Items, i => i.Id == clip1.Id);
        Assert.True(store.CanUndo); // remove-clip1 + two add commands still on stack
    }

    [Fact]
    public void CanUndo_IsFalse_AfterUndoConsumed()
    {
        var store = CreateStore();
        var clip  = new VideoClip { Name = "undo.mp4", Duration = 5 };
        store.AddClip(clip);
        store.RemoveClip(clip.Id);
        store.UndoLastRemove(); // undoes the remove; AddClip is still on stack

        // The AddClip command is still available to undo
        Assert.True(store.CanUndo);

        store.Undo(); // undo the add
        Assert.False(store.CanUndo);
    }

    // ── Transition management ──────────────────────────────────────────────

    private static (ClipStore store, Guid trackId, VideoClip clipA, VideoClip clipB)
        CreateStoreWithTwoClips()
    {
        var store = CreateStore(o => o.Transitions = true);
        var clipA = new VideoClip { Name = "a.mp4", Duration = 5 };
        var clipB = new VideoClip { Name = "b.mp4", Duration = 5 };
        store.AddClip(clipA);
        store.AddClip(clipB);
        return (store, store.PrimaryVideoTrack.Id, clipA, clipB);
    }

    [Fact]
    public void AddTransition_AddsTransitionItem_ToTrack()
    {
        var (store, trackId, clipA, clipB) = CreateStoreWithTwoClips();

        store.AddTransition(trackId, clipA.Id, clipB.Id, TransitionStyle.Dissolve, 1.0);

        var transition = store.PrimaryVideoTrack.Items.OfType<Transition>().Single();
        Assert.Equal(TransitionStyle.Dissolve, transition.Style);
        Assert.Equal(clipA.Id, transition.FromClipId);
        Assert.Equal(clipB.Id, transition.ToClipId);
        Assert.Equal(1.0, transition.Duration);
    }

    [Fact]
    public void AddTransition_UnknownFromClip_Throws()
    {
        var (store, trackId, _, clipB) = CreateStoreWithTwoClips();
        Assert.Throws<ArgumentException>(
            () => store.AddTransition(trackId, Guid.NewGuid(), clipB.Id, TransitionStyle.Fade, 1.0));
    }

    [Fact]
    public void AddTransition_UnknownToClip_Throws()
    {
        var (store, trackId, clipA, _) = CreateStoreWithTwoClips();
        Assert.Throws<ArgumentException>(
            () => store.AddTransition(trackId, clipA.Id, Guid.NewGuid(), TransitionStyle.Fade, 1.0));
    }

    [Fact]
    /// <summary>
    /// A transition covers the stretch where both clips play, and pulls the second one back to
    /// create it.
    /// </summary>
    /// <remarks>
    /// It used to be centred on the junction and moved nothing, which meant the timeline claimed a
    /// length the render never produced: ffmpeg's xfade output is A + B − d, so every marker,
    /// overlay and audio clip after the junction sat later than whatever it had been lined up with
    /// on screen (2026-09-05 audit, transitions-3).
    /// </remarks>
    public void AddTransition_CoversTheOverlapItCreates()
    {
        var (store, trackId, clipA, clipB) = CreateStoreWithTwoClips();

        store.AddTransition(trackId, clipA.Id, clipB.Id, TransitionStyle.Fade, 2.0);

        var t = store.PrimaryVideoTrack.Items.OfType<Transition>().Single();
        var clipAEnd = clipA.TimelinePosition + clipA.TrimmedDuration;

        Assert.Equal(clipAEnd - 2.0, t.TimelinePosition, precision: 5);
        Assert.Equal(2.0, t.Duration, precision: 5);

        // The second clip moved back to meet it, so the two overlap by exactly the crossfade.
        Assert.Equal(clipAEnd - 2.0, clipB.TimelinePosition, precision: 5);
        Assert.Null(store.ValidateAll());
    }

    [Fact]
    public void AddTransition_RaisesOnChange()
    {
        var (store, trackId, clipA, clipB) = CreateStoreWithTwoClips();
        var raised = false;
        store.OnChange += () => raised = true;

        store.AddTransition(trackId, clipA.Id, clipB.Id, TransitionStyle.Zoom, 1.0);

        Assert.True(raised);
    }

    [Fact]
    public void UpdateTransition_ChangesStyleAndDuration()
    {
        var (store, trackId, clipA, clipB) = CreateStoreWithTwoClips();
        store.AddTransition(trackId, clipA.Id, clipB.Id, TransitionStyle.Fade, 1.0);
        var t = store.PrimaryVideoTrack.Items.OfType<Transition>().Single();

        store.UpdateTransition(t.Id, TransitionStyle.WipeLeft, 2.0);

        Assert.Equal(TransitionStyle.WipeLeft, t.Style);
        Assert.Equal(2.0, t.Duration, precision: 5);
    }

    [Fact]
    public void UpdateTransition_RecalculatesTimelinePosition()
    {
        var (store, trackId, clipA, clipB) = CreateStoreWithTwoClips();
        clipA.TimelinePosition = 0;
        clipA.Duration         = 5;
        store.AddTransition(trackId, clipA.Id, clipB.Id, TransitionStyle.Fade, 1.0);
        var t = store.PrimaryVideoTrack.Items.OfType<Transition>().Single();

        store.UpdateTransition(t.Id, TransitionStyle.Dissolve, 2.0);

        Assert.Equal(clipA.TimelinePosition + clipA.Duration - 1.0, t.TimelinePosition, precision: 5);
    }

    [Fact]
    public void UpdateTransition_InvalidDuration_Throws()
    {
        var (store, trackId, clipA, clipB) = CreateStoreWithTwoClips();
        store.AddTransition(trackId, clipA.Id, clipB.Id, TransitionStyle.Fade, 1.0);
        var t = store.PrimaryVideoTrack.Items.OfType<Transition>().Single();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => store.UpdateTransition(t.Id, TransitionStyle.Fade, 0));
    }

    [Fact]
    public void UpdateTransition_UnknownId_Throws()
    {
        var (store, _, _, _) = CreateStoreWithTwoClips();
        Assert.Throws<ArgumentException>(
            () => store.UpdateTransition(Guid.NewGuid(), TransitionStyle.Fade, 1.0));
    }

    [Fact]
    public void UpdateTransition_RaisesOnChange()
    {
        var (store, trackId, clipA, clipB) = CreateStoreWithTwoClips();
        store.AddTransition(trackId, clipA.Id, clipB.Id, TransitionStyle.Fade, 1.0);
        var t = store.PrimaryVideoTrack.Items.OfType<Transition>().Single();
        var raised = false;
        store.OnChange += () => raised = true;

        store.UpdateTransition(t.Id, TransitionStyle.Zoom, 1.5);

        Assert.True(raised);
    }

    [Fact]
    public void RemoveTransition_RemovesFromTrack()
    {
        var (store, trackId, clipA, clipB) = CreateStoreWithTwoClips();
        store.AddTransition(trackId, clipA.Id, clipB.Id, TransitionStyle.Fade, 1.0);
        var t = store.PrimaryVideoTrack.Items.OfType<Transition>().Single();

        store.RemoveTransition(t.Id);

        Assert.Empty(store.PrimaryVideoTrack.Items.OfType<Transition>());
    }

    [Fact]
    public void RemoveTransition_RaisesOnChange()
    {
        var (store, trackId, clipA, clipB) = CreateStoreWithTwoClips();
        store.AddTransition(trackId, clipA.Id, clipB.Id, TransitionStyle.Fade, 1.0);
        var t     = store.PrimaryVideoTrack.Items.OfType<Transition>().Single();
        var raised = false;
        store.OnChange += () => raised = true;

        store.RemoveTransition(t.Id);

        Assert.True(raised);
    }

    // T0 (transitions GUI arc): AddTransition/UpdateTransition were not undoable before this
    // fix — RemoveTransition already was (it delegates to RemoveClip).

    [Fact]
    public void AddTransition_IsUndoable()
    {
        var (store, trackId, clipA, clipB) = CreateStoreWithTwoClips();

        store.AddTransition(trackId, clipA.Id, clipB.Id, TransitionStyle.Dissolve, 1.0);
        Assert.Single(store.PrimaryVideoTrack.Items.OfType<Transition>());

        Assert.True(store.CanUndo);
        store.Undo();

        Assert.Empty(store.PrimaryVideoTrack.Items.OfType<Transition>());
    }

    [Fact]
    public void AddTransition_UndoThenRedo_RestoresTransition()
    {
        var (store, trackId, clipA, clipB) = CreateStoreWithTwoClips();
        store.AddTransition(trackId, clipA.Id, clipB.Id, TransitionStyle.WipeRight, 1.5);
        var original = store.PrimaryVideoTrack.Items.OfType<Transition>().Single();

        store.Undo();
        Assert.Empty(store.PrimaryVideoTrack.Items.OfType<Transition>());

        store.Redo();
        var restored = store.PrimaryVideoTrack.Items.OfType<Transition>().Single();
        Assert.Equal(original.Id, restored.Id);
        Assert.Equal(TransitionStyle.WipeRight, restored.Style);
        Assert.Equal(1.5, restored.Duration, precision: 5);
    }

    [Fact]
    public void UpdateTransition_IsUndoable_RestoresStyleDurationAndPosition()
    {
        var (store, trackId, clipA, clipB) = CreateStoreWithTwoClips();
        clipA.TimelinePosition = 0;
        clipA.Duration         = 5;
        store.AddTransition(trackId, clipA.Id, clipB.Id, TransitionStyle.Fade, 1.0);
        var t = store.PrimaryVideoTrack.Items.OfType<Transition>().Single();
        var originalStyle    = t.Style;
        var originalDuration = t.Duration;
        var originalPosition = t.TimelinePosition;

        var originalClipBPosition = clipB.TimelinePosition;

        store.UpdateTransition(t.Id, TransitionStyle.WipeLeft, 2.0);
        Assert.Equal(TransitionStyle.WipeLeft, t.Style);

        // Two steps now: the style and duration change, and the clip moving to match the longer
        // overlap it asks for.
        store.Undo();
        store.Undo();

        Assert.Equal(originalStyle, t.Style);
        Assert.Equal(originalDuration, t.Duration, precision: 5);
        Assert.Equal(originalPosition, t.TimelinePosition, precision: 5);
        Assert.Equal(originalClipBPosition, clipB.TimelinePosition, precision: 5);
    }

    [Fact]
    public void UpdateTransition_UndoThenRedo_ReappliesNewValues()
    {
        var (store, trackId, clipA, clipB) = CreateStoreWithTwoClips();
        store.AddTransition(trackId, clipA.Id, clipB.Id, TransitionStyle.Fade, 1.0);
        var t = store.PrimaryVideoTrack.Items.OfType<Transition>().Single();

        store.UpdateTransition(t.Id, TransitionStyle.Zoom, 2.5);
        store.Undo();
        store.Redo();

        Assert.Equal(TransitionStyle.Zoom, t.Style);
        Assert.Equal(2.5, t.Duration, precision: 5);
    }

    // ── ApplyStyleToAllTransitions (item #57 T4) ───────────────────────────────

    [Fact]
    public void ApplyStyleToAllTransitions_ChangesStyleOnEveryTransition_KeepsOwnDuration()
    {
        var store = CreateStore(o => o.Transitions = true);
        var clipA = new VideoClip { Name = "a.mp4", TimelinePosition = 0,  Duration = 5 };
        var clipB = new VideoClip { Name = "b.mp4", TimelinePosition = 5,  Duration = 5 };
        var clipC = new VideoClip { Name = "c.mp4", TimelinePosition = 10, Duration = 5 };
        store.AddClip(clipA);
        store.AddClip(clipB);
        store.AddClip(clipC);
        var trackId = store.PrimaryVideoTrack.Id;
        store.AddTransition(trackId, clipA.Id, clipB.Id, TransitionStyle.Fade, 1.0);
        store.AddTransition(trackId, clipB.Id, clipC.Id, TransitionStyle.Dissolve, 2.0);

        store.ApplyStyleToAllTransitions(TransitionStyle.Zoom);

        var transitions = store.AllTransitions.OrderBy(t => t.TimelinePosition).ToList();
        Assert.Equal(2, transitions.Count);
        Assert.All(transitions, t => Assert.Equal(TransitionStyle.Zoom, t.Style));
        Assert.Equal(1.0, transitions[0].Duration, precision: 5);
        Assert.Equal(2.0, transitions[1].Duration, precision: 5);
    }

    [Fact]
    public void ApplyStyleToAllTransitions_NoTransitions_NoOp()
    {
        var (store, _, _, _) = CreateStoreWithTwoClips();
        var raised = false;
        store.OnChange += () => raised = true;

        store.ApplyStyleToAllTransitions(TransitionStyle.Zoom);

        Assert.False(raised);
    }

    [Fact]
    public void ApplyStyleToAllTransitions_IsUndoableAsOneEntry()
    {
        var store = CreateStore(o => o.Transitions = true);
        var clipA = new VideoClip { Name = "a.mp4", TimelinePosition = 0,  Duration = 5 };
        var clipB = new VideoClip { Name = "b.mp4", TimelinePosition = 5,  Duration = 5 };
        var clipC = new VideoClip { Name = "c.mp4", TimelinePosition = 10, Duration = 5 };
        store.AddClip(clipA);
        store.AddClip(clipB);
        store.AddClip(clipC);
        var trackId = store.PrimaryVideoTrack.Id;
        store.AddTransition(trackId, clipA.Id, clipB.Id, TransitionStyle.Fade, 1.0);
        store.AddTransition(trackId, clipB.Id, clipC.Id, TransitionStyle.Dissolve, 2.0);

        store.ApplyStyleToAllTransitions(TransitionStyle.Zoom);
        store.Undo();

        var transitions = store.AllTransitions.OrderBy(t => t.TimelinePosition).ToList();
        Assert.Equal(TransitionStyle.Fade, transitions[0].Style);
        Assert.Equal(TransitionStyle.Dissolve, transitions[1].Style);
    }

    [Fact]
    public void ApplyStyleToAllTransitions_UndoThenRedo_ReappliesToAll()
    {
        var store = CreateStore(o => o.Transitions = true);
        var clipA = new VideoClip { Name = "a.mp4", TimelinePosition = 0,  Duration = 5 };
        var clipB = new VideoClip { Name = "b.mp4", TimelinePosition = 5,  Duration = 5 };
        var clipC = new VideoClip { Name = "c.mp4", TimelinePosition = 10, Duration = 5 };
        store.AddClip(clipA);
        store.AddClip(clipB);
        store.AddClip(clipC);
        var trackId = store.PrimaryVideoTrack.Id;
        store.AddTransition(trackId, clipA.Id, clipB.Id, TransitionStyle.Fade, 1.0);
        store.AddTransition(trackId, clipB.Id, clipC.Id, TransitionStyle.Dissolve, 2.0);

        store.ApplyStyleToAllTransitions(TransitionStyle.Zoom);
        store.Undo();
        store.Redo();

        Assert.All(store.AllTransitions, t => Assert.Equal(TransitionStyle.Zoom, t.Style));
    }

    // ── Cross-track transition management ────────────────────────────────────

    [Fact]
    public void AddCrossTrackTransition_ComputesOverlapWindow_FromTwoTracks()
    {
        var store  = CreateStore(o => { o.Transitions = true; o.MultiTrack = true; });
        var clipA  = new VideoClip { Name = "a.mp4", TimelinePosition = 0,   Duration = 4 };
        var clipB  = new VideoClip { Name = "b.mp4", TimelinePosition = 2.5, Duration = 4 };
        store.AddClip(clipA);
        var track2 = store.AddVideoTrack();
        store.AddClipToTrack(track2.Id, clipB);

        store.AddCrossTrackTransition(clipA.Id, clipB.Id, TransitionStyle.Dissolve);

        var transition = store.AllTransitions.Single();
        Assert.Equal(TransitionStyle.Dissolve, transition.Style);
        Assert.Equal(clipA.Id, transition.FromClipId);
        Assert.Equal(clipB.Id, transition.ToClipId);
        Assert.Equal(2.5, transition.TimelinePosition, precision: 5);
        Assert.Equal(1.5, transition.Duration, precision: 5); // overlap: 2.5..4.0
    }

    [Fact]
    public void AddCrossTrackTransition_OrdersFromToByTrackOrder_RegardlessOfArgumentOrder()
    {
        var store  = CreateStore(o => { o.Transitions = true; o.MultiTrack = true; });
        var clipA  = new VideoClip { Name = "a.mp4", TimelinePosition = 0,   Duration = 4 };
        var clipB  = new VideoClip { Name = "b.mp4", TimelinePosition = 2.5, Duration = 4 };
        store.AddClip(clipA);
        var track2 = store.AddVideoTrack();
        store.AddClipToTrack(track2.Id, clipB);

        // Pass the higher-track clip first — result should still be from=A (lower track), to=B.
        store.AddCrossTrackTransition(clipB.Id, clipA.Id);

        var transition = store.AllTransitions.Single();
        Assert.Equal(clipA.Id, transition.FromClipId);
        Assert.Equal(clipB.Id, transition.ToClipId);
    }

    [Fact]
    public void AddCrossTrackTransition_InsertsOnHigherOrderTrack()
    {
        var store  = CreateStore(o => { o.Transitions = true; o.MultiTrack = true; });
        var clipA  = new VideoClip { Name = "a.mp4", TimelinePosition = 0,   Duration = 4 };
        var clipB  = new VideoClip { Name = "b.mp4", TimelinePosition = 2.5, Duration = 4 };
        store.AddClip(clipA);
        var track2 = store.AddVideoTrack();
        store.AddClipToTrack(track2.Id, clipB);

        store.AddCrossTrackTransition(clipA.Id, clipB.Id);

        Assert.Single(track2.Items.OfType<Transition>());
        Assert.Empty(store.PrimaryVideoTrack.Items.OfType<Transition>());
    }

    [Fact]
    public void AddCrossTrackTransition_ThrowsWhenClipsDoNotOverlap()
    {
        var store  = CreateStore(o => { o.Transitions = true; o.MultiTrack = true; });
        var clipA  = new VideoClip { Name = "a.mp4", TimelinePosition = 0, Duration = 4 };
        var clipB  = new VideoClip { Name = "b.mp4", TimelinePosition = 10, Duration = 4 };
        store.AddClip(clipA);
        var track2 = store.AddVideoTrack();
        store.AddClipToTrack(track2.Id, clipB);

        Assert.Throws<ArgumentException>(() => store.AddCrossTrackTransition(clipA.Id, clipB.Id));
    }

    [Fact]
    public void AddCrossTrackTransition_ThrowsWhenClipsOnSameTrack()
    {
        var (store, _, clipA, clipB) = CreateStoreWithTwoClips();

        Assert.Throws<InvalidOperationException>(() => store.AddCrossTrackTransition(clipA.Id, clipB.Id));
    }

    [Fact]
    public void AddCrossTrackTransition_SupportsUndo()
    {
        var store  = CreateStore(o => { o.Transitions = true; o.MultiTrack = true; });
        var clipA  = new VideoClip { Name = "a.mp4", TimelinePosition = 0,   Duration = 4 };
        var clipB  = new VideoClip { Name = "b.mp4", TimelinePosition = 2.5, Duration = 4 };
        store.AddClip(clipA);
        var track2 = store.AddVideoTrack();
        store.AddClipToTrack(track2.Id, clipB);
        store.AddCrossTrackTransition(clipA.Id, clipB.Id);
        Assert.Single(store.AllTransitions);

        store.Undo();

        Assert.Empty(store.AllTransitions);
    }

    // ── Text overlay management ────────────────────────────────────────────

    private static TextOverlay MakeOverlay(string text = "Hello", double position = 0, double duration = 5) =>
        new() { Text = text, Name = text, TimelinePosition = position, Duration = duration };

    [Fact]
    public void AddTextOverlay_AppearsOnPrimaryTrack()
    {
        var store   = CreateStore(o => o.TextOverlays = true);
        var overlay = MakeOverlay();

        store.AddTextOverlay(overlay);

        Assert.Contains(store.PrimaryVideoTrack.Items, i => i.Id == overlay.Id);
    }

    [Fact]
    public void AddTextOverlay_NewOverlay_AppearsInAllTextOverlays()
    {
        var store   = CreateStore(o => o.TextOverlays = true);
        var overlay = MakeOverlay("World");

        store.AddTextOverlay(overlay);

        Assert.Contains(store.AllTextOverlays, o => o.Id == overlay.Id);
    }

    [Fact]
    public void AddTextOverlay_RaisesOnChange()
    {
        var store   = CreateStore(o => o.TextOverlays = true);
        var raised  = false;
        store.OnChange += () => raised = true;

        store.AddTextOverlay(MakeOverlay());

        Assert.True(raised);
    }

    [Fact]
    public void UpdateTextOverlay_ChangesText()
    {
        var store   = CreateStore(o => o.TextOverlays = true);
        var overlay = MakeOverlay("Original");
        store.AddTextOverlay(overlay);

        store.UpdateTextOverlay(overlay with { Text = "Updated", Name = "Updated" });

        var result = store.AllTextOverlays.Single(o => o.Id == overlay.Id);
        Assert.Equal("Updated", result.Text);
    }

    [Fact]
    public void UpdateTextOverlay_ChangesPositionAndDuration()
    {
        var store   = CreateStore(o => o.TextOverlays = true);
        var overlay = MakeOverlay(position: 0, duration: 5);
        store.AddTextOverlay(overlay);

        store.UpdateTextOverlay(overlay with { TimelinePosition = 2.5, Duration = 3.0 });

        var result = store.AllTextOverlays.Single(o => o.Id == overlay.Id);
        Assert.Equal(2.5, result.TimelinePosition, precision: 5);
        Assert.Equal(3.0, result.Duration,         precision: 5);
    }

    [Fact]
    public void UpdateTextOverlay_UnknownId_Throws()
    {
        var store = CreateStore(o => o.TextOverlays = true);
        Assert.Throws<ArgumentException>(
            () => store.UpdateTextOverlay(MakeOverlay()));
    }

    [Fact]
    public void UpdateTextOverlay_RaisesOnChange()
    {
        var store   = CreateStore(o => o.TextOverlays = true);
        var overlay = MakeOverlay();
        store.AddTextOverlay(overlay);
        var raised  = false;
        store.OnChange += () => raised = true;

        store.UpdateTextOverlay(overlay with { Text = "Changed" });

        Assert.True(raised);
    }

    [Fact]
    public void RemoveTextOverlay_RemovesFromTrack()
    {
        var store   = CreateStore(o => o.TextOverlays = true);
        var overlay = MakeOverlay();
        store.AddTextOverlay(overlay);

        store.RemoveTextOverlay(overlay.Id);

        Assert.DoesNotContain(store.PrimaryVideoTrack.Items, i => i.Id == overlay.Id);
    }

    [Fact]
    public void RemoveTextOverlay_RaisesOnChange()
    {
        var store   = CreateStore(o => o.TextOverlays = true);
        var overlay = MakeOverlay();
        store.AddTextOverlay(overlay);
        var raised  = false;
        store.OnChange += () => raised = true;

        store.RemoveTextOverlay(overlay.Id);

        Assert.True(raised);
    }

    [Fact]
    public void UpdateTextOverlay_UpdatesAllProperties()
    {
        var store   = CreateStore(o => o.TextOverlays = true);
        var overlay = MakeOverlay();
        store.AddTextOverlay(overlay);

        var updated = overlay with
        {
            Text            = "New",
            FontFamily      = "Georgia",
            FontSize        = 72,
            FontColor       = "#FF0000",
            BoxColor        = "#000000@0.50",
            HorizontalAlign = TextHorizontalAlign.Left,
            VerticalAlign   = TextVerticalAlign.Top,
            OffsetX         = 10,
            OffsetY         = 20,
            FadeInSeconds   = 0.5,
            FadeOutSeconds  = 0.5,
            FontBold        = true,
            FontUnderline   = true,
            Runs            = [new TextRun { Text = "New", Bold = true }],
        };
        store.UpdateTextOverlay(updated);

        var result = store.AllTextOverlays.Single(o => o.Id == overlay.Id);
        Assert.Equal("New",       result.Text);
        Assert.Equal("Georgia",   result.FontFamily);
        Assert.Equal(72,          result.FontSize);
        Assert.Equal("#FF0000",   result.FontColor);
        Assert.Equal("#000000@0.50", result.BoxColor);
        Assert.Equal(TextHorizontalAlign.Left, result.HorizontalAlign);
        Assert.Equal(TextVerticalAlign.Top,    result.VerticalAlign);
        Assert.Equal(10,  result.OffsetX);
        Assert.Equal(20,  result.OffsetY);
        Assert.Equal(0.5, result.FadeInSeconds,  precision: 5);
        Assert.Equal(0.5, result.FadeOutSeconds, precision: 5);
        Assert.True(result.FontBold);
        Assert.True(result.FontUnderline);
        // item #16, phase 115 — same whitelist-regression class phase 111 found: a field left out
        // of UpdateTextOverlay's explicit copy silently never saves.
        Assert.NotNull(result.Runs);
        Assert.Single(result.Runs!);
        Assert.Equal("New", result.Runs![0].Text);
        Assert.True(result.Runs[0].Bold);
    }

    // ── UpdateClipSpeed ───────────────────────────────────────────────────────────

    private static VideoClip MakeClipWithDuration(ClipStore store, double duration)
    {
        var clip = new VideoClip { Name = "v", Duration = duration, MemFsName = "v.mp4" };
        store.PrimaryVideoTrack.Items.Add(clip);
        return clip;
    }

    [Fact]
    public void UpdateClipSpeed_SetsSpeed()
    {
        var store = CreateStore();
        var clip  = MakeClipWithDuration(store, 10.0);

        store.UpdateClipSpeed(clip.Id, 2.0);

        Assert.Equal(2.0, clip.Speed);
    }

    [Fact]
    public void UpdateClipSpeed_ClampsAboveMax()
    {
        var store = CreateStore();
        var clip  = MakeClipWithDuration(store, 10.0);

        store.UpdateClipSpeed(clip.Id, 99.0);

        Assert.Equal(4.0, clip.Speed);
    }

    [Fact]
    public void UpdateClipSpeed_ClampsBelowMin()
    {
        var store = CreateStore();
        var clip  = MakeClipWithDuration(store, 10.0);

        store.UpdateClipSpeed(clip.Id, 0.01);

        Assert.Equal(0.25, clip.Speed);
    }

    [Fact]
    public void UpdateClipSpeed_ZeroOrNegative_DoesNothing()
    {
        var store = CreateStore();
        var clip  = MakeClipWithDuration(store, 10.0);

        store.UpdateClipSpeed(clip.Id, -1.0);

        Assert.Equal(1.0, clip.Speed); // default unchanged
    }

    [Fact]
    public void UpdateClipSpeed_RaisesOnChange()
    {
        var store   = CreateStore();
        var clip    = MakeClipWithDuration(store, 10.0);
        var changed = false;
        store.OnChange += () => changed = true;

        store.UpdateClipSpeed(clip.Id, 0.5);

        Assert.True(changed);
    }

    [Fact]
    public void UpdateClipSpeed_UnknownId_DoesNothing()
    {
        var store   = CreateStore();
        var changed = false;
        store.OnChange += () => changed = true;

        store.UpdateClipSpeed(Guid.NewGuid(), 2.0); // no-op

        Assert.False(changed);
    }

    // ── SetClipVolume ─────────────────────────────────────────────────────────

    [Fact]
    public void SetClipVolume_SetsVolume()
    {
        var store = CreateStore();
        var clip  = MakeClipWithDuration(store, 10.0);
        store.SetClipVolume(clip.Id, 0.5);
        Assert.Equal(0.5, clip.Volume, precision: 9);
    }

    [Fact]
    public void SetClipVolume_ClampsAboveMax()
    {
        var store = CreateStore();
        var clip  = MakeClipWithDuration(store, 10.0);
        store.SetClipVolume(clip.Id, 5.0);
        Assert.Equal(2.0, clip.Volume, precision: 9);
    }

    [Fact]
    public void SetClipVolume_ClampsBelowMin()
    {
        var store = CreateStore();
        var clip  = MakeClipWithDuration(store, 10.0);
        store.SetClipVolume(clip.Id, -1.0);
        Assert.Equal(0.0, clip.Volume, precision: 9);
    }

    [Fact]
    public void SetClipVolume_NotifiesOnChange()
    {
        var store   = CreateStore();
        var clip    = MakeClipWithDuration(store, 10.0);
        var changed = false;
        store.OnChange += () => changed = true;
        store.SetClipVolume(clip.Id, 1.5);
        Assert.True(changed);
    }

    [Fact]
    public void SetClipVolume_UnknownId_IsNoOp()
    {
        var store   = CreateStore();
        var changed = false;
        store.OnChange += () => changed = true;
        store.SetClipVolume(Guid.NewGuid(), 1.0);
        Assert.False(changed);
    }

    // ── AddVolumeKeyframe ─────────────────────────────────────────────────────

    [Fact]
    public void AddVolumeKeyframe_AddsAndSortsByPosition()
    {
        var store = CreateStore();
        var clip  = MakeClipWithDuration(store, 10.0);
        store.AddVolumeKeyframe(clip.Id, 0.8, 1.0);
        store.AddVolumeKeyframe(clip.Id, 0.2, 0.5);
        Assert.Equal(2, clip.VolumeAutomation.Count);
        Assert.Equal(0.2, clip.VolumeAutomation[0].Position, precision: 9);
        Assert.Equal(0.8, clip.VolumeAutomation[1].Position, precision: 9);
    }

    [Fact]
    public void AddVolumeKeyframe_ReplacesAtSamePosition()
    {
        var store = CreateStore();
        var clip  = MakeClipWithDuration(store, 10.0);
        store.AddVolumeKeyframe(clip.Id, 0.5, 1.0);
        store.AddVolumeKeyframe(clip.Id, 0.5, 0.25);
        Assert.Single(clip.VolumeAutomation);
        Assert.Equal(0.25, clip.VolumeAutomation[0].Volume, precision: 9);
    }

    [Fact]
    public void AddVolumeKeyframe_ClampsValues()
    {
        var store = CreateStore();
        var clip  = MakeClipWithDuration(store, 10.0);
        store.AddVolumeKeyframe(clip.Id, -1.0, 99.0);
        Assert.Equal(0.0, clip.VolumeAutomation[0].Position, precision: 9);
        Assert.Equal(2.0, clip.VolumeAutomation[0].Volume, precision: 9);
    }

    [Fact]
    public void AddVolumeKeyframe_NotifiesOnChange()
    {
        var store   = CreateStore();
        var clip    = MakeClipWithDuration(store, 10.0);
        var changed = false;
        store.OnChange += () => changed = true;
        store.AddVolumeKeyframe(clip.Id, 0.5, 1.0);
        Assert.True(changed);
    }

    // ── UpdateVolumeKeyframe ──────────────────────────────────────────────────

    [Fact]
    public void UpdateVolumeKeyframe_UpdatesAndResorts()
    {
        var store = CreateStore();
        var clip  = MakeClipWithDuration(store, 10.0);
        store.AddVolumeKeyframe(clip.Id, 0.3, 1.0);
        store.AddVolumeKeyframe(clip.Id, 0.7, 0.5);
        var kfId = clip.VolumeAutomation[1].Id; // originally at 0.7
        store.UpdateVolumeKeyframe(clip.Id, kfId, 0.1, 1.5);
        Assert.Equal(0.1, clip.VolumeAutomation[0].Position, precision: 9);
        Assert.Equal(1.5, clip.VolumeAutomation[0].Volume, precision: 9);
    }

    [Fact]
    public void UpdateVolumeKeyframe_UnknownKeyframeId_IsNoOp()
    {
        var store = CreateStore();
        var clip  = MakeClipWithDuration(store, 10.0);
        store.AddVolumeKeyframe(clip.Id, 0.5, 1.0);
        var before = clip.VolumeAutomation[0].Volume;
        store.UpdateVolumeKeyframe(clip.Id, Guid.NewGuid(), 0.5, 0.1);
        Assert.Equal(before, clip.VolumeAutomation[0].Volume, precision: 9);
    }

    // ── RemoveVolumeKeyframe ──────────────────────────────────────────────────

    [Fact]
    public void RemoveVolumeKeyframe_RemovesById()
    {
        var store = CreateStore();
        var clip  = MakeClipWithDuration(store, 10.0);
        store.AddVolumeKeyframe(clip.Id, 0.5, 1.0);
        var kfId = clip.VolumeAutomation[0].Id;
        store.RemoveVolumeKeyframe(clip.Id, kfId);
        Assert.Empty(clip.VolumeAutomation);
    }

    [Fact]
    public void RemoveVolumeKeyframe_NotifiesOnChange()
    {
        var store   = CreateStore();
        var clip    = MakeClipWithDuration(store, 10.0);
        store.AddVolumeKeyframe(clip.Id, 0.5, 1.0);
        var kfId    = clip.VolumeAutomation[0].Id;
        var changed = false;
        store.OnChange += () => changed = true;
        store.RemoveVolumeKeyframe(clip.Id, kfId);
        Assert.True(changed);
    }

    // ── ClearVolumeAutomation ─────────────────────────────────────────────────

    [Fact]
    public void ClearVolumeAutomation_RemovesAllKeyframes()
    {
        var store = CreateStore();
        var clip  = MakeClipWithDuration(store, 10.0);
        store.AddVolumeKeyframe(clip.Id, 0.2, 0.8);
        store.AddVolumeKeyframe(clip.Id, 0.8, 1.2);
        store.ClearVolumeAutomation(clip.Id);
        Assert.Empty(clip.VolumeAutomation);
    }

    [Fact]
    public void ClearVolumeAutomation_EmptyList_IsNoOp()
    {
        var store   = CreateStore();
        var clip    = MakeClipWithDuration(store, 10.0);
        var changed = false;
        store.OnChange += () => changed = true;
        store.ClearVolumeAutomation(clip.Id);
        Assert.False(changed);
    }

    // ── Marker tests ──────────────────────────────────────────────────────────

    [Fact]
    public void AddMarker_ReturnsMarkerAndRaisesOnChange()
    {
        var store   = CreateStore(o => o.Markers = true);
        var changed = false;
        store.OnChange += () => changed = true;

        var marker = store.AddMarker(5.0, "Intro");

        Assert.NotNull(marker);
        Assert.Equal("Intro", marker.Label);
        Assert.Equal(5.0, marker.TimeSeconds);
        Assert.Single(store.Markers);
        Assert.True(changed);
    }

    [Fact]
    public void AddMarker_UsesDefaultTimecodeLabel_WhenLabelIsNull()
    {
        var store  = CreateStore(o => o.Markers = true);
        var marker = store.AddMarker(65.0); // 1:05.0

        Assert.NotNull(marker);
        Assert.Equal("1:05.0", marker.Label);
    }

    [Fact]
    public void AddMarker_ClampsNegativeTime_ToZero()
    {
        var store  = CreateStore(o => o.Markers = true);
        var marker = store.AddMarker(-3.0);

        Assert.NotNull(marker);
        Assert.Equal(0.0, marker.TimeSeconds);
    }

    [Fact]
    public void AddMarker_AssignsDistinctCyclingColors()
    {
        var store = CreateStore(o => o.Markers = true);

        store.AddMarker(1.0);
        store.AddMarker(2.0);

        var colors = store.Markers.Select(m => m.Color).ToList();
        Assert.Equal(2, colors.Distinct().Count());
    }

    [Fact]
    public void AddMarker_WhenMarkersDisabled_ReturnsNull()
    {
        var store   = CreateStore(o => o.Markers = false);
        var changed = false;
        store.OnChange += () => changed = true;

        var marker = store.AddMarker(5.0);

        Assert.Null(marker);
        Assert.Empty(store.Markers);
        Assert.False(changed);
    }

    [Fact]
    public void Markers_AreSortedByTime()
    {
        var store = CreateStore(o => o.Markers = true);
        store.AddMarker(10.0, "B");
        store.AddMarker(2.0,  "A");
        store.AddMarker(5.0,  "C");

        var labels = store.Markers.Select(m => m.Label).ToList();
        Assert.Equal(["A", "C", "B"], labels);
    }

    [Fact]
    public void UpdateMarker_ChangesLabelAndTime_AndRaisesOnChange()
    {
        var store   = CreateStore(o => o.Markers = true);
        var marker  = store.AddMarker(3.0, "Old");
        Assert.NotNull(marker);

        var changed = false;
        store.OnChange += () => changed = true;

        store.UpdateMarker(marker.Id, "New", 7.5);

        Assert.Equal("New", marker.Label);
        Assert.Equal(7.5,   marker.TimeSeconds);
        Assert.True(changed);
    }

    [Fact]
    public void UpdateMarker_UnknownId_IsNoOp()
    {
        var store   = CreateStore(o => o.Markers = true);
        var changed = false;
        store.OnChange += () => changed = true;

        store.UpdateMarker(Guid.NewGuid(), "X", 1.0);

        Assert.Empty(store.Markers);
        Assert.False(changed);
    }

    [Fact]
    public void UpdateMarker_PushesUndoCommand_AndRestoresOnUndo()
    {
        var store  = CreateStore(o => o.Markers = true);
        var marker = store.AddMarker(3.0, "Old");
        Assert.NotNull(marker);

        store.UpdateMarker(marker.Id, "New", 7.5);
        Assert.True(store.CanUndo);
        Assert.Equal("Update marker", store.UndoDescription);

        store.Undo();

        Assert.Equal("Old", marker.Label);
        Assert.Equal(3.0,   marker.TimeSeconds);
    }

    [Fact]
    public void UpdateMarker_Redo_ReappliesLabelAndTime()
    {
        var store  = CreateStore(o => o.Markers = true);
        var marker = store.AddMarker(3.0, "Old");
        Assert.NotNull(marker);

        store.UpdateMarker(marker.Id, "New", 7.5);
        store.Undo();
        store.Redo();

        Assert.Equal("New", marker.Label);
        Assert.Equal(7.5,   marker.TimeSeconds);
    }

    [Fact]
    public void UpdateMarker_NoOp_WhenValuesUnchanged()
    {
        var store  = CreateStore(o => o.Markers = true);
        var marker = store.AddMarker(3.0, "Old");
        Assert.NotNull(marker);

        var descriptionBeforeUpdate = store.UndoDescription; // "Add marker \"Old\""
        store.UpdateMarker(marker.Id, "Old", 3.0);

        Assert.Equal(descriptionBeforeUpdate, store.UndoDescription); // no new command pushed
    }

    [Fact]
    public void UpdateMarker_BlankLabel_KeepsExistingLabel()
    {
        var store  = CreateStore(o => o.Markers = true);
        var marker = store.AddMarker(3.0, "Old");
        Assert.NotNull(marker);

        store.UpdateMarker(marker.Id, "   ", 9.0);

        Assert.Equal("Old", marker.Label);
        Assert.Equal(9.0,   marker.TimeSeconds);
    }

    [Fact]
    public void CommitMarkerPosition_PushesUndoCommand_WhenTimeChanged()
    {
        var store  = CreateStore(o => o.Markers = true);
        var marker = store.AddMarker(3.0, "Cue");
        Assert.NotNull(marker);

        marker.TimeSeconds = 8.0; // simulates the live drag mutation VideoTimeline applies on pointermove
        store.CommitMarkerPosition(marker.Id, originalTime: 3.0);

        Assert.Equal(8.0, marker.TimeSeconds);
        Assert.True(store.CanUndo);

        store.Undo();
        Assert.Equal(3.0, marker.TimeSeconds);
    }

    [Fact]
    public void CommitMarkerPosition_ClampsNegativeTime_ToZero()
    {
        var store  = CreateStore(o => o.Markers = true);
        var marker = store.AddMarker(3.0, "Cue");
        Assert.NotNull(marker);

        marker.TimeSeconds = -5.0; // dragged past the start of the timeline
        store.CommitMarkerPosition(marker.Id, originalTime: 3.0);

        Assert.Equal(0.0, marker.TimeSeconds);
    }

    [Fact]
    public void CommitMarkerPosition_NoOp_WhenTimeUnchanged()
    {
        var store  = CreateStore(o => o.Markers = true);
        var marker = store.AddMarker(3.0, "Cue");
        Assert.NotNull(marker);

        var descriptionBeforeCommit = store.UndoDescription;
        store.CommitMarkerPosition(marker.Id, originalTime: 3.0); // no drag actually happened

        Assert.Equal(descriptionBeforeCommit, store.UndoDescription); // no new command pushed
    }

    [Fact]
    public void CommitMarkerPosition_UnknownId_IsNoOp()
    {
        var store   = CreateStore(o => o.Markers = true);
        var changed = false;
        store.OnChange += () => changed = true;

        store.CommitMarkerPosition(Guid.NewGuid(), originalTime: 0.0);

        Assert.False(changed);
    }

    [Fact]
    public void RemoveMarker_DeletesMarkerAndRaisesOnChange()
    {
        var store  = CreateStore(o => o.Markers = true);
        var marker = store.AddMarker(5.0, "M1");
        Assert.NotNull(marker);

        var changed = false;
        store.OnChange += () => changed = true;

        store.RemoveMarker(marker.Id);

        Assert.Empty(store.Markers);
        Assert.True(changed);
    }

    [Fact]
    public void RemoveMarker_UnknownId_IsNoOp()
    {
        var store   = CreateStore(o => o.Markers = true);
        var changed = false;
        store.OnChange += () => changed = true;

        store.RemoveMarker(Guid.NewGuid());

        Assert.False(changed);
    }

    [Fact]
    public void VideoEditorOptions_Markers_DefaultIsTrue()
    {
        var options = new VideoEditorOptions();
        Assert.True(options.Markers);
    }

    // ── Multi-step Undo / Redo tests ──────────────────────────────────────────

    [Fact]
    public void Undo_AfterAddClip_RemovesClip()
    {
        var store = CreateStore();
        var clip  = new VideoClip { Name = "a.mp4", Duration = 5 };
        store.AddClip(clip);

        Assert.True(store.CanUndo);
        store.Undo();

        Assert.DoesNotContain(store.PrimaryVideoTrack.Items, i => i.Id == clip.Id);
        Assert.False(store.CanUndo);
    }

    [Fact]
    public void Redo_AfterUndoAddClip_RestoresClip()
    {
        var store = CreateStore();
        var clip  = new VideoClip { Name = "a.mp4", Duration = 5 };
        store.AddClip(clip);
        store.Undo();

        Assert.True(store.CanRedo);
        store.Redo();

        Assert.Contains(store.PrimaryVideoTrack.Items, i => i.Id == clip.Id);
        Assert.False(store.CanRedo);
    }

    [Fact]
    public void Undo_AfterRemoveClip_RestoresClip()
    {
        var store = CreateStore();
        var clip  = new VideoClip { Name = "b.mp4", Duration = 3 };
        store.AddClip(clip);
        store.Undo(); // undo the add so redo stack is clear
        store.Redo(); // redo the add — clip is back
        store.RemoveClip(clip.Id);

        store.Undo();

        Assert.Contains(store.PrimaryVideoTrack.Items, i => i.Id == clip.Id);
    }

    [Fact]
    public void MultiStep_Undo_Redo_RoundTrips()
    {
        var store = CreateStore();
        var c1 = new VideoClip { Name = "1.mp4", Duration = 2 };
        var c2 = new VideoClip { Name = "2.mp4", Duration = 2 };
        var c3 = new VideoClip { Name = "3.mp4", Duration = 2 };

        store.AddClip(c1);
        store.AddClip(c2);
        store.AddClip(c3);

        // Undo 3 times — all clips removed
        store.Undo(); store.Undo(); store.Undo();
        Assert.Empty(store.PrimaryVideoTrack.Items);

        // Redo 3 times — all clips restored
        store.Redo(); store.Redo(); store.Redo();
        Assert.Equal(3, store.PrimaryVideoTrack.Items.Count);
    }

    [Fact]
    public void NewMutation_ClearsRedoStack()
    {
        var store = CreateStore();
        var c1 = new VideoClip { Name = "x.mp4", Duration = 2 };
        var c2 = new VideoClip { Name = "y.mp4", Duration = 2 };
        store.AddClip(c1);
        store.Undo();           // c1 removed; redo available
        store.AddClip(c2);      // new mutation — should clear redo

        Assert.False(store.CanRedo);
    }

    [Fact]
    public void Undo_Trim_RestoresOriginalTrimPoints()
    {
        var store = CreateStore();
        var clip  = new VideoClip { Name = "t.mp4", Duration = 10, EndTrim = 10 };
        store.AddClip(clip);

        store.UpdateTrim(clip.Id, 2.0, 8.0);
        Assert.Equal(2.0, clip.StartTrim);

        store.Undo(); // undo the trim change
        Assert.Equal(0.0, clip.StartTrim);
        Assert.Equal(10.0, clip.EndTrim);
    }

    [Fact]
    public void Undo_Speed_RestoresOriginalSpeed()
    {
        var store = CreateStore();
        var clip  = new VideoClip { Name = "s.mp4", Duration = 5, EndTrim = 5 };
        store.AddClip(clip);
        store.UpdateClipSpeed(clip.Id, 2.0);

        store.Undo(); // undo speed change
        Assert.Equal(1.0, clip.Speed);
    }

    [Fact]
    public void Undo_Volume_RestoresOriginalVolume()
    {
        var store = CreateStore();
        var clip  = new VideoClip { Name = "v.mp4", Duration = 5, EndTrim = 5 };
        store.AddClip(clip);
        store.SetClipVolume(clip.Id, 0.5);

        store.Undo(); // undo volume change
        Assert.Equal(1.0, clip.Volume, precision: 9);
    }

    [Fact]
    public void Undo_AddMarker_RemovesMarker()
    {
        var store  = CreateStore(o => o.Markers = true);
        var marker = store.AddMarker(5.0, "Beat");
        Assert.NotNull(marker);

        store.Undo();
        Assert.Empty(store.Markers);
    }

    [Fact]
    public void Redo_AddMarker_RestoresMarker()
    {
        var store  = CreateStore(o => o.Markers = true);
        var marker = store.AddMarker(5.0, "Beat");
        Assert.NotNull(marker);
        store.Undo();
        store.Redo();

        Assert.Single(store.Markers);
        Assert.Equal("Beat", store.Markers[0].Label);
    }

    [Fact]
    public void HistoryDepth_Overflow_DropOldestEntries()
    {
        var store = CreateStore();

        // Push 60 add-clip commands (max depth = 50)
        for (var i = 0; i < 60; i++)
        {
            var clip = new VideoClip { Name = $"{i}.mp4", Duration = 1 };
            store.AddClip(clip);
        }

        // Should be able to undo at most 50 times without throwing
        var undoCount = 0;
        while (store.CanUndo && undoCount <= 55)
        {
            store.Undo();
            undoCount++;
        }

        Assert.Equal(50, undoCount);
    }

    [Fact]
    public void UndoDescription_ReflectsTopOfStack()
    {
        var store = CreateStore();
        var clip  = new VideoClip { Name = "desc.mp4", Duration = 2 };
        store.AddClip(clip);

        Assert.NotNull(store.UndoDescription);
        Assert.Contains("desc.mp4", store.UndoDescription);
    }

    [Fact]
    public void CanUndo_IsFalse_Initially()
    {
        var store = CreateStore();
        Assert.False(store.CanUndo);
        Assert.False(store.CanRedo);
    }

    [Fact]
    public void UndoLastRemove_Shim_StillWorks()
    {
        var store = CreateStore();
        var clip  = new VideoClip { Name = "shim.mp4", Duration = 5 };
        store.AddClip(clip);
        store.RemoveClip(clip.Id);

        store.UndoLastRemove(); // shim delegates to Undo()

        Assert.Contains(store.PrimaryVideoTrack.Items, i => i.Id == clip.Id);
    }

    // ── UpdateClipEffects ────────────────────────────────────────────────────

    [Fact]
    public void UpdateClipEffects_SetsBrightnessOnClip()
    {
        var store = CreateStore();
        var clip  = new VideoClip { Name = "fx.mp4", Duration = 5 };
        store.AddClip(clip);

        store.UpdateClipEffects(clip.Id, new ClipEffects { Brightness = 0.5 });

        Assert.Equal(0.5, clip.Effects.Brightness, precision: 4);
    }

    [Fact]
    public void UpdateClipEffects_PushesUndoCommand()
    {
        var store = CreateStore();
        var clip  = new VideoClip { Name = "fx.mp4", Duration = 5 };
        store.AddClip(clip);
        var before = store.CanUndo;

        store.UpdateClipEffects(clip.Id, new ClipEffects { Contrast = 1.8 });

        Assert.True(store.CanUndo);
        Assert.Contains("effects", store.UndoDescription, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UpdateClipEffects_Undo_RestoresOriginalEffects()
    {
        var store = CreateStore();
        var clip  = new VideoClip { Name = "fx.mp4", Duration = 5 };
        store.AddClip(clip);

        store.UpdateClipEffects(clip.Id, new ClipEffects { Saturation = 0.2 });
        Assert.Equal(0.2, clip.Effects.Saturation, precision: 4);

        store.Undo();
        Assert.Equal(1.0, clip.Effects.Saturation, precision: 4); // back to neutral
    }

    [Fact]
    public void UpdateClipEffects_Redo_ReappliesEffects()
    {
        var store = CreateStore();
        var clip  = new VideoClip { Name = "fx.mp4", Duration = 5 };
        store.AddClip(clip);

        store.UpdateClipEffects(clip.Id, new ClipEffects { FadeInSeconds = 1.5 });
        store.Undo();
        store.Redo();

        Assert.Equal(1.5, clip.Effects.FadeInSeconds, precision: 4);
    }

    [Fact]
    public void UpdateClipEffects_UnknownId_DoesNothing()
    {
        var store = CreateStore();
        var countBefore = store.CanUndo;

        store.UpdateClipEffects(Guid.NewGuid(), new ClipEffects { Brightness = 0.9 });

        // undo stack unchanged (no command pushed for unknown clip)
        Assert.False(store.CanUndo);
    }

    // ── ApplyFadeToBlackAt ────────────────────────────────────────────────────

    [Fact]
    public void ApplyFadeToBlackAt_SetsFadeOutSeconds_ToDistanceFromClipEnd()
    {
        var store = CreateStore();
        var clip  = new VideoClip { Name = "fx.mp4", TimelinePosition = 0, Duration = 10 };
        store.AddClip(clip);

        store.ApplyFadeToBlackAt(7.0); // clip ends at 10 -> fade should be 3s

        Assert.Equal(3.0, clip.Effects.FadeOutSeconds, precision: 4);
    }

    [Fact]
    public void ApplyFadeToBlackAt_ClampsToClipDuration_WhenMarkerAtClipStart()
    {
        var store = CreateStore();
        var clip  = new VideoClip { Name = "fx.mp4", TimelinePosition = 5, Duration = 4 };
        store.AddClip(clip);

        store.ApplyFadeToBlackAt(5.0); // exactly at the clip's start -> whole clip fades

        Assert.Equal(clip.EffectiveDuration, clip.Effects.FadeOutSeconds, precision: 4);
    }

    [Fact]
    public void ApplyFadeToBlackAt_NoOp_WhenNoClipCoversPosition()
    {
        var store = CreateStore();
        var clip  = new VideoClip { Name = "fx.mp4", TimelinePosition = 0, Duration = 4 };
        store.AddClip(clip);
        var undoStateBefore = store.CanUndo;

        store.ApplyFadeToBlackAt(10.0); // past the end of the only clip

        Assert.Equal(0.0, clip.Effects.FadeOutSeconds, precision: 4);
        Assert.Equal(undoStateBefore, store.CanUndo); // no new command pushed
    }

    [Fact]
    public void ApplyFadeToBlackAt_SupportsUndo()
    {
        var store = CreateStore();
        var clip  = new VideoClip { Name = "fx.mp4", TimelinePosition = 0, Duration = 10 };
        store.AddClip(clip);

        store.ApplyFadeToBlackAt(6.0);
        Assert.Equal(4.0, clip.Effects.FadeOutSeconds, precision: 4);

        store.Undo();

        Assert.Equal(0.0, clip.Effects.FadeOutSeconds, precision: 4);
    }

    [Fact]
    public void ApplyFadeToBlackAt_FindsClipOnAnyVideoTrack_NotJustPrimary()
    {
        var store  = CreateStore(o => o.MultiTrack = true);
        var track2 = store.AddVideoTrack();
        var clip   = new VideoClip { Name = "fx.mp4", TimelinePosition = 0, Duration = 8 };
        store.AddClipToTrack(track2.Id, clip);

        store.ApplyFadeToBlackAt(5.0);

        Assert.Equal(3.0, clip.Effects.FadeOutSeconds, precision: 4);
    }

    [Fact]
    public void ClipEffects_IsNeutral_TrueForDefaults()
    {
        Assert.True(new ClipEffects().IsNeutral);
    }

    [Fact]
    public void ClipEffects_IsNeutral_FalseWhenBrightnessSet()
    {
        Assert.False(new ClipEffects { Brightness = 0.1 }.IsNeutral);
    }

    [Fact]
    public void ClipEffects_IsNeutral_FalseWhenFadeInSet()
    {
        Assert.False(new ClipEffects { FadeInSeconds = 0.5 }.IsNeutral);
    }

    // ── UpdateAudioTrim ──────────────────────────────────────────────────────

    [Fact]
    public void UpdateAudioTrim_SetsTrimPoints()
    {
        var store = CreateStore(o => o.AudioTracks = true);
        var clip  = new AudioClip { Name = "a.mp3", Duration = 10 };
        store.AddClipToTrack(store.AudioTracks.First().Id, clip);

        store.UpdateAudioTrim(clip.Id, 1.0, 8.0);

        var updated = store.AudioTracks.First().AudioClips.First();
        Assert.Equal(1.0, updated.StartTrim);
        Assert.Equal(8.0, updated.EndTrim);
    }

    [Fact]
    public void UpdateAudioTrim_ClampsToDuration()
    {
        var store = CreateStore(o => o.AudioTracks = true);
        var clip  = new AudioClip { Name = "a.mp3", Duration = 5 };
        store.AddClipToTrack(store.AudioTracks.First().Id, clip);

        store.UpdateAudioTrim(clip.Id, -1.0, 999.0);

        var updated = store.AudioTracks.First().AudioClips.First();
        Assert.Equal(0.0, updated.StartTrim);
        Assert.Equal(5.0, updated.EndTrim);
    }

    [Fact]
    public void UpdateAudioTrim_IgnoresInvalidRange()
    {
        var store = CreateStore(o => o.AudioTracks = true);
        var clip  = new AudioClip { Name = "a.mp3", Duration = 10, StartTrim = 2, EndTrim = 8 };
        store.AddClipToTrack(store.AudioTracks.First().Id, clip);

        store.UpdateAudioTrim(clip.Id, 6.0, 3.0); // start > end — should be ignored

        var updated = store.AudioTracks.First().AudioClips.First();
        Assert.Equal(2.0, updated.StartTrim); // unchanged
        Assert.Equal(8.0, updated.EndTrim);   // unchanged
    }

    [Fact]
    public void UpdateAudioTrim_IsUndoable()
    {
        var store = CreateStore(o => o.AudioTracks = true);
        var clip  = new AudioClip { Name = "a.mp3", Duration = 10 };
        store.AddClipToTrack(store.AudioTracks.First().Id, clip);

        store.UpdateAudioTrim(clip.Id, 1.0, 9.0);
        store.Undo();

        var reverted = store.AudioTracks.First().AudioClips.First();
        Assert.Equal(0.0, reverted.StartTrim);
        Assert.Equal(0.0, reverted.EndTrim);
    }

    [Fact]
    public void UpdateAudioTrim_IsRedoable()
    {
        var store = CreateStore(o => o.AudioTracks = true);
        var clip  = new AudioClip { Name = "a.mp3", Duration = 10 };
        store.AddClipToTrack(store.AudioTracks.First().Id, clip);

        store.UpdateAudioTrim(clip.Id, 1.0, 9.0);
        store.Undo();
        store.Redo();

        var redone = store.AudioTracks.First().AudioClips.First();
        Assert.Equal(1.0, redone.StartTrim);
        Assert.Equal(9.0, redone.EndTrim);
    }

    // ── UpdateAudioFade ──────────────────────────────────────────────────────

    [Fact]
    public void UpdateAudioFade_SetsFadeValues()
    {
        var store = CreateStore(o => o.AudioTracks = true);
        var clip  = new AudioClip { Name = "a.mp3", Duration = 10 };
        store.AddClipToTrack(store.AudioTracks.First().Id, clip);

        store.UpdateAudioFade(clip.Id, 1.5, 2.0);

        var updated = store.AudioTracks.First().AudioClips.First();
        Assert.Equal(1.5, updated.FadeInSeconds);
        Assert.Equal(2.0, updated.FadeOutSeconds);
    }

    [Fact]
    public void UpdateAudioFade_ClampsToHalfDuration()
    {
        var store = CreateStore(o => o.AudioTracks = true);
        var clip  = new AudioClip { Name = "a.mp3", Duration = 6 };
        store.AddClipToTrack(store.AudioTracks.First().Id, clip);

        store.UpdateAudioFade(clip.Id, 99.0, 99.0);

        var updated = store.AudioTracks.First().AudioClips.First();
        Assert.Equal(3.0, updated.FadeInSeconds);  // clamped to Duration/2
        Assert.Equal(3.0, updated.FadeOutSeconds);
    }

    [Fact]
    public void UpdateAudioFade_IsUndoable()
    {
        var store = CreateStore(o => o.AudioTracks = true);
        var clip  = new AudioClip { Name = "a.mp3", Duration = 10 };
        store.AddClipToTrack(store.AudioTracks.First().Id, clip);

        store.UpdateAudioFade(clip.Id, 1.0, 2.0);
        store.Undo();

        var reverted = store.AudioTracks.First().AudioClips.First();
        Assert.Equal(0.0, reverted.FadeInSeconds);
        Assert.Equal(0.0, reverted.FadeOutSeconds);
    }

    [Fact]
    public void UpdateAudioFade_IsRedoable()
    {
        var store = CreateStore(o => o.AudioTracks = true);
        var clip  = new AudioClip { Name = "a.mp3", Duration = 10 };
        store.AddClipToTrack(store.AudioTracks.First().Id, clip);

        store.UpdateAudioFade(clip.Id, 1.0, 2.0);
        store.Undo();
        store.Redo();

        var redone = store.AudioTracks.First().AudioClips.First();
        Assert.Equal(1.0, redone.FadeInSeconds);
        Assert.Equal(2.0, redone.FadeOutSeconds);
    }

    // ── SetClipChannelVolume (backlog #10) ──────────────────────────────────────

    [Fact]
    public void SetClipChannelVolume_SetsBothChannels()
    {
        var store = CreateStore(o => o.AudioTracks = true);
        var clip  = new AudioClip { Name = "a.mp3", Duration = 10 };
        store.AddClipToTrack(store.AudioTracks.First().Id, clip);

        store.SetClipChannelVolume(clip.Id, 0.5, 1.5);

        var updated = store.AudioTracks.First().AudioClips.First();
        Assert.Equal(0.5, updated.LeftVolume,  precision: 9);
        Assert.Equal(1.5, updated.RightVolume, precision: 9);
    }

    [Fact]
    public void SetClipChannelVolume_ClampsToRange()
    {
        var store = CreateStore(o => o.AudioTracks = true);
        var clip  = new AudioClip { Name = "a.mp3", Duration = 10 };
        store.AddClipToTrack(store.AudioTracks.First().Id, clip);

        store.SetClipChannelVolume(clip.Id, -1.0, 5.0);

        var updated = store.AudioTracks.First().AudioClips.First();
        Assert.Equal(0.0, updated.LeftVolume,  precision: 9);
        Assert.Equal(2.0, updated.RightVolume, precision: 9);
    }

    [Fact]
    public void SetClipChannelVolume_IsUndoable()
    {
        var store = CreateStore(o => o.AudioTracks = true);
        var clip  = new AudioClip { Name = "a.mp3", Duration = 10 };
        store.AddClipToTrack(store.AudioTracks.First().Id, clip);

        store.SetClipChannelVolume(clip.Id, 0.3, 0.4);
        store.Undo();

        var reverted = store.AudioTracks.First().AudioClips.First();
        Assert.Equal(1.0, reverted.LeftVolume,  precision: 9);
        Assert.Equal(1.0, reverted.RightVolume, precision: 9);
    }

    [Fact]
    public void SetClipChannelVolume_IsRedoable()
    {
        var store = CreateStore(o => o.AudioTracks = true);
        var clip  = new AudioClip { Name = "a.mp3", Duration = 10 };
        store.AddClipToTrack(store.AudioTracks.First().Id, clip);

        store.SetClipChannelVolume(clip.Id, 0.3, 0.4);
        store.Undo();
        store.Redo();

        var redone = store.AudioTracks.First().AudioClips.First();
        Assert.Equal(0.3, redone.LeftVolume,  precision: 9);
        Assert.Equal(0.4, redone.RightVolume, precision: 9);
    }

    [Fact]
    public void SetClipChannelVolume_UnknownId_IsNoOp()
    {
        var store   = CreateStore(o => o.AudioTracks = true);
        var changed = false;
        store.OnChange += () => changed = true;
        store.SetClipChannelVolume(Guid.NewGuid(), 0.5, 0.5);
        Assert.False(changed);
    }

    [Fact]
    public void SetClipChannelVolume_NonAudioClip_IsNoOp()
    {
        // Only AudioClip supports channel balance — a VideoClip with the same Id should not match.
        var store = CreateStore();
        var clip  = new VideoClip { Name = "v.mp4", Duration = 10, MemFsName = "v.mp4" };
        store.PrimaryVideoTrack.Items.Add(clip);
        var changed = false;
        store.OnChange += () => changed = true;

        store.SetClipChannelVolume(clip.Id, 0.5, 0.5);

        Assert.False(changed);
    }

    // ── RelinkClip ───────────────────────────────────────────────────────────

    [Fact]
    public void RelinkClip_ClearsIsMediaMissingAndSetsMemFs_VideoClip()
    {
        var store = CreateStore();
        var clip  = new VideoClip { Name = "v.mp4", Duration = 5, IsMediaMissing = true };
        store.AddClip(clip);

        store.RelinkClip(clip.Id, "new_v.mp4");

        var updated = store.PrimaryVideoTrack.VideoClips.First();
        Assert.False(updated.IsMediaMissing);
        Assert.Equal("new_v.mp4", updated.MemFsName);
    }

    [Fact]
    public void RelinkClip_ClearsIsMediaMissingAndSetsMemFs_AudioClip()
    {
        var store = CreateStore(o => o.AudioTracks = true);
        var clip  = new AudioClip { Name = "a.mp3", Duration = 5, IsMediaMissing = true };
        store.AddClipToTrack(store.AudioTracks.First().Id, clip);

        store.RelinkClip(clip.Id, "new_a.mp3");

        var updated = store.AudioTracks.First().AudioClips.First();
        Assert.False(updated.IsMediaMissing);
        Assert.Equal("new_a.mp3", updated.MemFsName);
    }

    [Fact]
    public void RelinkClip_IsUndoable()
    {
        var store = CreateStore();
        var clip  = new VideoClip { Name = "v.mp4", Duration = 5, MemFsName = null, IsMediaMissing = true };
        store.AddClip(clip);

        store.RelinkClip(clip.Id, "new_v.mp4");
        store.Undo();

        var reverted = store.PrimaryVideoTrack.VideoClips.First();
        Assert.Null(reverted.MemFsName);
        Assert.True(reverted.IsMediaMissing);
    }

    [Fact]
    public void RelinkClip_IsRedoable()
    {
        var store = CreateStore();
        var clip  = new VideoClip { Name = "v.mp4", Duration = 5, IsMediaMissing = true };
        store.AddClip(clip);

        store.RelinkClip(clip.Id, "new_v.mp4");
        store.Undo();
        store.Redo();

        var redone = store.PrimaryVideoTrack.VideoClips.First();
        Assert.False(redone.IsMediaMissing);
        Assert.Equal("new_v.mp4", redone.MemFsName);
    }

    [Fact]
    public void RelinkClip_DoesNothingForUnknownId()
    {
        var store = CreateStore();
        store.RelinkClip(Guid.NewGuid(), "ghost.mp4"); // should not throw
        Assert.Empty(store.PrimaryVideoTrack.VideoClips);
    }

    // ── AddVolumeKeyframe undo/redo ──────────────────────────────────────────

    [Fact]
    public void AddVolumeKeyframe_IsUndoable()
    {
        var store = CreateStore();
        var clip  = new VideoClip { Name = "c.mp4", Duration = 10 };
        store.AddClipToTrack(store.PrimaryVideoTrack.Id, clip);

        store.AddVolumeKeyframe(clip.Id, 0.5, 1.2);
        Assert.Single(clip.VolumeAutomation);

        store.Undo();
        Assert.Empty(clip.VolumeAutomation);

        store.Redo();
        Assert.Single(clip.VolumeAutomation);
        Assert.Equal(1.2, clip.VolumeAutomation[0].Volume, 6);
    }

    [Fact]
    public void AddVolumeKeyframe_ReplaceExisting_IsUndoable()
    {
        var store = CreateStore();
        var clip  = new VideoClip { Name = "c.mp4", Duration = 10 };
        store.AddClipToTrack(store.PrimaryVideoTrack.Id, clip);

        store.AddVolumeKeyframe(clip.Id, 0.5, 1.0);
        store.AddVolumeKeyframe(clip.Id, 0.5, 0.5); // replaces at same position

        Assert.Single(clip.VolumeAutomation);
        Assert.Equal(0.5, clip.VolumeAutomation[0].Volume, 6);

        store.Undo(); // undo replace
        Assert.Single(clip.VolumeAutomation);
        Assert.Equal(1.0, clip.VolumeAutomation[0].Volume, 6);
    }

    // ── UpdateVolumeKeyframe undo/redo ───────────────────────────────────────

    [Fact]
    public void UpdateVolumeKeyframe_IsUndoable()
    {
        var store = CreateStore();
        var clip  = new VideoClip { Name = "c.mp4", Duration = 10 };
        store.AddClipToTrack(store.PrimaryVideoTrack.Id, clip);

        store.AddVolumeKeyframe(clip.Id, 0.25, 0.8);
        var kfId = clip.VolumeAutomation[0].Id;

        store.UpdateVolumeKeyframe(clip.Id, kfId, 0.75, 1.5);
        Assert.Equal(0.75, clip.VolumeAutomation[0].Position, 6);
        Assert.Equal(1.5,  clip.VolumeAutomation[0].Volume,   6);

        store.Undo();
        Assert.Equal(0.25, clip.VolumeAutomation[0].Position, 6);
        Assert.Equal(0.8,  clip.VolumeAutomation[0].Volume,   6);

        store.Redo();
        Assert.Equal(0.75, clip.VolumeAutomation[0].Position, 6);
        Assert.Equal(1.5,  clip.VolumeAutomation[0].Volume,   6);
    }

    // ── RemoveVolumeKeyframe undo/redo ───────────────────────────────────────

    [Fact]
    public void RemoveVolumeKeyframe_IsUndoable()
    {
        var store = CreateStore();
        var clip  = new VideoClip { Name = "c.mp4", Duration = 10 };
        store.AddClipToTrack(store.PrimaryVideoTrack.Id, clip);

        store.AddVolumeKeyframe(clip.Id, 0.5, 1.0);
        var kfId = clip.VolumeAutomation[0].Id;

        store.RemoveVolumeKeyframe(clip.Id, kfId);
        Assert.Empty(clip.VolumeAutomation);

        store.Undo();
        Assert.Single(clip.VolumeAutomation);
        Assert.Equal(kfId, clip.VolumeAutomation[0].Id);

        store.Redo();
        Assert.Empty(clip.VolumeAutomation);
    }

    // ── ClearVolumeAutomation undo/redo ──────────────────────────────────────

    [Fact]
    public void ClearVolumeAutomation_IsUndoable()
    {
        var store = CreateStore();
        var clip  = new VideoClip { Name = "c.mp4", Duration = 10 };
        store.AddClipToTrack(store.PrimaryVideoTrack.Id, clip);

        store.AddVolumeKeyframe(clip.Id, 0.0, 0.5);
        store.AddVolumeKeyframe(clip.Id, 1.0, 1.5);
        Assert.Equal(2, clip.VolumeAutomation.Count);

        store.ClearVolumeAutomation(clip.Id);
        Assert.Empty(clip.VolumeAutomation);

        store.Undo();
        Assert.Equal(2, clip.VolumeAutomation.Count);

        store.Redo();
        Assert.Empty(clip.VolumeAutomation);
    }

    [Fact]
    public void ClearVolumeAutomation_OnEmptyList_DoesNotPushCommand()
    {
        var store = CreateStore();
        var clip  = new VideoClip { Name = "c.mp4", Duration = 10 };
        store.AddClipToTrack(store.PrimaryVideoTrack.Id, clip);

        // capture undo depth after adding the clip
        var undoDepthBefore = store.CanUndo;

        store.ClearVolumeAutomation(clip.Id); // nothing to clear — should not add to stack

        // undo count must not have increased (still exactly the add-clip command)
        Assert.True(undoDepthBefore);      // add-clip is still undoable
        // Undo the add-clip; nothing further should be undoable
        store.Undo();
        Assert.False(store.CanUndo);
    }

    // ── MoveClip ──────────────────────────────────────────────────────────────

    [Fact]
    public void MoveClip_ShiftsTimelinePosition()
    {
        var store = CreateStore();
        var clip  = new VideoClip { Name = "c.mp4", Duration = 10 };
        clip.TimelinePosition = 5.0;
        store.AddClipToTrack(store.PrimaryVideoTrack.Id, clip);

        store.MoveClip(clip.Id, 3.0);
        Assert.Equal(8.0, clip.TimelinePosition, 6);
    }

    [Fact]
    public void MoveClip_ClampsToZero()
    {
        var store = CreateStore();
        var clip  = new VideoClip { Name = "c.mp4", Duration = 10 };
        clip.TimelinePosition = 1.0;
        store.AddClipToTrack(store.PrimaryVideoTrack.Id, clip);

        store.MoveClip(clip.Id, -10.0);
        Assert.Equal(0.0, clip.TimelinePosition, 6);
    }

    [Fact]
    public void MoveClip_IsUndoable()
    {
        var store = CreateStore();
        var clip  = new VideoClip { Name = "c.mp4", Duration = 10 };
        clip.TimelinePosition = 4.0;
        store.AddClipToTrack(store.PrimaryVideoTrack.Id, clip);

        store.MoveClip(clip.Id, 2.0);
        Assert.Equal(6.0, clip.TimelinePosition, 6);

        store.Undo();
        Assert.Equal(4.0, clip.TimelinePosition, 6);

        store.Redo();
        Assert.Equal(6.0, clip.TimelinePosition, 6);
    }

    [Fact]
    public void MoveClip_DoesNothingForUnknownId()
    {
        var store = CreateStore();
        store.MoveClip(Guid.NewGuid(), 5.0); // should not throw
        Assert.False(store.CanUndo);
    }

    // ── ReorderTrack ──────────────────────────────────────────────────────

    [Fact]
    public void ReorderTrack_SwapsOrderWithDisplacedTrack()
    {
        var store  = CreateStore();
        var second = store.AddVideoTrack();

        var primaryOrder = store.PrimaryVideoTrack.Order;
        var secondOrder  = second.Order;

        store.ReorderTrack(second.Id, primaryOrder);

        Assert.Equal(primaryOrder, second.Order);
        Assert.Equal(secondOrder,  store.PrimaryVideoTrack.Order);
    }

    [Fact]
    public void ReorderTrack_NoOp_WhenOrderUnchanged()
    {
        var store = CreateStore();
        var track = store.AddVideoTrack();
        var order = track.Order;

        var descriptionBeforeReorder = store.UndoDescription; // "Add video track"
        store.ReorderTrack(track.Id, order);

        Assert.Equal(descriptionBeforeReorder, store.UndoDescription); // no new command pushed
        Assert.Equal(order, track.Order);
    }

    [Fact]
    public void ReorderTrack_PushesUndoCommand()
    {
        var store  = CreateStore();
        var second = store.AddVideoTrack();

        Assert.Equal("Add video track", store.UndoDescription); // from AddVideoTrack itself

        store.ReorderTrack(second.Id, store.PrimaryVideoTrack.Order);

        Assert.True(store.CanUndo);
        Assert.Equal("Reorder track", store.UndoDescription);
    }

    [Fact]
    public void ReorderTrack_Undo_RestoresOriginalOrders()
    {
        var store  = CreateStore();
        var second = store.AddVideoTrack();
        var primaryId    = store.PrimaryVideoTrack.Id;
        var primaryOrder = store.PrimaryVideoTrack.Order;
        var secondOrder  = second.Order;

        store.ReorderTrack(second.Id, primaryOrder);
        store.Undo();

        Assert.Equal(primaryOrder, store.Tracks.First(t => t.Id == primaryId).Order);
        Assert.Equal(secondOrder,  store.Tracks.First(t => t.Id == second.Id).Order);
    }

    [Fact]
    public void ReorderTrack_InvalidTrackId_Throws()
    {
        var store = CreateStore();
        Assert.Throws<ArgumentException>(() => store.ReorderTrack(Guid.NewGuid(), 0));
    }

    // ── DetachAudio ──────────────────────────────────────────────────────

    [Fact]
    public void DetachAudio_SetsMuteAudioOnVideoClip()
    {
        var store = CreateStore(o => o.AudioTracks = true);
        var clip  = new VideoClip { Name = "V1", Duration = 10 };
        store.AddClip(clip);
        var audioTrack = store.AddAudioTrack();
        var audioClip  = new AudioClip { Name = "V1 (Audio)", Duration = 10 };

        store.DetachAudio(clip.Id, audioClip, audioTrack.Id);

        Assert.True(clip.MuteAudio);
    }

    [Fact]
    public void DetachAudio_AddsAudioClipToAudioTrack()
    {
        var store = CreateStore(o => o.AudioTracks = true);
        var clip  = new VideoClip { Name = "V1", Duration = 10 };
        store.AddClip(clip);
        var audioTrack = store.AddAudioTrack();
        var audioClip  = new AudioClip { Name = "V1 (Audio)", Duration = 10 };

        store.DetachAudio(clip.Id, audioClip, audioTrack.Id);

        Assert.Contains(audioClip, audioTrack.Items);
    }

    [Fact]
    public void DetachAudio_PushesUndoCommand()
    {
        var store = CreateStore(o => o.AudioTracks = true);
        var clip  = new VideoClip { Name = "V1", Duration = 10 };
        store.AddClip(clip);
        var audioTrack = store.AddAudioTrack();
        var audioClip  = new AudioClip { Name = "V1 (Audio)", Duration = 10 };

        store.DetachAudio(clip.Id, audioClip, audioTrack.Id);

        Assert.True(store.CanUndo);
    }

    [Fact]
    public void DetachAudio_Undo_ClearsMuteAudioAndRemovesClip()
    {
        var store = CreateStore(o => o.AudioTracks = true);
        var clip  = new VideoClip { Name = "V1", Duration = 10 };
        store.AddClip(clip);
        var audioTrack = store.AddAudioTrack();
        var audioClip  = new AudioClip { Name = "V1 (Audio)", Duration = 10 };

        store.DetachAudio(clip.Id, audioClip, audioTrack.Id);
        store.Undo();

        Assert.False(clip.MuteAudio);
        Assert.DoesNotContain(audioClip, audioTrack.Items);
    }

    [Fact]
    public void DetachAudio_InvalidVideoClipId_Throws()
    {
        var store = CreateStore(o => o.AudioTracks = true);
        var audioTrack = store.AddAudioTrack();
        var audioClip  = new AudioClip { Name = "orphan", Duration = 5 };

        Assert.Throws<ArgumentException>(() =>
            store.DetachAudio(Guid.NewGuid(), audioClip, audioTrack.Id));
    }

    [Fact]
    public void DetachAudio_NonVideoClipId_Throws()
    {
        var store      = CreateStore(o => o.AudioTracks = true);
        var audioTrack = store.AddAudioTrack();
        // Place an AudioClip on the audio track, then try to detach it as if it were a VideoClip
        var audioClip  = new AudioClip { Name = "A1", Duration = 5 };
        store.AddClipToTrack(audioTrack.Id, audioClip);
        var target = new AudioClip { Name = "detach target", Duration = 5 };

        Assert.Throws<ArgumentException>(() =>
            store.DetachAudio(audioClip.Id, target, audioTrack.Id));
    }

    // ── Image clips (Phase 28) ────────────────────────────────────────────────────

    [Fact]
    public void AddImageClip_AppearInPrimaryVideoTrackImageClips()
    {
        var store = CreateStore();
        var clip  = new ImageClip { Name = "photo.png", Duration = 5.0 };

        store.AddImageClip(clip);

        Assert.Contains(clip, store.PrimaryVideoTrack.ImageClips);
    }

    [Fact]
    public void AddImageClip_FeatureFlagDisabled_Throws()
    {
        var store = CreateStore(o => o.ImageClips = false);
        var clip  = new ImageClip { Name = "photo.png", Duration = 5.0 };

        Assert.Throws<InvalidOperationException>(() => store.AddImageClip(clip));
    }

    [Fact]
    public void AddImageClip_NotifiesObservers()
    {
        var store    = CreateStore();
        var notified = false;
        store.OnChange += () => notified = true;

        store.AddImageClip(new ImageClip { Name = "a.png", Duration = 5.0 });

        Assert.True(notified);
    }

    [Fact]
    public void RemoveImageClip_RemovesFromTrack()
    {
        var store = CreateStore();
        var clip  = new ImageClip { Name = "photo.png", Duration = 5.0 };
        store.AddImageClip(clip);

        store.RemoveImageClip(clip.Id);

        Assert.Empty(store.PrimaryVideoTrack.ImageClips);
    }

    [Fact]
    public void RemoveImageClip_UnknownId_DoesNotThrow()
    {
        var store = CreateStore();

        var ex = Record.Exception(() => store.RemoveImageClip(Guid.NewGuid()));

        Assert.Null(ex);
    }

    [Fact]
    public void UpdateImageDuration_ChangesDuration()
    {
        var store = CreateStore();
        var clip  = new ImageClip { Name = "photo.png", Duration = 5.0 };
        store.AddImageClip(clip);

        store.UpdateImageDuration(clip.Id, 10.0);

        Assert.Equal(10.0, clip.Duration);
    }

    [Fact]
    public void UpdateImageDuration_ClampsToMinimum()
    {
        var store = CreateStore();
        var clip  = new ImageClip { Name = "photo.png", Duration = 5.0 };
        store.AddImageClip(clip);

        store.UpdateImageDuration(clip.Id, 0.0);

        Assert.True(clip.Duration >= 0.1);
    }

    [Fact]
    public void AddImageClip_SupportsUndo()
    {
        var store = CreateStore();
        var clip  = new ImageClip { Name = "photo.png", Duration = 5.0 };
        store.AddImageClip(clip);

        store.Undo();

        Assert.Empty(store.PrimaryVideoTrack.ImageClips);
    }

    [Fact]
    public void RemoveImageClip_SupportsUndo()
    {
        var store = CreateStore();
        var clip  = new ImageClip { Name = "photo.png", Duration = 5.0 };
        store.AddImageClip(clip);
        store.RemoveImageClip(clip.Id);

        store.Undo();

        Assert.Contains(clip, store.PrimaryVideoTrack.ImageClips);
    }

    // ── Phase 36: Track Locking ───────────────────────────────────────────────

    [Fact]
    public void LockTrack_SetsIsLocked()
    {
        var store = CreateStore();
        var track = store.PrimaryVideoTrack;

        store.LockTrack(track.Id, true);

        Assert.True(track.IsLocked);
    }

    [Fact]
    public void LockTrack_RaisesOnChange()
    {
        var store  = CreateStore();
        var raised = false;
        store.OnChange += () => raised = true;

        store.LockTrack(store.PrimaryVideoTrack.Id, true);

        Assert.True(raised);
    }

    [Fact]
    public void LockTrack_NoOpWhenAlreadyLocked()
    {
        var store  = CreateStore();
        var track  = store.PrimaryVideoTrack;
        store.LockTrack(track.Id, true);
        var changeCount = 0;
        store.OnChange += () => changeCount++;

        store.LockTrack(track.Id, true); // same value — should be no-op

        Assert.Equal(0, changeCount);
    }

    [Fact]
    public void LockTrack_SupportsUndo()
    {
        var store = CreateStore();
        var track = store.PrimaryVideoTrack;
        store.LockTrack(track.Id, true);

        store.Undo();

        Assert.False(track.IsLocked);
    }

    [Fact]
    public void UnlockTrack_SupportsUndo()
    {
        var store = CreateStore();
        var track = store.PrimaryVideoTrack;
        store.LockTrack(track.Id, true);
        store.LockTrack(track.Id, false);

        store.Undo();

        Assert.True(track.IsLocked);
    }

    [Fact]
    public void LockTrack_SupportsRedo()
    {
        var store = CreateStore();
        var track = store.PrimaryVideoTrack;
        store.LockTrack(track.Id, true);
        store.Undo();

        store.Redo();

        Assert.True(track.IsLocked);
    }

    [Fact]
    public void AddClipToLockedTrack_IsIgnored()
    {
        var store = CreateStore();
        var track = store.PrimaryVideoTrack;
        store.LockTrack(track.Id, true);

        store.AddClip(new VideoClip { Name = "a.mp4", Duration = 5 });

        Assert.Empty(store.Clips);
    }

    [Fact]
    public void RemoveClipFromLockedTrack_IsIgnored()
    {
        var store = CreateStore();
        var clip  = new VideoClip { Name = "a.mp4", Duration = 5 };
        store.AddClip(clip);
        store.LockTrack(store.PrimaryVideoTrack.Id, true);

        store.RemoveClip(clip.Id);

        Assert.Single(store.Clips);
    }

    [Fact]
    public void MoveClipOnLockedTrack_IsIgnored()
    {
        var store = CreateStore();
        var clip  = new VideoClip { Name = "a.mp4", Duration = 5, TimelinePosition = 0 };
        store.AddClip(clip);
        store.LockTrack(store.PrimaryVideoTrack.Id, true);

        store.MoveClip(clip.Id, 10.0);

        Assert.Equal(0, clip.TimelinePosition);
    }

    [Fact]
    public void UpdateTrimOnLockedTrack_IsIgnored()
    {
        var store = CreateStore();
        var clip  = new VideoClip { Name = "a.mp4", Duration = 10, StartTrim = 0, EndTrim = 10 };
        store.AddClip(clip);
        store.LockTrack(store.PrimaryVideoTrack.Id, true);

        store.UpdateTrim(clip.Id, 2, 8);

        Assert.Equal(0, clip.StartTrim);
        Assert.Equal(10, clip.EndTrim);
    }

    [Fact]
    public void DuplicateClipOnLockedTrack_IsIgnored()
    {
        var store = CreateStore();
        var clip  = new VideoClip { Name = "a.mp4", Duration = 5 };
        store.AddClip(clip);
        store.LockTrack(store.PrimaryVideoTrack.Id, true);

        store.DuplicateClip(clip.Id);

        Assert.Single(store.Clips);
    }

    [Fact]
    public void SplitClipOnLockedTrack_IsIgnored()
    {
        var store = CreateStore();
        var clip  = new VideoClip { Name = "a.mp4", Duration = 10, StartTrim = 0, EndTrim = 10 };
        store.AddClip(clip);
        store.LockTrack(store.PrimaryVideoTrack.Id, true);

        store.SplitClip(clip.Id, 5);

        Assert.Single(store.Clips);
    }

    [Fact]
    public void ReorderItemsOnLockedTrack_IsIgnored()
    {
        var store  = CreateStore();
        var clipA  = new VideoClip { Name = "a.mp4", Duration = 5 };
        var clipB  = new VideoClip { Name = "b.mp4", Duration = 5 };
        store.AddClip(clipA);
        store.AddClip(clipB);
        store.LockTrack(store.PrimaryVideoTrack.Id, true);

        // Attempt to reverse order
        store.ReorderTrackItems(store.PrimaryVideoTrack.Id, [clipB, clipA]);

        // Original order should be preserved
        var items = store.PrimaryVideoTrack.VideoClips.ToList();
        Assert.Equal(clipA.Id, items[0].Id);
        Assert.Equal(clipB.Id, items[1].Id);
    }

    [Fact]
    public void RemoveImageClipFromLockedTrack_IsIgnored()
    {
        var store = CreateStore();
        var clip  = new ImageClip { Name = "photo.png", Duration = 5.0 };
        store.AddImageClip(clip);
        store.LockTrack(store.PrimaryVideoTrack.Id, true);

        store.RemoveImageClip(clip.Id);

        Assert.Contains(clip, store.PrimaryVideoTrack.ImageClips);
    }

    [Fact]
    public void UnlockTrack_RestoresMutability()
    {
        var store = CreateStore();
        var track = store.PrimaryVideoTrack;
        store.LockTrack(track.Id, true);
        store.LockTrack(track.Id, false);

        store.AddClip(new VideoClip { Name = "a.mp4", Duration = 5 });

        Assert.Single(store.Clips);
    }

    // ── Phase 37: AllVideoClips / AllAudioClips / AllImageClips ───────────────

    [Fact]
    public void AllVideoClips_ReturnsSingleTrackClips()
    {
        var store = CreateStore();
        store.AddClip(new VideoClip { Name = "a.mp4", Duration = 5 });
        store.AddClip(new VideoClip { Name = "b.mp4", Duration = 3 });

        Assert.Equal(2, store.AllVideoClips.Count());
    }

    [Fact]
    public void AllVideoClips_AggregatesAcrossMultipleTracks()
    {
        var store = CreateStore(o => { o.MultiTrack = true; o.MaxVideoTracks = 4; });
        store.AddClip(new VideoClip { Name = "a.mp4", Duration = 5 });
        var track2 = store.AddVideoTrack();
        store.AddClipToTrack(track2.Id, new VideoClip { Name = "b.mp4", Duration = 3 });

        Assert.Equal(2, store.AllVideoClips.Count());
    }

    [Fact]
    public void AllAudioClips_ReturnsClipsFromAudioTracks()
    {
        var store = CreateStore(o => o.AudioTracks = true);
        var audioTrack = store.AudioTracks.First();
        var clip = new AudioClip { Name = "music.mp3", Duration = 60 };
        store.AddClipToTrack(audioTrack.Id, clip);

        var result = store.AllAudioClips.ToList();
        Assert.Single(result);
        Assert.Equal("music.mp3", result[0].Name);
    }

    [Fact]
    public void AllAudioClips_EmptyWhenNoAudioTracks()
    {
        var store = CreateStore();

        Assert.Empty(store.AllAudioClips);
    }

    [Fact]
    public void AllImageClips_ReturnsImageClipsFromVideoTracks()
    {
        var store = CreateStore(o => o.ImageClips = true);
        store.AddImageClip(new ImageClip { Name = "photo.png", Duration = 5 });
        store.AddImageClip(new ImageClip { Name = "bg.jpg",   Duration = 5 });

        Assert.Equal(2, store.AllImageClips.Count());
    }

    [Fact]
    public void AllImageClips_EmptyWhenNoneAdded()
    {
        var store = CreateStore(o => o.ImageClips = true);

        Assert.Empty(store.AllImageClips);
    }

    // ── Phase 37: RenameTrack ─────────────────────────────────────────────────

    [Fact]
    public void RenameTrack_ChangesLabel()
    {
        var store = CreateStore();
        var track = store.PrimaryVideoTrack;

        store.RenameTrack(track.Id, "My Footage");

        Assert.Equal("My Footage", track.Label);
    }

    [Fact]
    public void RenameTrack_TrimsWhitespace()
    {
        var store = CreateStore();
        var track = store.PrimaryVideoTrack;

        store.RenameTrack(track.Id, "  B-Roll  ");

        Assert.Equal("B-Roll", track.Label);
    }

    [Fact]
    public void RenameTrack_NoOpForEmptyString()
    {
        var store = CreateStore();
        var track = store.PrimaryVideoTrack;
        var original = track.Label;

        store.RenameTrack(track.Id, "   ");

        Assert.Equal(original, track.Label);
    }

    [Fact]
    public void RenameTrack_NoOpForSameName()
    {
        var store = CreateStore();
        var track = store.PrimaryVideoTrack;
        var original = track.Label;
        var fired = false;
        store.OnChange += () => fired = true;

        store.RenameTrack(track.Id, original);

        Assert.False(fired, "OnChange should not fire when name is unchanged");
    }

    [Fact]
    public void RenameTrack_RaisesOnChange()
    {
        var store = CreateStore();
        var fired = false;
        store.OnChange += () => fired = true;

        store.RenameTrack(store.PrimaryVideoTrack.Id, "New Name");

        Assert.True(fired);
    }

    // ── Phase 38: image-duration trim + lock guard ────────────────────────────

    [Fact]
    public void UpdateImageDuration_UpdatesDuration()
    {
        var store = CreateStore(o => o.ImageClips = true);
        var clip  = new ImageClip { Name = "bg.png", Duration = 5.0 };
        store.AddImageClip(clip);

        store.UpdateImageDuration(clip.Id, 8.5);

        Assert.Equal(8.5, clip.Duration);
    }

    [Fact]
    public void UpdateImageDuration_RespectsLockedTrack()
    {
        var store = CreateStore(o => o.ImageClips = true);
        var clip  = new ImageClip { Name = "bg.png", Duration = 5.0 };
        store.AddImageClip(clip);
        store.LockTrack(store.PrimaryVideoTrack.Id, true);

        store.UpdateImageDuration(clip.Id, 9.0);

        Assert.Equal(5.0, clip.Duration);
    }

    [Fact]
    public void UpdateImageDuration_ClampsToMinimumWhenExtremeLow()
    {
        var store = CreateStore(o => o.ImageClips = true);
        var clip  = new ImageClip { Name = "bg.png", Duration = 5.0 };
        store.AddImageClip(clip);

        store.UpdateImageDuration(clip.Id, -3.0);

        Assert.Equal(0.1, clip.Duration);
    }

    // ── Phase 38: CommitDraggedPosition (middle-drag move) ────────────────────

    [Fact]
    public void CommitDraggedPosition_UpdatesTimelinePosition()
    {
        var store = CreateStore();
        var clip  = new VideoClip { Name = "a.mp4", Duration = 5, TimelinePosition = 0 };
        store.AddClip(clip);

        // Simulate live preview (already moved to 3s)
        clip.TimelinePosition = 3.0;
        store.CommitDraggedPosition(clip.Id, 0.0);

        Assert.Equal(3.0, clip.TimelinePosition);
    }

    [Fact]
    public void CommitDraggedPosition_SupportsUndo()
    {
        var store = CreateStore();
        var clip  = new VideoClip { Name = "a.mp4", Duration = 5, TimelinePosition = 0 };
        store.AddClip(clip);

        clip.TimelinePosition = 4.0;
        store.CommitDraggedPosition(clip.Id, 0.0);
        store.Undo();

        Assert.Equal(0.0, clip.TimelinePosition);
    }

    [Fact]
    public void CommitDraggedPosition_RespectsLockedTrack()
    {
        var store = CreateStore();
        var clip  = new VideoClip { Name = "a.mp4", Duration = 5, TimelinePosition = 0 };
        store.AddClip(clip);
        store.LockTrack(store.PrimaryVideoTrack.Id, true);

        clip.TimelinePosition = 5.0;
        store.CommitDraggedPosition(clip.Id, 0.0);

        // Lock reverts position to originalPosition
        Assert.Equal(0.0, clip.TimelinePosition);
    }

    [Fact]
    public void CommitDraggedPosition_NoUndoEntryWhenPositionUnchanged()
    {
        var store = CreateStore();
        var clip  = new VideoClip { Name = "a.mp4", Duration = 5, TimelinePosition = 2.0 };
        store.AddClip(clip);
        var undoDescBefore = store.UndoDescription;

        store.CommitDraggedPosition(clip.Id, 2.0); // no change — delta < 0.001

        Assert.Equal(undoDescBefore, store.UndoDescription);
    }

    // ── Phase 103: CommitDraggedPositionAndTrack (cross-track middle-drag move, item #25) ──────

    [Fact]
    public void CommitDraggedPositionAndTrack_SameTrack_BehavesLikeCommitDraggedPosition()
    {
        var store = CreateStore();
        var clip  = new VideoClip { Name = "a.mp4", Duration = 5, TimelinePosition = 0 };
        store.AddClip(clip);
        var trackId = store.PrimaryVideoTrack.Id;

        clip.TimelinePosition = 3.0; // live-preview already moved it
        store.CommitDraggedPositionAndTrack(clip.Id, trackId, 0.0);

        Assert.Equal(3.0, clip.TimelinePosition);
        Assert.Contains(clip, store.PrimaryVideoTrack.Items);
    }

    [Fact]
    public void CommitDraggedPositionAndTrack_TrackChanged_MovesItemToNewTracksItems()
    {
        var store = CreateStore(o => { o.MultiTrack = true; o.MaxVideoTracks = 4; });
        var fromTrackId = store.PrimaryVideoTrack.Id;
        var clip = new VideoClip { Name = "a.mp4", Duration = 5, TimelinePosition = 0 };
        store.AddClip(clip);
        var toTrack = store.AddVideoTrack();

        // Only horizontal position is live-previewed during the drag (item stays on its
        // original track's Items the whole gesture — see the method's own doc comment for why);
        // the target track is passed explicitly and the move happens entirely inside the commit.
        clip.TimelinePosition = 2.0;

        store.CommitDraggedPositionAndTrack(clip.Id, fromTrackId, 0.0, toTrack.Id);

        Assert.DoesNotContain(clip, store.PrimaryVideoTrack.Items);
        Assert.Contains(clip, toTrack.Items);
        Assert.Equal(2.0, clip.TimelinePosition);
    }

    [Fact]
    public void CommitDraggedPositionAndTrack_TrackChanged_SupportsUndo()
    {
        var store = CreateStore(o => { o.MultiTrack = true; o.MaxVideoTracks = 4; });
        var fromTrackId = store.PrimaryVideoTrack.Id;
        var clip = new VideoClip { Name = "a.mp4", Duration = 5, TimelinePosition = 0 };
        store.AddClip(clip);
        var toTrack = store.AddVideoTrack();

        clip.TimelinePosition = 2.0;
        store.CommitDraggedPositionAndTrack(clip.Id, fromTrackId, 0.0, toTrack.Id);

        store.Undo();

        Assert.Contains(clip, store.PrimaryVideoTrack.Items);
        Assert.DoesNotContain(clip, toTrack.Items);
        Assert.Equal(0.0, clip.TimelinePosition);
    }

    [Fact]
    public void CommitDraggedPositionAndTrack_TrackChanged_SupportsRedo()
    {
        var store = CreateStore(o => { o.MultiTrack = true; o.MaxVideoTracks = 4; });
        var fromTrackId = store.PrimaryVideoTrack.Id;
        var clip = new VideoClip { Name = "a.mp4", Duration = 5, TimelinePosition = 0 };
        store.AddClip(clip);
        var toTrack = store.AddVideoTrack();

        clip.TimelinePosition = 2.0;
        store.CommitDraggedPositionAndTrack(clip.Id, fromTrackId, 0.0, toTrack.Id);
        store.Undo();

        store.Redo();

        Assert.DoesNotContain(clip, store.PrimaryVideoTrack.Items);
        Assert.Contains(clip, toTrack.Items);
        Assert.Equal(2.0, clip.TimelinePosition);
    }

    [Fact]
    public void CommitDraggedPositionAndTrack_LockedTargetTrack_RevertsTrackAndPosition()
    {
        var store = CreateStore(o => { o.MultiTrack = true; o.MaxVideoTracks = 4; });
        var fromTrackId = store.PrimaryVideoTrack.Id;
        var clip = new VideoClip { Name = "a.mp4", Duration = 5, TimelinePosition = 0 };
        store.AddClip(clip);
        var toTrack = store.AddVideoTrack();
        store.LockTrack(toTrack.Id, true);

        clip.TimelinePosition = 2.0;

        store.CommitDraggedPositionAndTrack(clip.Id, fromTrackId, 0.0, toTrack.Id);

        Assert.Contains(clip, store.PrimaryVideoTrack.Items);
        Assert.DoesNotContain(clip, toTrack.Items);
        Assert.Equal(0.0, clip.TimelinePosition);
    }

    // ── Phase 106: InsertClipWithRipple (ripple-insert-with-confirmation, item #25 part 4) ─────

    [Fact]
    public void InsertClipWithRipple_EmptyTrack_PlacesClipAtPosition()
    {
        var store = CreateStore();
        var clip  = new VideoClip { Name = "a.mp4", Duration = 5 };

        store.InsertClipWithRipple(store.PrimaryVideoTrack.Id, clip, 3.0);

        Assert.Contains(clip, store.PrimaryVideoTrack.Items);
        Assert.Equal(3.0, clip.TimelinePosition, 3);
    }

    [Fact]
    public void InsertClipWithRipple_ShiftsClipsAtOrAfterInsertionPoint()
    {
        var store = CreateStore();
        var a = new VideoClip { Name = "a.mp4", Duration = 5, TimelinePosition = 0 };
        var b = new VideoClip { Name = "b.mp4", Duration = 3, TimelinePosition = 5 };
        store.AddClip(a); store.AddClip(b);
        var inserted = new VideoClip { Name = "new.mp4", Duration = 4 };

        // Insert touching a's end (5s) — b, which starts exactly there, must shift later by 4s.
        store.InsertClipWithRipple(store.PrimaryVideoTrack.Id, inserted, 5.0);

        Assert.Equal(0.0, a.TimelinePosition, 3);   // untouched — starts before insertion point
        Assert.Equal(5.0, inserted.TimelinePosition, 3);
        Assert.Equal(9.0, b.TimelinePosition, 3);   // 5 + 4
    }

    [Fact]
    public void InsertClipWithRipple_DoesNotShiftClipsBeforeInsertionPoint()
    {
        var store = CreateStore();
        var a = new VideoClip { Name = "a.mp4", Duration = 5, TimelinePosition = 0 };
        store.AddClip(a);
        var inserted = new VideoClip { Name = "new.mp4", Duration = 4 };

        store.InsertClipWithRipple(store.PrimaryVideoTrack.Id, inserted, 10.0);

        Assert.Equal(0.0, a.TimelinePosition, 3);
    }

    [Fact]
    public void InsertClipWithRipple_SupportsUndo()
    {
        var store = CreateStore();
        var a = new VideoClip { Name = "a.mp4", Duration = 5, TimelinePosition = 0 };
        var b = new VideoClip { Name = "b.mp4", Duration = 3, TimelinePosition = 5 };
        store.AddClip(a); store.AddClip(b);
        var inserted = new VideoClip { Name = "new.mp4", Duration = 4 };

        store.InsertClipWithRipple(store.PrimaryVideoTrack.Id, inserted, 5.0);
        store.Undo();

        Assert.DoesNotContain(inserted, store.PrimaryVideoTrack.Items);
        Assert.Equal(5.0, b.TimelinePosition, 3); // shifted back
    }

    [Fact]
    public void InsertClipWithRipple_SupportsRedo()
    {
        var store = CreateStore();
        var a = new VideoClip { Name = "a.mp4", Duration = 5, TimelinePosition = 0 };
        var b = new VideoClip { Name = "b.mp4", Duration = 3, TimelinePosition = 5 };
        store.AddClip(a); store.AddClip(b);
        var inserted = new VideoClip { Name = "new.mp4", Duration = 4 };

        store.InsertClipWithRipple(store.PrimaryVideoTrack.Id, inserted, 5.0);
        store.Undo();
        store.Redo();

        Assert.Contains(inserted, store.PrimaryVideoTrack.Items);
        Assert.Equal(9.0, b.TimelinePosition, 3);
    }

    [Fact]
    public void InsertClipWithRipple_NoOpOnLockedTrack()
    {
        var store = CreateStore();
        var clip = new VideoClip { Name = "a.mp4", Duration = 5, TimelinePosition = 0 };
        store.AddClip(clip);
        store.LockTrack(store.PrimaryVideoTrack.Id, true);
        var inserted = new VideoClip { Name = "new.mp4", Duration = 4 };

        store.InsertClipWithRipple(store.PrimaryVideoTrack.Id, inserted, 2.0);

        Assert.DoesNotContain(inserted, store.PrimaryVideoTrack.Items);
    }

    // ── Item #59: ripple-insert must leave Order chronologically consistent ─────────────────────
    //
    // Order is not cosmetic: TimelineTrack.VideoClips sorts by it, and ExportService's pipeline
    // consumes that order directly (ExportService.RunPipelineAsync) — so an Order that disagrees
    // with TimelinePosition doesn't just draw the timeline wrong, it concatenates the exported
    // video in the wrong sequence. The pre-existing tests above only ever asserted
    // TimelinePosition, which is exactly why this went unnoticed.

    [Fact]
    public void InsertClipWithRipple_InsertingBeforeExistingClip_LeavesOrderChronological()
    {
        var store = CreateStore();
        var existing = new VideoClip { Name = "existing.mp4", Duration = 5, TimelinePosition = 0 };
        store.AddClip(existing);
        var inserted = new VideoClip { Name = "new.mp4", Duration = 4 };

        // Insert at 0 — the new clip becomes chronologically FIRST, existing shifts to 4s.
        store.InsertClipWithRipple(store.PrimaryVideoTrack.Id, inserted, 0.0);

        Assert.Equal(0.0, inserted.TimelinePosition, 3);
        Assert.Equal(4.0, existing.TimelinePosition, 3);

        // Order must agree with chronology, or export/render sequence them backwards.
        Assert.True(inserted.Order < existing.Order,
            $"inserted clip starts earlier ({inserted.TimelinePosition}s) than existing " +
            $"({existing.TimelinePosition}s) but has a higher Order " +
            $"({inserted.Order} vs {existing.Order}) — export and the timeline render loop both " +
            "sequence by Order, so they would play/draw in reverse.");
    }

    [Fact]
    public void InsertClipWithRipple_OrderMatchesTimelinePositionSequence()
    {
        var store = CreateStore();
        var a = new VideoClip { Name = "a.mp4", Duration = 5, TimelinePosition = 0 };
        var b = new VideoClip { Name = "b.mp4", Duration = 5, TimelinePosition = 5 };
        store.AddClip(a); store.AddClip(b);
        var inserted = new VideoClip { Name = "mid.mp4", Duration = 2 };

        // Insert between a and b.
        store.InsertClipWithRipple(store.PrimaryVideoTrack.Id, inserted, 5.0);

        var byOrder    = store.PrimaryVideoTrack.Items.OrderBy(i => i.Order).Select(i => i.Name).ToList();
        var byPosition = store.PrimaryVideoTrack.Items.OrderBy(i => i.TimelinePosition).Select(i => i.Name).ToList();

        Assert.Equal(byPosition, byOrder);
    }

    [Fact]
    public void InsertClipWithRipple_Undo_RestoresPositionsInTheTrackItself()
    {
        // The pre-existing undo test asserts on the caller's own `b` reference. That can pass even
        // if the track's list holds different instances — RenumberItems replaces every entry via
        // `with { Order = i }`, which copies records. Assert through the track instead.
        var store = CreateStore();
        var a = new VideoClip { Name = "a.mp4", Duration = 5, TimelinePosition = 0 };
        var b = new VideoClip { Name = "b.mp4", Duration = 3, TimelinePosition = 5 };
        store.AddClip(a); store.AddClip(b);
        var inserted = new VideoClip { Name = "new.mp4", Duration = 4 };

        store.InsertClipWithRipple(store.PrimaryVideoTrack.Id, inserted, 5.0);
        store.Undo();

        var names = store.PrimaryVideoTrack.Items.Select(i => i.Name).ToList();
        Assert.DoesNotContain("new.mp4", names);

        var bInTrack = store.PrimaryVideoTrack.Items.Single(i => i.Name == "b.mp4");
        Assert.Equal(5.0, bInTrack.TimelinePosition, 3);
    }

    // ── Phase 107: OverwriteInsert (item #49 — Insert vs. Overwrite edit modes) ─────────────────

    [Fact]
    public void OverwriteInsert_NoOverlap_PlacesClipUntouched()
    {
        var store = CreateStore();
        var a = new VideoClip { Name = "a.mp4", Duration = 5, TimelinePosition = 0 };
        store.AddClip(a);
        var overwrite = new VideoClip { Name = "new.mp4", Duration = 3 };

        store.OverwriteInsert(store.PrimaryVideoTrack.Id, overwrite, 10.0);

        Assert.Equal(0.0, a.TimelinePosition, 3);
        Assert.Equal(5.0, a.TrimmedDuration, 3);
        Assert.Contains(overwrite, store.PrimaryVideoTrack.Items);
        Assert.Equal(10.0, overwrite.TimelinePosition, 3);
    }

    [Fact]
    public void OverwriteInsert_FullyCoveredClip_IsRemoved()
    {
        var store = CreateStore();
        var a = new VideoClip { Name = "a.mp4", Duration = 5, TimelinePosition = 2 };
        store.AddClip(a);
        var overwrite = new VideoClip { Name = "new.mp4", Duration = 10 };

        store.OverwriteInsert(store.PrimaryVideoTrack.Id, overwrite, 0.0);

        Assert.DoesNotContain(a, store.PrimaryVideoTrack.Items);
        Assert.Single(store.PrimaryVideoTrack.Items); // only the new clip remains
    }

    [Fact]
    public void OverwriteInsert_OverlapAtEnd_TrimsExistingEndBack()
    {
        var store = CreateStore();
        var a = new VideoClip { Name = "a.mp4", Duration = 10, TimelinePosition = 0, EndTrim = 10 };
        store.AddClip(a);
        var overwrite = new VideoClip { Name = "new.mp4", Duration = 5 };

        store.OverwriteInsert(store.PrimaryVideoTrack.Id, overwrite, 7.0);

        var remaining = store.PrimaryVideoTrack.VideoClips.Single(c => c.Id == a.Id);
        Assert.Equal(0.0, remaining.TimelinePosition, 3);
        Assert.Equal(7.0, remaining.TrimmedDuration, 3);
    }

    [Fact]
    public void OverwriteInsert_OverlapAtStart_TrimsExistingStartForward()
    {
        var store = CreateStore();
        var a = new VideoClip { Name = "a.mp4", Duration = 10, TimelinePosition = 5, EndTrim = 10 };
        store.AddClip(a);
        var overwrite = new VideoClip { Name = "new.mp4", Duration = 8 };

        store.OverwriteInsert(store.PrimaryVideoTrack.Id, overwrite, 0.0);

        var remaining = store.PrimaryVideoTrack.VideoClips.Single(c => c.Id == a.Id);
        Assert.Equal(8.0, remaining.TimelinePosition, 3);
        Assert.Equal(7.0, remaining.TrimmedDuration, 3);
    }

    [Fact]
    public void OverwriteInsert_LandsInsideExisting_SplitsIntoTwoClips()
    {
        var store = CreateStore();
        var a = new VideoClip { Name = "a.mp4", Duration = 20, TimelinePosition = 0, EndTrim = 20 };
        store.AddClip(a);
        var overwrite = new VideoClip { Name = "new.mp4", Duration = 4 };

        store.OverwriteInsert(store.PrimaryVideoTrack.Id, overwrite, 8.0);

        var videoClips = store.PrimaryVideoTrack.VideoClips.Where(c => c.Id != overwrite.Id).ToList();
        Assert.Equal(2, videoClips.Count);
        Assert.Contains(videoClips, c => Math.Abs(c.TimelinePosition - 0.0) < 0.001 && Math.Abs(c.TrimmedDuration - 8.0) < 0.001);
        Assert.Contains(videoClips, c => Math.Abs(c.TimelinePosition - 12.0) < 0.001 && Math.Abs(c.TrimmedDuration - 8.0) < 0.001);
    }

    [Fact]
    public void OverwriteInsert_SupportsUndo()
    {
        var store = CreateStore();
        var a = new VideoClip { Name = "a.mp4", Duration = 10, TimelinePosition = 0, EndTrim = 10 };
        store.AddClip(a);
        var overwrite = new VideoClip { Name = "new.mp4", Duration = 5 };

        store.OverwriteInsert(store.PrimaryVideoTrack.Id, overwrite, 7.0);
        store.Undo();

        Assert.DoesNotContain(overwrite, store.PrimaryVideoTrack.Items);
        var restored = store.PrimaryVideoTrack.VideoClips.Single(c => c.Id == a.Id);
        Assert.Equal(0.0, restored.TimelinePosition, 3);
        Assert.Equal(10.0, restored.TrimmedDuration, 3);
    }

    [Fact]
    public void OverwriteInsert_SupportsRedo()
    {
        var store = CreateStore();
        var a = new VideoClip { Name = "a.mp4", Duration = 10, TimelinePosition = 0, EndTrim = 10 };
        store.AddClip(a);
        var overwrite = new VideoClip { Name = "new.mp4", Duration = 5 };

        store.OverwriteInsert(store.PrimaryVideoTrack.Id, overwrite, 7.0);
        store.Undo();
        store.Redo();

        Assert.Contains(overwrite, store.PrimaryVideoTrack.Items);
        var remaining = store.PrimaryVideoTrack.VideoClips.Single(c => c.Id == a.Id);
        Assert.Equal(7.0, remaining.TrimmedDuration, 3);
    }

    [Fact]
    public void OverwriteInsert_NoOpOnLockedTrack()
    {
        var store = CreateStore();
        var a = new VideoClip { Name = "a.mp4", Duration = 10, TimelinePosition = 0, EndTrim = 10 };
        store.AddClip(a);
        store.LockTrack(store.PrimaryVideoTrack.Id, true);
        var overwrite = new VideoClip { Name = "new.mp4", Duration = 5 };

        store.OverwriteInsert(store.PrimaryVideoTrack.Id, overwrite, 7.0);

        Assert.DoesNotContain(overwrite, store.PrimaryVideoTrack.Items);
        Assert.Single(store.PrimaryVideoTrack.Items);
    }

    // ── Phase 108: Slip / Roll / Slide trim edits (item #50) ────────────────────────────────────

    [Fact]
    public void SlipClip_ShiftsSourceWindow_WithoutMovingOrResizingOnTimeline()
    {
        var store = CreateStore();
        var a = new VideoClip { Name = "a.mp4", Duration = 20, TimelinePosition = 5, StartTrim = 4, EndTrim = 10 };
        store.AddClip(a);

        store.SlipClip(a.Id, 2.0);

        Assert.Equal(6.0, a.StartTrim, 3);
        Assert.Equal(12.0, a.EndTrim, 3);
        Assert.Equal(5.0, a.TimelinePosition, 3);   // unchanged
        Assert.Equal(6.0, a.TrimmedDuration, 3);    // unchanged (12-6 == 10-4)
    }

    [Fact]
    public void SlipClip_ClampsToSourceBounds()
    {
        var store = CreateStore();
        var a = new VideoClip { Name = "a.mp4", Duration = 10, TimelinePosition = 0, StartTrim = 8, EndTrim = 10 };
        store.AddClip(a);

        store.SlipClip(a.Id, 5.0); // only 0 room to grow past EndTrim=10 (Duration=10)

        Assert.Equal(8.0, a.StartTrim, 3);
        Assert.Equal(10.0, a.EndTrim, 3);
    }

    [Fact]
    public void SlipClip_SupportsUndo()
    {
        var store = CreateStore();
        var a = new VideoClip { Name = "a.mp4", Duration = 20, TimelinePosition = 0, StartTrim = 4, EndTrim = 10 };
        store.AddClip(a);

        store.SlipClip(a.Id, 2.0);
        store.Undo();

        Assert.Equal(4.0, a.StartTrim, 3);
        Assert.Equal(10.0, a.EndTrim, 3);
    }

    [Fact]
    public void SlipClip_NoOpOnLockedTrack()
    {
        var store = CreateStore();
        var a = new VideoClip { Name = "a.mp4", Duration = 20, TimelinePosition = 0, StartTrim = 4, EndTrim = 10 };
        store.AddClip(a);
        store.LockTrack(store.PrimaryVideoTrack.Id, true);

        store.SlipClip(a.Id, 2.0);

        Assert.Equal(4.0, a.StartTrim, 3);
        Assert.Equal(10.0, a.EndTrim, 3);
    }

    [Fact]
    public void RollEdit_MovesSharedBoundary_KeepsCombinedSpanConstant()
    {
        var store = CreateStore();
        // a: 0..10 (source 0..10). b: touches at 10, on-timeline 10..18 (source 0..8).
        var a = new VideoClip { Name = "a.mp4", Duration = 20, TimelinePosition = 0, StartTrim = 0, EndTrim = 10 };
        var b = new VideoClip { Name = "b.mp4", Duration = 20, TimelinePosition = 10, StartTrim = 0, EndTrim = 8 };
        store.AddClip(a); store.AddClip(b);

        store.RollEdit(a.Id, 3.0);

        Assert.Equal(13.0, a.EndTrim, 3);           // a grew by 3
        Assert.Equal(3.0, b.StartTrim, 3);           // b shrank from the front by 3
        Assert.Equal(13.0, b.TimelinePosition, 3);   // b starts exactly where a now ends
        Assert.Equal(18.0, b.TimelinePosition + b.TrimmedDuration, 3); // combined span unchanged
    }

    [Fact]
    public void RollEdit_ClampsToNeighborRoom()
    {
        var store = CreateStore();
        var a = new VideoClip { Name = "a.mp4", Duration = 10, TimelinePosition = 0, StartTrim = 0, EndTrim = 10 }; // no room to grow
        var b = new VideoClip { Name = "b.mp4", Duration = 20, TimelinePosition = 10, StartTrim = 0, EndTrim = 8 };
        store.AddClip(a); store.AddClip(b);

        store.RollEdit(a.Id, 5.0);

        Assert.Equal(10.0, a.EndTrim, 3); // unchanged — a's source is already fully used
        Assert.Equal(10.0, b.TimelinePosition, 3);
    }

    [Fact]
    public void RollEdit_NoOpWhenNoFollowingClip()
    {
        var store = CreateStore();
        var a = new VideoClip { Name = "a.mp4", Duration = 20, TimelinePosition = 0, StartTrim = 0, EndTrim = 10 };
        store.AddClip(a);

        store.RollEdit(a.Id, 3.0);

        Assert.Equal(10.0, a.EndTrim, 3);
    }

    [Fact]
    public void RollEdit_SupportsUndo()
    {
        var store = CreateStore();
        var a = new VideoClip { Name = "a.mp4", Duration = 20, TimelinePosition = 0, StartTrim = 0, EndTrim = 10 };
        var b = new VideoClip { Name = "b.mp4", Duration = 20, TimelinePosition = 10, StartTrim = 0, EndTrim = 8 };
        store.AddClip(a); store.AddClip(b);

        store.RollEdit(a.Id, 3.0);
        store.Undo();

        Assert.Equal(10.0, a.EndTrim, 3);
        Assert.Equal(0.0, b.StartTrim, 3);
        Assert.Equal(10.0, b.TimelinePosition, 3);
    }

    [Fact]
    public void SlideClip_MovesMidClip_NeighborsAbsorbChange()
    {
        var store = CreateStore();
        // a: 0..10 (source 0..10, room to grow). mid: 10..15 (source 2..7). next: 15..25 (source 0..10).
        var a    = new VideoClip { Name = "a.mp4",   Duration = 20, TimelinePosition = 0,  StartTrim = 0, EndTrim = 10 };
        var mid  = new VideoClip { Name = "mid.mp4", Duration = 20, TimelinePosition = 10, StartTrim = 2, EndTrim = 7 };
        var next = new VideoClip { Name = "n.mp4",   Duration = 20, TimelinePosition = 15, StartTrim = 0, EndTrim = 10 };
        store.AddClip(a); store.AddClip(mid); store.AddClip(next);

        store.SlideClip(mid.Id, 2.0);

        Assert.Equal(12.0, a.EndTrim, 3);              // a extends to fill the gap
        Assert.Equal(12.0, mid.TimelinePosition, 3);    // mid moved right by 2
        Assert.Equal(2.0, mid.StartTrim, 3);            // mid's own trim unchanged
        Assert.Equal(7.0, mid.EndTrim, 3);
        Assert.Equal(2.0, next.StartTrim, 3);           // next trims forward by 2
        Assert.Equal(17.0, next.TimelinePosition, 3);   // next starts where mid now ends
    }

    [Fact]
    public void SlideClip_NoOpWhenMissingEitherNeighbor()
    {
        var store = CreateStore();
        var mid  = new VideoClip { Name = "mid.mp4", Duration = 20, TimelinePosition = 10, StartTrim = 2, EndTrim = 7 };
        var next = new VideoClip { Name = "n.mp4",   Duration = 20, TimelinePosition = 15, StartTrim = 0, EndTrim = 10 };
        store.AddClip(mid); store.AddClip(next); // no "prev" neighbor

        store.SlideClip(mid.Id, 2.0);

        Assert.Equal(10.0, mid.TimelinePosition, 3); // unchanged
    }

    [Fact]
    public void SlideClip_SupportsUndo()
    {
        var store = CreateStore();
        var a    = new VideoClip { Name = "a.mp4",   Duration = 20, TimelinePosition = 0,  StartTrim = 0, EndTrim = 10 };
        var mid  = new VideoClip { Name = "mid.mp4", Duration = 20, TimelinePosition = 10, StartTrim = 2, EndTrim = 7 };
        var next = new VideoClip { Name = "n.mp4",   Duration = 20, TimelinePosition = 15, StartTrim = 0, EndTrim = 10 };
        store.AddClip(a); store.AddClip(mid); store.AddClip(next);

        store.SlideClip(mid.Id, 2.0);
        store.Undo();

        Assert.Equal(10.0, a.EndTrim, 3);
        Assert.Equal(10.0, mid.TimelinePosition, 3);
        Assert.Equal(0.0, next.StartTrim, 3);
        Assert.Equal(15.0, next.TimelinePosition, 3);
    }

    // ── Phase 110: Link/unlink clips (item #52 — J-cuts/L-cuts) ─────────────────────────────────

    private static ClipStore CreateStoreWithAudio(out VideoClip video, out AudioClip audio,
        double videoPos = 0, double audioPos = 0)
    {
        var store = CreateStore(o => { o.AudioTracks = true; });
        video = new VideoClip { Name = "v.mp4", Duration = 10, TimelinePosition = videoPos, EndTrim = 10 };
        store.AddClip(video);
        var track = store.AddAudioTrack();
        audio = new AudioClip { Name = "a.mp3", Duration = 10, TimelinePosition = audioPos, EndTrim = 10 };
        store.AddClipToTrack(track.Id, audio);
        return store;
    }

    [Fact]
    public void LinkClips_SetsSymmetricLink()
    {
        var store = CreateStoreWithAudio(out var video, out var audio);

        store.LinkClips(video.Id, audio.Id);

        Assert.Equal(audio.Id, video.LinkedClipId);
        Assert.Equal(video.Id, audio.LinkedClipId);
    }

    [Fact]
    public void LinkClips_ReplacesExistingLinkOnEitherSide()
    {
        var store = CreateStoreWithAudio(out var video, out var audio);
        var track = store.AudioTracks.First();
        var otherAudio = new AudioClip { Name = "other.mp3", Duration = 5, TimelinePosition = 20 };
        store.AddClipToTrack(track.Id, otherAudio);
        store.LinkClips(video.Id, audio.Id);

        store.LinkClips(video.Id, otherAudio.Id);

        Assert.Equal(otherAudio.Id, video.LinkedClipId);
        Assert.Equal(video.Id, otherAudio.LinkedClipId);
        Assert.Null(audio.LinkedClipId); // old partner unlinked
    }

    [Fact]
    public void LinkClips_SupportsUndo()
    {
        var store = CreateStoreWithAudio(out var video, out var audio);

        store.LinkClips(video.Id, audio.Id);
        store.Undo();

        Assert.Null(video.LinkedClipId);
        Assert.Null(audio.LinkedClipId);
    }

    [Fact]
    public void UnlinkClip_ClearsBothSides()
    {
        var store = CreateStoreWithAudio(out var video, out var audio);
        store.LinkClips(video.Id, audio.Id);

        store.UnlinkClip(video.Id);

        Assert.Null(video.LinkedClipId);
        Assert.Null(audio.LinkedClipId);
    }

    [Fact]
    public void UnlinkClip_NoOpWhenNotLinked()
    {
        var store = CreateStoreWithAudio(out var video, out var audio);
        var undoDescBefore = store.UndoDescription;

        store.UnlinkClip(video.Id); // never linked

        Assert.Null(video.LinkedClipId);
        Assert.Equal(undoDescBefore, store.UndoDescription); // no new command pushed
    }

    [Fact]
    public void UnlinkClip_SupportsUndo()
    {
        var store = CreateStoreWithAudio(out var video, out var audio);
        store.LinkClips(video.Id, audio.Id);

        store.UnlinkClip(video.Id);
        store.Undo();

        Assert.Equal(audio.Id, video.LinkedClipId);
        Assert.Equal(video.Id, audio.LinkedClipId);
    }

    [Fact]
    public void FindNearbyLinkCandidate_FindsClosestUnlinkedAudioWithinThreshold()
    {
        var store = CreateStoreWithAudio(out var video, out var audio, videoPos: 0, audioPos: 0.5);

        var found = store.FindNearbyLinkCandidate(video, thresholdSeconds: 1.0);

        Assert.Equal(audio.Id, found?.Id);
    }

    [Fact]
    public void FindNearbyLinkCandidate_ReturnsNullOutsideThreshold()
    {
        var store = CreateStoreWithAudio(out var video, out var audio, videoPos: 0, audioPos: 5.0);

        var found = store.FindNearbyLinkCandidate(video, thresholdSeconds: 1.0);

        Assert.Null(found);
    }

    [Fact]
    public void FindNearbyLinkCandidate_ExcludesAlreadyLinkedAudio()
    {
        var store = CreateStoreWithAudio(out var video, out var audio, videoPos: 0, audioPos: 0.2);
        var otherVideo = new VideoClip { Name = "v2.mp4", Duration = 5, TimelinePosition = 50, EndTrim = 5 };
        store.AddClip(otherVideo);
        store.LinkClips(otherVideo.Id, audio.Id); // audio now linked to a different video

        var found = store.FindNearbyLinkCandidate(video, thresholdSeconds: 1.0);

        Assert.Null(found);
    }

    // ── Phase 39: ClipStore.Reset ─────────────────────────────────────────────

    [Fact]
    public void Reset_ClearsAllClipsAndRestoresPrimaryTrack()
    {
        var store = CreateStore(o => { o.MultiTrack = true; o.MaxVideoTracks = 4; });
        store.AddClip(new VideoClip { Name = "a.mp4", Duration = 5 });
        store.AddVideoTrack();

        store.Reset();

        Assert.Single(store.VideoTracks);
        Assert.Empty(store.Clips);
    }

    [Fact]
    public void Reset_ClearsUndoStack()
    {
        var store = CreateStore();
        store.AddClip(new VideoClip { Name = "a.mp4", Duration = 5 });

        store.Reset();

        Assert.False(store.CanUndo);
    }

    [Fact]
    public void Reset_RaisesOnChange()
    {
        var store = CreateStore();
        var fired = false;
        store.OnChange += () => fired = true;

        store.Reset();

        Assert.True(fired);
    }

    // ── Phase 40: Ripple Edit ─────────────────────────────────────────────────

    [Fact]
    public void RippleDeleteClip_RemovesClipAndShiftsSubsequent()
    {
        var store = CreateStore();
        var a = new VideoClip { Name = "a.mp4", Duration = 5, TimelinePosition = 0 };
        var b = new VideoClip { Name = "b.mp4", Duration = 3, TimelinePosition = 5 };
        var c = new VideoClip { Name = "c.mp4", Duration = 4, TimelinePosition = 8 };
        store.AddClip(a); store.AddClip(b); store.AddClip(c);

        store.RippleDeleteClip(b.Id);

        Assert.Equal(2, store.AllVideoClips.Count());
        // c was at 8, b duration = 3, so c should shift to 8-3=5
        Assert.Equal(5.0, c.TimelinePosition, 3);
    }

    [Fact]
    public void RippleDeleteClip_DoesNotShiftEarlierClips()
    {
        var store = CreateStore();
        var a = new VideoClip { Name = "a.mp4", Duration = 5, TimelinePosition = 0 };
        var b = new VideoClip { Name = "b.mp4", Duration = 3, TimelinePosition = 5 };
        store.AddClip(a); store.AddClip(b);

        store.RippleDeleteClip(b.Id);

        Assert.Equal(0.0, a.TimelinePosition, 3);
    }

    [Fact]
    public void RippleDeleteClip_SupportsUndo()
    {
        var store = CreateStore();
        var a = new VideoClip { Name = "a.mp4", Duration = 5, TimelinePosition = 0 };
        var b = new VideoClip { Name = "b.mp4", Duration = 3, TimelinePosition = 5 };
        var c = new VideoClip { Name = "c.mp4", Duration = 4, TimelinePosition = 8 };
        store.AddClip(a); store.AddClip(b); store.AddClip(c);

        store.RippleDeleteClip(b.Id);
        store.Undo();

        Assert.Equal(3, store.AllVideoClips.Count());
        Assert.Equal(8.0, c.TimelinePosition, 3);
    }

    [Fact]
    public void RippleDeleteClip_NoOpOnLockedTrack()
    {
        var store = CreateStore();
        var clip  = new VideoClip { Name = "a.mp4", Duration = 5, TimelinePosition = 0 };
        store.AddClip(clip);
        store.LockTrack(store.PrimaryVideoTrack.Id, true);

        store.RippleDeleteClip(clip.Id);

        Assert.Single(store.AllVideoClips);
    }

    [Fact]
    /// <summary>
    /// A ripple move is a lift and an insert: the clips after it close up, then the ones where it
    /// lands are pushed on.
    /// </summary>
    /// <remarks>
    /// <para>This used to assert that everything downstream moved by the <b>drag distance</b>, which
    /// is a different tool — "move this clip and everything after it" — and it fell apart the moment
    /// the drag went backwards: dragging a clip eighteen seconds earlier moved the clips behind it
    /// eighteen seconds earlier too, through zero and into negative time. The no-overlap assertion
    /// added on 2026-09-05 is what surfaced it.</para>
    ///
    /// <para>Here: a runs 0–5, b runs 7–10. Lifting a closes b up by a's own length, to 2. Dropping
    /// a at 3 then pushes b clear again, to 8 — so the two end up adjacent, which is what closing
    /// the gaps means.</para>
    /// </remarks>
    public void RippleCommitDraggedPosition_LiftsAndInserts()
    {
        var store = CreateStore();
        var a = new VideoClip { Name = "a.mp4", Duration = 5, TimelinePosition = 0 };
        var b = new VideoClip { Name = "b.mp4", Duration = 3, TimelinePosition = 7 };
        store.AddClip(a); store.AddClip(b);

        a.TimelinePosition = 3.0;
        store.RippleCommitDraggedPosition(a.Id, 0.0);

        Assert.Equal(3.0, a.TimelinePosition, 3);
        Assert.Equal(8.0, b.TimelinePosition, 3);
        Assert.Null(store.ValidateAll());
    }

    [Fact]
    public void RippleCommitDraggedPosition_SupportsUndo()
    {
        var store = CreateStore();
        var a = new VideoClip { Name = "a.mp4", Duration = 5, TimelinePosition = 0 };
        var b = new VideoClip { Name = "b.mp4", Duration = 3, TimelinePosition = 7 };
        store.AddClip(a); store.AddClip(b);

        a.TimelinePosition = 3.0;
        store.RippleCommitDraggedPosition(a.Id, 0.0);

        // Two steps now: the lift-and-move, and the room made where it landed.
        store.Undo();
        store.Undo();

        Assert.Equal(0.0,  a.TimelinePosition, 3);
        Assert.Equal(7.0,  b.TimelinePosition, 3);
    }

    // ── Overlay layer stacking (backlog #39) ────────────────────────────────
    // Every Callout/Text/ClipArt item gets its own LayerIndex, independent of
    // TimelinePosition — "everything added gets its own layer, each layer higher
    // than any added before it," regardless of where on the timeline it starts.

    [Fact]
    public void AddCallout_FirstOne_GetsLayerIndexZero()
    {
        var store  = CreateStore();
        var callout = new CalloutClip { Name = "c1" };

        store.AddCallout(callout);

        Assert.Equal(0, callout.LayerIndex);
    }

    [Fact]
    public void AddCallout_Second_GetsHigherLayerIndexThanFirst_EvenIfItStartsEarlier()
    {
        var store = CreateStore();
        var first  = new CalloutClip { Name = "c1", TimelinePosition = 10 };
        var second = new CalloutClip { Name = "c2", TimelinePosition = 0 }; // starts BEFORE c1
        store.AddCallout(first);

        store.AddCallout(second);

        Assert.True(second.LayerIndex > first.LayerIndex);
    }

    [Fact]
    public void AddCallout_TextOverlay_ClipArt_ShareOneLayerIndexSequence()
    {
        // Different overlay TYPES still stack in one shared sequence — a text overlay added
        // after a callout must render as a higher layer than that callout, not restart at 0.
        var store   = CreateStore(o => o.TextOverlays = true);
        var callout = new CalloutClip { Name = "c1" };
        var overlay = MakeOverlay("t1");
        var clipArt = new ClipArtClip { Name = "a1", AssetId = "asset-1" };

        store.AddCallout(callout);
        store.AddTextOverlay(overlay);
        store.AddClipArtClip(clipArt);

        Assert.Equal(0, callout.LayerIndex);
        Assert.Equal(1, overlay.LayerIndex);
        Assert.Equal(2, clipArt.LayerIndex);
    }

    [Fact]
    public void AddCallout_DoesNotAffectVideoClipLayerIndex()
    {
        // LayerIndex is meaningless for sequential items — confirms adding overlays never
        // touches it for VideoClip (stays at the record default, 0).
        var store = CreateStore();
        var video = new VideoClip { Name = "v.mp4", Duration = 5 };
        store.AddClip(video);

        store.AddCallout(new CalloutClip { Name = "c1" });
        store.AddCallout(new CalloutClip { Name = "c2" });

        Assert.Equal(0, video.LayerIndex);
    }

    [Fact]
    public void ReplaceFromProject_LegacyProjectWithNoLayerIndex_AssignsDistinctIndices()
    {
        // Older saved projects have no LayerIndex in their JSON at all — it deserializes to the
        // default 0 for every item, which would collapse every overlay onto the same stack row
        // (backlog #39 follow-on) unless NormalizeLayerIndices() (called from
        // ReplaceFromProject) fixes it up.
        var store = CreateStore(o => o.TextOverlays = true);
        var project = new ProjectFile
        {
            Tracks =
            [
                new ProjectTrack
                {
                    Type = TrackType.Video,
                    CalloutClips = [new ProjectCalloutClip { Id = Guid.NewGuid(), Order = 0 }],  // LayerIndex defaults to 0
                    TextOverlays = [new ProjectTextOverlay { Id = Guid.NewGuid(), Order = 1 }],  // LayerIndex defaults to 0 too
                },
            ],
        };

        store.ReplaceFromProject(project);

        var overlays = store.PrimaryVideoTrack.Items
            .Where(i => i is CalloutClip or TextOverlay)
            .ToList();
        Assert.Equal(2, overlays.Count);
        Assert.NotEqual(overlays[0].LayerIndex, overlays[1].LayerIndex);
    }

    [Fact]
    public void ReplaceFromProject_PreservesRelativeLayerOrder()
    {
        var store = CreateStore(o => o.TextOverlays = true);
        var project = new ProjectFile
        {
            Tracks =
            [
                new ProjectTrack
                {
                    Type = TrackType.Video,
                    CalloutClips = [new ProjectCalloutClip { Id = Guid.NewGuid(), Order = 0, LayerIndex = 5 }],
                    TextOverlays = [new ProjectTextOverlay { Id = Guid.NewGuid(), Order = 1, LayerIndex = 2 }],
                },
            ],
        };

        store.ReplaceFromProject(project);

        var callout = store.PrimaryVideoTrack.Items.OfType<CalloutClip>().Single();
        var overlay = store.PrimaryVideoTrack.Items.OfType<TextOverlay>().Single();
        Assert.True(callout.LayerIndex > overlay.LayerIndex); // 5 > 2 relationship preserved
    }
}
