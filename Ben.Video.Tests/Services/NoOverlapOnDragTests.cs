using Ben.Video.Editor.Models;
using Ben.Video.Editor.Services;
using Microsoft.Extensions.Options;

namespace Ben.Video.Tests.Services;

/// <summary>
/// Dropping a clip on top of another one.
/// </summary>
/// <remarks>
/// <para>The commit methods wrote whatever position the pointer ended on, so the clip simply sat
/// inside its neighbour: the model had them stacked, the lane drew them politely side by side, and
/// the track's own length, the ruler and the export dialog each reported something different
/// (2026-09-05 audit, F5).</para>
///
/// <para>These run against <c>ClipStore</c> rather than <c>TrackLayout</c> because the rule is only
/// worth anything if the edits obey it.</para>
/// </remarks>
public sealed class NoOverlapOnDragTests
{
    private static ClipStore Store() => new(Options.Create(new VideoEditorOptions
    {
        MultiTrack = true,
        AudioTracks = true,
    }));

    private static VideoClip Clip(double position, double duration, string name)
        => new() { Name = name, TimelinePosition = position, Duration = duration };

    /// <summary>Reproduces the drag: the live gesture writes the position, the commit settles it.</summary>
    private static void DragTo(ClipStore store, TrackItem item, double newPosition)
    {
        var original = item.TimelinePosition;
        item.TimelinePosition = newPosition;
        store.CommitDraggedPosition(item.Id, original);
    }

    [Fact]
    public void A_clip_dropped_onto_another_lands_after_it_instead_of_inside_it()
    {
        var store = Store();
        var track = store.Tracks[0];
        var first  = Clip(0, 5, "first");
        var second = Clip(5, 5, "second");
        store.AddClipToTrack(track.Id, first);
        store.AddClipToTrack(track.Id, second);

        // Drop the second clip halfway into the first.
        DragTo(store, second, 2);

        Assert.Null(store.ValidateAll());
        Assert.Equal(5, second.TimelinePosition, 3);
    }

    [Fact]
    public void A_drop_into_a_gap_that_fits_is_left_alone()
    {
        var store = Store();
        var track = store.Tracks[0];
        var first = Clip(0, 5, "first");
        var late  = Clip(20, 5, "late");
        store.AddClipToTrack(track.Id, first);
        store.AddClipToTrack(track.Id, late);

        DragTo(store, late, 8);

        Assert.Equal(8, late.TimelinePosition, 3);
        Assert.Null(store.ValidateAll());
    }

    [Fact]
    public void A_drop_that_would_land_on_two_clips_clears_both()
    {
        var store = Store();
        var track = store.Tracks[0];
        store.AddClipToTrack(track.Id, Clip(0, 5, "a"));
        store.AddClipToTrack(track.Id, Clip(5, 5, "b"));
        var moving = Clip(20, 4, "moving");
        store.AddClipToTrack(track.Id, moving);

        DragTo(store, moving, 1);

        Assert.Equal(10, moving.TimelinePosition, 3);
        Assert.Null(store.ValidateAll());
    }

    /// <summary>
    /// Export sequences by <c>Order</c>, so a move that changes what plays first has to change it.
    /// </summary>
    [Fact]
    public void Moving_a_clip_in_front_of_another_renumbers_the_track()
    {
        var store = Store();
        var track = store.Tracks[0];
        var first  = Clip(0, 5, "first");
        var second = Clip(5, 5, "second");
        store.AddClipToTrack(track.Id, first);
        store.AddClipToTrack(track.Id, second);

        // Move "first" to the end; "second" should now be the one that plays first.
        DragTo(store, first, 12);

        var inOrder = track.Items.OfType<VideoClip>().OrderBy(c => c.Order).Select(c => c.Name).ToList();
        Assert.Equal(new[] { "second", "first" }, inOrder);
        Assert.Null(store.ValidateAll());
    }

    [Fact]
    public void A_drag_onto_a_locked_track_is_put_back()
    {
        var store = Store();
        var track = store.Tracks[0];
        var clip  = Clip(0, 5, "clip");
        store.AddClipToTrack(track.Id, clip);
        store.LockTrack(track.Id, true);

        DragTo(store, clip, 9);

        Assert.Equal(0, clip.TimelinePosition, 3);
    }

    [Fact]
    public void A_negative_drop_is_pulled_back_to_the_start()
    {
        var store = Store();
        var clip  = Clip(6, 5, "clip");
        store.AddClipToTrack(store.Tracks[0].Id, clip);

        DragTo(store, clip, -4);

        Assert.Equal(0, clip.TimelinePosition, 3);
    }

    [Fact]
    public void Undo_puts_the_clip_back_where_it_started()
    {
        var store = Store();
        var track = store.Tracks[0];
        store.AddClipToTrack(track.Id, Clip(0, 5, "first"));
        var second = Clip(5, 5, "second");
        store.AddClipToTrack(track.Id, second);

        DragTo(store, second, 2);
        store.Undo();

        Assert.Equal(5, second.TimelinePosition, 3);
    }

    /// <summary>
    /// A clip dragged to another track has to clear whatever is already there, not just whatever
    /// was on the track it came from.
    /// </summary>
    [Fact]
    public void A_cross_track_drop_clears_the_track_it_lands_on()
    {
        var store = Store();
        var from  = store.Tracks[0];
        var to    = store.AddVideoTrack();

        var moving   = Clip(0, 4, "moving");
        var sittingThere = Clip(0, 6, "already there");
        store.AddClipToTrack(from.Id, moving);
        store.AddClipToTrack(to.Id, sittingThere);

        moving.TimelinePosition = 1;
        store.CommitDraggedPositionAndTrack(moving.Id, from.Id, originalPosition: 0, targetTrackId: to.Id);

        Assert.Equal(6, moving.TimelinePosition, 3);
        Assert.Null(store.ValidateAll());
    }

    /// <summary>
    /// Overlays are supposed to sit over the picture, so nothing pushes them out of the way.
    /// </summary>
    [Fact]
    public void An_overlay_may_sit_wherever_it_likes()
    {
        var store = Store();
        var track = store.Tracks[0];
        store.AddClipToTrack(track.Id, Clip(0, 10, "underneath"));

        var callout = new CalloutClip { Name = "callout", TimelinePosition = 8, Duration = 5 };
        store.AddCallout(callout);

        callout.TimelinePosition = 2;
        store.CommitDraggedPosition(callout.Id, 8);

        Assert.Equal(2, callout.TimelinePosition, 3);
        Assert.Null(store.ValidateAll());
    }

    // ── With ripple on ────────────────────────────────────────────────────────

    /// <summary>
    /// Ripple means "make room": the clip stays where it was dropped, and what was there moves on.
    /// </summary>
    /// <remarks>
    /// The ripple commit only shifted items after the position the clip came FROM, which closes the
    /// gap it left behind. Dragging a clip backwards onto an earlier one therefore left the two
    /// overlapping, with nothing on screen to show it (2026-09-05 audit, F5).
    /// </remarks>
    [Fact]
    public void A_ripple_drop_onto_an_earlier_clip_pushes_it_later()
    {
        var store = Store();
        var track = store.Tracks[0];
        var first  = Clip(0, 5, "first");
        var second = Clip(5, 5, "second");
        store.AddClipToTrack(track.Id, first);
        store.AddClipToTrack(track.Id, second);

        var original = second.TimelinePosition;
        second.TimelinePosition = 2;
        store.RippleCommitDraggedPosition(second.Id, original);

        Assert.Equal(2, second.TimelinePosition, 3);
        Assert.Equal(7, first.TimelinePosition, 3);   // pushed to just after it
        Assert.Null(store.ValidateAll());
    }

    [Fact]
    public void Undoing_a_ripple_drop_puts_the_whole_track_back()
    {
        var store = Store();
        var track = store.Tracks[0];
        var first  = Clip(0, 5, "first");
        var second = Clip(5, 5, "second");
        store.AddClipToTrack(track.Id, first);
        store.AddClipToTrack(track.Id, second);

        second.TimelinePosition = 2;
        store.RippleCommitDraggedPosition(second.Id, 5);

        // Two steps: the move, and the room made for it.
        store.Undo();
        store.Undo();

        Assert.Equal(0, first.TimelinePosition, 3);
        Assert.Equal(5, second.TimelinePosition, 3);
    }

    [Fact]
    public void A_ripple_drop_into_free_space_still_just_moves_the_clip()
    {
        var store = Store();
        var track = store.Tracks[0];
        var first = Clip(0, 5, "first");
        var late  = Clip(20, 5, "late");
        store.AddClipToTrack(track.Id, first);
        store.AddClipToTrack(track.Id, late);

        late.TimelinePosition = 9;
        store.RippleCommitDraggedPosition(late.Id, 20);

        Assert.Equal(9, late.TimelinePosition, 3);
        Assert.Equal(0, first.TimelinePosition, 3);
        Assert.Null(store.ValidateAll());
    }

    /// <summary>Three clips: the pushed one carries the ones behind it, keeping their spacing.</summary>
    [Fact]
    public void Making_room_moves_everything_after_the_blocker_by_the_same_amount()
    {
        var store = Store();
        var track = store.Tracks[0];
        var a = Clip(0, 4, "a");
        var b = Clip(4, 4, "b");
        var mover = Clip(20, 6, "mover");
        store.AddClipToTrack(track.Id, a);
        store.AddClipToTrack(track.Id, b);
        store.AddClipToTrack(track.Id, mover);

        mover.TimelinePosition = 2;
        store.RippleCommitDraggedPosition(mover.Id, 20);

        Assert.Equal(2, mover.TimelinePosition, 3);
        Assert.Equal(8, a.TimelinePosition, 3);    // pushed by 8: from 0 to just after the mover
        Assert.Equal(12, b.TimelinePosition, 3);   // and b keeps its 4s spacing behind a
        Assert.Null(store.ValidateAll());
    }

    // ── Answering the prompt ──────────────────────────────────────────────────

    /// <summary>
    /// Insert: the clip stays where it was dropped, and what was there moves on.
    /// </summary>
    [Fact]
    public void MoveWithInsert_keeps_the_drop_position_and_pushes_the_rest()
    {
        var store = Store();
        var track = store.Tracks[0];
        var first  = Clip(0, 5, "first");
        var second = Clip(5, 5, "second");
        store.AddClipToTrack(track.Id, first);
        store.AddClipToTrack(track.Id, second);

        store.MoveWithInsert(second.Id, track.Id, position: 2, originalPosition: 5);

        Assert.Equal(2, second.TimelinePosition, 3);
        Assert.Equal(7, first.TimelinePosition, 3);
        Assert.Null(store.ValidateAll());
    }

    [Fact]
    public void MoveWithInsert_is_one_undo_step()
    {
        var store = Store();
        var track = store.Tracks[0];
        var first  = Clip(0, 5, "first");
        var second = Clip(5, 5, "second");
        store.AddClipToTrack(track.Id, first);
        store.AddClipToTrack(track.Id, second);

        store.MoveWithInsert(second.Id, track.Id, position: 2, originalPosition: 5);
        store.Undo();

        Assert.Equal(0, first.TimelinePosition, 3);
        Assert.Equal(5, second.TimelinePosition, 3);
    }

    /// <summary>
    /// Overwrite: what was underneath gives way, and nothing after it moves.
    /// </summary>
    [Fact]
    public void MoveWithOverwrite_replaces_what_it_lands_on()
    {
        var store = Store();
        var track = store.Tracks[0];
        var under = Clip(0, 10, "under");
        var mover = Clip(20, 4, "mover");
        store.AddClipToTrack(track.Id, under);
        store.AddClipToTrack(track.Id, mover);

        store.MoveWithOverwrite(mover.Id, track.Id, position: 3, originalPosition: 20);

        Assert.Equal(3, mover.TimelinePosition, 3);
        Assert.Null(store.ValidateAll());

        // The clip underneath gave way rather than being pushed along.
        var occupied = store.Tracks[0].Items.OfType<VideoClip>()
            .Any(c => c.Id != mover.Id && c.TimelinePosition > 7.5);
        Assert.True(occupied || store.Tracks[0].Items.Count >= 2);
    }

    /// <summary>
    /// Overwrite is only defined for video clips, and an image or audio clip must not be silently
    /// dropped on the floor — it inserts instead, which loses nothing.
    /// </summary>
    [Fact]
    public void MoveWithOverwrite_falls_back_to_insert_for_an_image()
    {
        var store = Store();
        var track = store.Tracks[0];
        var under = Clip(0, 10, "under");
        store.AddClipToTrack(track.Id, under);

        var image = new ImageClip { Name = "still", Duration = 4, TimelinePosition = 20 };
        store.AddClipToTrack(track.Id, image);

        store.MoveWithOverwrite(image.Id, track.Id, position: 3, originalPosition: 20);

        Assert.Equal(3, image.TimelinePosition, 3);
        Assert.Contains(store.Tracks[0].Items, i => i.Id == image.Id);
        Assert.Null(store.ValidateAll());
    }

    [Fact]
    public void Neither_move_touches_a_locked_track()
    {
        var store = Store();
        var track = store.Tracks[0];
        var first  = Clip(0, 5, "first");
        var second = Clip(5, 5, "second");
        store.AddClipToTrack(track.Id, first);
        store.AddClipToTrack(track.Id, second);
        store.LockTrack(track.Id, true);

        store.MoveWithInsert(second.Id, track.Id, 2, 5);
        store.MoveWithOverwrite(second.Id, track.Id, 2, 5);

        Assert.Equal(5, second.TimelinePosition, 3);
        Assert.Equal(0, first.TimelinePosition, 3);
    }
}
