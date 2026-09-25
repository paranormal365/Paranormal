using Ben.Video.Editor.Models;
using Ben.Video.Editor.Services;
using Microsoft.Extensions.Options;

namespace Ben.Video.Tests.Services;

/// <summary>
/// A transition names two clips and sits on the junction between them. When that junction stops
/// existing, the transition has to go with it.
/// </summary>
/// <remarks>
/// The 2026-09-05 audit (transitions-5) established this and wrote it into
/// <c>ReconcileTransitions</c>, whose own remarks say it is "called after every edit that can move
/// a clip". It was called from three places: two drag commits and a split. Deleting a clip — the
/// plainest way there is to destroy a junction — was not one of them, so ripple-deleting the first
/// of two joined clips left a one-second "Circle open" sitting at 0.0s on a track with a single
/// clip on it, measured on the running editor 2026-09-18. That is the exact state transitions-5
/// existed to prevent, because export matches transitions to junctions by position and applies
/// them to whichever pair happens to be there.
/// </remarks>
public sealed class NoTransitionOutlivesItsJunctionTests
{
    private static (ClipStore Store, TimelineTrack Track, VideoClip A, VideoClip B) Joined()
    {
        var store = new ClipStore(Options.Create(new VideoEditorOptions { Transitions = true, MultiTrack = true }));
        var track = store.Tracks[0];
        var a = new VideoClip { Name = "a", Duration = 5 };
        var b = new VideoClip { Name = "b", Duration = 5 };
        store.AddClipToTrack(track.Id, a);
        store.AddClipToTrack(track.Id, b);
        store.AddTransition(track.Id, a.Id, b.Id, TransitionStyle.CircleOpen, 1.0);
        return (store, track, a, b);
    }

    [Fact]
    public void Ripple_deleting_the_first_clip_takes_the_transition_with_it()
    {
        var (store, track, a, _) = Joined();

        store.RippleDeleteClip(a.Id);

        Assert.Empty(track.Items.OfType<Transition>());
    }

    [Fact]
    public void Ripple_deleting_the_second_clip_takes_the_transition_with_it()
    {
        var (store, track, _, b) = Joined();

        store.RippleDeleteClip(b.Id);

        Assert.Empty(track.Items.OfType<Transition>());
    }

    [Fact]
    public void Removing_the_first_clip_takes_the_transition_with_it()
    {
        var (store, track, a, _) = Joined();

        store.RemoveClip(a.Id);

        Assert.Empty(track.Items.OfType<Transition>());
    }

    [Fact]
    public void Removing_the_second_clip_takes_the_transition_with_it()
    {
        var (store, track, _, b) = Joined();

        store.RemoveClip(b.Id);

        Assert.Empty(track.Items.OfType<Transition>());
    }

    [Fact]
    public void A_transition_never_outlives_the_last_clip_on_the_track()
    {
        var (store, track, a, b) = Joined();

        store.RemoveClip(a.Id);
        store.RemoveClip(b.Id);

        Assert.Empty(track.Items);
    }

    [Fact]
    public void Trimming_the_first_clip_shorter_takes_the_transition_with_it()
    {
        // The junction walks back to 3.0s while the second clip stays at 4.0s, leaving the
        // transition floating in a one-second gap that joins nothing.
        var (store, track, a, _) = Joined();

        store.UpdateTrim(a.Id, 0, 3);

        Assert.Empty(track.Items.OfType<Transition>());
    }

    [Fact]
    public void Trimming_by_the_clips_own_edge_takes_it_too()
    {
        var (store, track, a, _) = Joined();
        a.EndTrim = 3;

        store.CommitTrim(a.Id, originalStart: 0, originalEnd: 5);

        Assert.Empty(track.Items.OfType<Transition>());
    }

    [Fact]
    public void Nudging_the_second_clip_away_takes_the_transition_with_it()
    {
        // Dragging a clip already reconciled; nudging it with the keyboard landed in the same
        // place and did not.
        var (store, track, _, b) = Joined();

        store.MoveClip(b.Id, 3.0);

        Assert.Empty(track.Items.OfType<Transition>());
    }

    [Fact]
    public void Nothing_is_left_sitting_in_a_gap_that_joins_nothing()
    {
        // The shape of every failure above, stated once: whatever survives an edit must still lie
        // between the two clips it names.
        var (store, track, a, b) = Joined();

        store.UpdateTrim(a.Id, 0, 3);

        foreach (var t in track.Items.OfType<Transition>())
        {
            var from = track.Items.FirstOrDefault(i => i.Id == t.FromClipId);
            var to   = track.Items.FirstOrDefault(i => i.Id == t.ToClipId);
            Assert.True(from is not null && to is not null, "a transition naming a clip that is gone");

            var junction = from!.TimelinePosition + from.EffectiveLength - t.Duration;
            Assert.True(Math.Abs(to!.TimelinePosition - junction) <= 0.05,
                $"{t.Name} sits at {t.TimelinePosition:F2} but its clips no longer meet there");
        }
    }

    [Fact]
    public void Deleting_a_clip_that_has_nothing_to_do_with_it_leaves_the_transition_alone()
    {
        var (store, track, a, b) = Joined();
        var c = new VideoClip { Name = "c", Duration = 5 };
        store.AddClipToTrack(track.Id, c);

        store.RemoveClip(c.Id);

        Assert.Single(track.Items.OfType<Transition>());
    }
}
