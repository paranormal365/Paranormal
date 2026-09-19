using Ben.Video.Editor.Models;
using Ben.Video.Editor.Services;
using Microsoft.Extensions.Options;

namespace Ben.Video.Tests.Services;

/// <summary>
/// What an edit destroys, undo has to give back.
/// </summary>
/// <remarks>
/// Splitting a clip a transition started from stranded that transition, and reconciliation removed
/// it straight off the track's list — no command, so the wipe a person had chosen was gone for
/// good. Worse, the overlap it had opened was closed by its own separate undo step, so undoing the
/// split left two clips overlapping by a second with nothing to justify the overlap: a second of
/// footage silently unseen, in a state no edit could otherwise produce (2026-09-18 audit).
/// </remarks>
public sealed class SplitKeepsTheHistoryHonestTests
{
    private static (ClipStore Store, TimelineTrack Track, VideoClip A, VideoClip B) TwoClipsAndATransition()
    {
        var store = new ClipStore(Options.Create(new VideoEditorOptions { Transitions = true, MultiTrack = true }));
        var track = store.Tracks[0];
        var a = new VideoClip { Name = "a", Duration = 5 };
        var b = new VideoClip { Name = "b", Duration = 5 };
        store.AddClipToTrack(track.Id, a);
        store.AddClipToTrack(track.Id, b);
        store.AddTransition(track.Id, a.Id, b.Id, TransitionStyle.WipeLeft, 1.0);
        return (store, track, a, b);
    }

    /// <summary>
    /// Undoes back to the state the fixture left behind — every step the split itself pushed, and
    /// no further. "Split" is the last of them, so stopping once it has been undone is exactly the
    /// one Ctrl+Z a person would expect to have to press, repeated until the edit is gone.
    /// </summary>
    private static void UndoTheSplit(ClipStore store)
    {
        for (var guard = 0; guard < 20 && store.CanUndo; guard++)
        {
            var wasTheSplit = store.UndoDescription?.StartsWith("Split", StringComparison.Ordinal) == true;
            store.Undo();
            if (wasTheSplit) return;
        }

        Assert.Fail("never reached the split while undoing");
    }

    [Fact]
    public void Splitting_a_clip_strands_its_transition_as_it_always_did()
    {
        var (store, track, a, _) = TwoClipsAndATransition();
        Assert.Single(track.Items.OfType<Transition>());

        store.SplitClipAtTimelineTime(a.Id, 2.0);

        Assert.Empty(track.Items.OfType<Transition>());
    }

    [Fact]
    public void Undoing_the_split_brings_the_transition_back()
    {
        var (store, track, a, _) = TwoClipsAndATransition();
        var styleChosen = track.Items.OfType<Transition>().Single().Style;

        store.SplitClipAtTimelineTime(a.Id, 2.0);
        UndoTheSplit(store);

        var restored = Assert.Single(track.Items.OfType<Transition>());
        Assert.Equal(styleChosen, restored.Style);
    }

    [Fact]
    public void Undoing_the_split_leaves_no_overlap_without_a_transition_to_justify_it()
    {
        var (store, track, a, b) = TwoClipsAndATransition();

        store.SplitClipAtTimelineTime(a.Id, 2.0);
        UndoTheSplit(store);

        var clips = track.Items.OfType<VideoClip>().OrderBy(c => c.TimelinePosition).ToList();
        Assert.Equal(2, clips.Count);

        var overlap = clips[0].TimelinePosition + clips[0].TrimmedDuration - clips[1].TimelinePosition;
        var transition = track.Items.OfType<Transition>().SingleOrDefault();

        if (overlap > 0.05)
            Assert.True(transition is not null,
                $"the clips overlap by {overlap:F2}s with no transition to account for it — " +
                "a second of footage nobody can see and nothing on screen explains.");
    }

    [Fact]
    public void Undo_puts_the_whole_timeline_back_where_it_started()
    {
        var (store, track, a, b) = TwoClipsAndATransition();
        var before = track.Items.Select(i => (i.Name, Math.Round(i.TimelinePosition, 3))).ToList();

        store.SplitClipAtTimelineTime(a.Id, 2.0);
        UndoTheSplit(store);

        var after = track.Items.Select(i => (i.Name, Math.Round(i.TimelinePosition, 3))).ToList();
        Assert.Equal(before, after);
    }
}
