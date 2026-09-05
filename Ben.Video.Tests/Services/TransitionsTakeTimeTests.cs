using Ben.Video.Editor.Models;
using Ben.Video.Editor.Services;
using Microsoft.Extensions.Options;

namespace Ben.Video.Tests.Services;

/// <summary>
/// A transition is time, not decoration.
/// </summary>
/// <remarks>
/// <para>A crossfade makes two clips play at once for its length, and ffmpeg's xfade output is
/// A + B − d. The store used to centre the transition on the junction and move nothing, so the
/// timeline claimed a length the render never produced: every marker, overlay and audio clip after
/// the junction sat later than whatever it had been lined up with on screen (2026-09-05 audit,
/// transitions-3).</para>
///
/// <para>Nothing checked the junction still existed either, so removing or splitting a clip left
/// the transition behind pointing at nothing, and export matched transitions to junctions by
/// position — applying it to whichever pair happened to be there (transitions-5).</para>
/// </remarks>
public sealed class TransitionsTakeTimeTests
{
    private static ClipStore Store() =>
        new(Options.Create(new VideoEditorOptions { Transitions = true, MultiTrack = true }));

    private static (ClipStore Store, TimelineTrack Track, VideoClip A, VideoClip B) TwoClips()
    {
        var store = Store();
        var track = store.Tracks[0];
        var a = new VideoClip { Name = "a", Duration = 5 };
        var b = new VideoClip { Name = "b", Duration = 5 };
        store.AddClipToTrack(track.Id, a);
        store.AddClipToTrack(track.Id, b);
        return (store, track, a, b);
    }

    [Fact]
    public void Adding_one_pulls_the_second_clip_back_by_its_length()
    {
        var (store, track, a, b) = TwoClips();
        Assert.Equal(5, b.TimelinePosition, 3);

        store.AddTransition(track.Id, a.Id, b.Id, TransitionStyle.Fade, 1.0);

        Assert.Equal(4, b.TimelinePosition, 3);
        Assert.Null(store.ValidateAll());
    }

    /// <summary>
    /// The whole point: the length the timeline reports is the length the render produces.
    /// </summary>
    [Fact]
    public void The_timeline_gets_shorter_by_exactly_the_crossfade()
    {
        var (store, track, a, b) = TwoClips();
        var before = track.TotalDuration;

        store.AddTransition(track.Id, a.Id, b.Id, TransitionStyle.Fade, 1.0);

        Assert.Equal(before - 1.0, track.TotalDuration, 3);
    }

    [Fact]
    public void Everything_after_the_junction_moves_with_it()
    {
        var (store, track, a, b) = TwoClips();
        var c = new VideoClip { Name = "c", Duration = 3 };
        store.AddClipToTrack(track.Id, c);
        Assert.Equal(10, c.TimelinePosition, 3);

        store.AddTransition(track.Id, a.Id, b.Id, TransitionStyle.Fade, 1.0);

        Assert.Equal(9, c.TimelinePosition, 3);
        Assert.Null(store.ValidateAll());
    }

    [Fact]
    public void Removing_it_gives_the_time_back()
    {
        var (store, track, a, b) = TwoClips();
        store.AddTransition(track.Id, a.Id, b.Id, TransitionStyle.Fade, 1.0);
        var transition = track.Items.OfType<Transition>().Single();

        store.RemoveTransition(transition.Id);

        Assert.Equal(5, b.TimelinePosition, 3);
        Assert.Empty(track.Items.OfType<Transition>());
        Assert.Null(store.ValidateAll());
    }

    [Fact]
    public void Lengthening_it_pulls_the_second_clip_further_back()
    {
        var (store, track, a, b) = TwoClips();
        store.AddTransition(track.Id, a.Id, b.Id, TransitionStyle.Fade, 1.0);
        var transition = track.Items.OfType<Transition>().Single();

        store.UpdateTransition(transition.Id, TransitionStyle.Dissolve, 2.0);

        Assert.Equal(3, b.TimelinePosition, 3);
        Assert.Equal(2.0, transition.Duration, 3);
        Assert.Null(store.ValidateAll());
    }

    /// <summary>
    /// A crossfade longer than the clips it joins is something ffmpeg has to invent.
    /// </summary>
    [Fact]
    public void It_is_never_longer_than_the_clips_can_spare()
    {
        var store = Store();
        var track = store.Tracks[0];
        var a = new VideoClip { Name = "a", Duration = 1 };
        var b = new VideoClip { Name = "b", Duration = 1 };
        store.AddClipToTrack(track.Id, a);
        store.AddClipToTrack(track.Id, b);

        store.AddTransition(track.Id, a.Id, b.Id, TransitionStyle.Fade, 5.0);

        var transition = track.Items.OfType<Transition>().Single();
        Assert.True(transition.Duration <= 1.0,
            $"A {transition.Duration}s crossfade between two one-second clips.");
        Assert.Null(store.ValidateAll());
    }

    /// <summary>The pair may overlap by the crossfade, and by nothing more.</summary>
    [Fact]
    public void The_allowed_overlap_is_exactly_the_transitions_length()
    {
        var (store, track, a, b) = TwoClips();
        store.AddTransition(track.Id, a.Id, b.Id, TransitionStyle.Fade, 1.0);

        Assert.Equal(1.0, TrackLayout.AllowedOverlap(track, a, b), 3);
        Assert.Null(TrackLayout.Validate(track));

        // One second more and the track is wrong again.
        b.TimelinePosition -= 1;
        Assert.NotNull(TrackLayout.Validate(track));
    }

    // ── Reconciling ───────────────────────────────────────────────────────────

    [Fact]
    public void A_transition_whose_clips_are_pulled_apart_is_dropped()
    {
        var (store, track, a, b) = TwoClips();
        store.AddTransition(track.Id, a.Id, b.Id, TransitionStyle.Fade, 1.0);
        Assert.Single(track.Items.OfType<Transition>());

        // Drag b well clear of a: there is no junction any more.
        var original = b.TimelinePosition;
        b.TimelinePosition = 30;
        store.CommitDraggedPosition(b.Id, original);

        Assert.Empty(track.Items.OfType<Transition>());
        Assert.Null(store.ValidateAll());
    }

    [Fact]
    public void A_transition_survives_an_edit_that_leaves_its_junction_alone()
    {
        var (store, track, a, b) = TwoClips();
        var c = new VideoClip { Name = "c", Duration = 3 };
        store.AddClipToTrack(track.Id, c);
        store.AddTransition(track.Id, a.Id, b.Id, TransitionStyle.Fade, 1.0);

        // Move the clip that has nothing to do with the junction.
        var original = c.TimelinePosition;
        c.TimelinePosition = 40;
        store.CommitDraggedPosition(c.Id, original);

        Assert.Single(track.Items.OfType<Transition>());
    }

    [Fact]
    public void Splitting_the_clip_a_transition_hands_off_to_drops_it()
    {
        var (store, track, a, b) = TwoClips();
        store.AddTransition(track.Id, a.Id, b.Id, TransitionStyle.Fade, 1.0);

        store.SplitClipAtTimelineTime(b.Id, b.TimelinePosition + 2);

        Assert.Empty(track.Items.OfType<Transition>());
        Assert.Null(store.ValidateAll());
    }
}
