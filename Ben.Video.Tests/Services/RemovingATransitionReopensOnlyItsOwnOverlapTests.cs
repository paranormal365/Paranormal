using Ben.Video.Editor.Models;
using Ben.Video.Editor.Services;
using Microsoft.Extensions.Options;

namespace Ben.Video.Tests.Services;

/// <summary>
/// Removing a transition puts back the second the two clips were sharing — but only when it is
/// still the one holding them together.
/// </summary>
/// <remarks>
/// RemoveTransition checked only that the FOLLOWING clip still existed before shifting everything
/// after it later by the transition's length. For a transition whose PRECEDING clip had gone, that
/// overlap had already been closed, so the shift was pure loss: the surviving clip walked a second
/// to the right and the finished video opened on a second of black. Seen on the running editor
/// 2026-09-18, clearing a stray transition left behind by an older delete.
/// </remarks>
public sealed class RemovingATransitionReopensOnlyItsOwnOverlapTests
{
    private static (ClipStore Store, TimelineTrack Track, VideoClip A, VideoClip B) Joined()
    {
        var store = new ClipStore(Options.Create(new VideoEditorOptions { Transitions = true, MultiTrack = true }));
        var track = store.Tracks[0];
        var a = new VideoClip { Name = "a", Duration = 3 };
        var b = new VideoClip { Name = "b", Duration = 4 };
        store.AddClipToTrack(track.Id, a);
        store.AddClipToTrack(track.Id, b);
        store.AddTransition(track.Id, a.Id, b.Id, TransitionStyle.CircleOpen, 1.0);
        return (store, track, a, b);
    }

    [Fact]
    public void Removing_a_real_transition_gives_the_shared_second_back()
    {
        var (store, track, a, b) = Joined();
        Assert.Equal(2.0, b.TimelinePosition, 3);   // pulled back by the transition

        store.RemoveTransition(track.Items.OfType<Transition>().Single().Id);

        Assert.Equal(0.0, a.TimelinePosition, 3);
        Assert.Equal(3.0, b.TimelinePosition, 3);   // back to meeting a's end
    }

    [Fact]
    public void Removing_a_stray_transition_does_not_push_the_surviving_clip_along()
    {
        // The state an older delete used to leave: the transition's first clip is gone, its
        // overlap was closed with it, and the second clip is sitting at zero.
        var (store, track, a, b) = Joined();
        var stray = track.Items.OfType<Transition>().Single();
        track.Items.Remove(a);              // straight off the list, the way the old delete did
        b.TimelinePosition = 0;

        store.RemoveTransition(stray.Id);

        Assert.Equal(0.0, b.TimelinePosition, 3);
    }

    [Fact]
    public void The_finished_video_does_not_open_on_black()
    {
        var (store, track, a, b) = Joined();
        var stray = track.Items.OfType<Transition>().Single();
        track.Items.Remove(a);
        b.TimelinePosition = 0;

        store.RemoveTransition(stray.Id);

        var first = track.Items.OfType<VideoClip>().OrderBy(c => c.TimelinePosition).First();
        Assert.True(first.TimelinePosition <= 0.05,
            $"the video starts with {first.TimelinePosition:F2}s of nothing before the first clip.");
    }
}
