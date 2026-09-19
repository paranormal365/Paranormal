using Ben.Video.Editor.Models;
using Ben.Video.Editor.Services;

namespace Ben.Video.Tests.Services;

/// <summary>
/// The rule a track has to obey: its clips run one after another, and none of them overlaps.
/// </summary>
/// <remarks>
/// Nothing said this before, so nothing could enforce it. See <see cref="TrackLayout"/> for how the
/// overlaps were both possible and invisible (2026-09-05 audit, F5).
/// </remarks>
public sealed class TrackLayoutTests
{
    private static TimelineTrack Track(params TrackItem[] items)
    {
        var track = new TimelineTrack { Label = "Video 1", Type = TrackType.Video };
        track.Items.AddRange(items);
        return track;
    }

    private static VideoClip Clip(double position, double duration, string name = "clip")
        => new() { Name = name, TimelinePosition = position, Duration = duration };

    // ── What counts ───────────────────────────────────────────────────────────

    [Fact]
    public void Video_audio_and_image_clips_hold_a_place_in_time()
    {
        Assert.True(TrackLayout.IsSequential(new VideoClip()));
        Assert.True(TrackLayout.IsSequential(new AudioClip()));
        Assert.True(TrackLayout.IsSequential(new ImageClip()));
    }

    /// <summary>
    /// Overlays are meant to sit on top of the picture, and a transition belongs to the junction
    /// between two clips. Neither competes for a place in the lane.
    /// </summary>
    [Fact]
    public void Overlays_and_transitions_do_not()
    {
        Assert.False(TrackLayout.IsSequential(new CalloutClip()));
        Assert.False(TrackLayout.IsSequential(new TextOverlay()));
        Assert.False(TrackLayout.IsSequential(new ClipArtClip()));
        Assert.False(TrackLayout.IsSequential(new Transition()));
    }

    // ── Overlap ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0, 5, true)]      // straight on top
    [InlineData(4, 5, true)]      // overlapping the tail
    [InlineData(-2, 5, true)]     // overlapping the head
    [InlineData(5, 5, false)]     // touching, after
    [InlineData(11, 5, false)]    // clear of it
    public void Overlaps_answers_for_a_span(double position, double duration, bool expected)
    {
        var track = Track(Clip(position: 0, duration: 5, name: "existing"));

        Assert.Equal(expected, TrackLayout.Overlaps(track, position, duration));
    }

    [Fact]
    public void An_item_does_not_overlap_itself()
    {
        var moving = Clip(position: 0, duration: 5, name: "moving");
        var track  = Track(moving);

        Assert.False(TrackLayout.Overlaps(track, 1, 5, excludeItemId: moving.Id));
        Assert.True(TrackLayout.Overlaps(track, 1, 5));
    }

    /// <summary>
    /// Adjacent clips touch: one ends exactly where the next begins. Pixel arithmetic rarely lands
    /// on the same double twice, so "touching" has to survive a rounding error.
    /// </summary>
    [Fact]
    public void Touching_is_not_overlapping_even_a_fraction_out()
    {
        var track = Track(Clip(position: 0, duration: 5));

        Assert.False(TrackLayout.Overlaps(track, 5.0000004, 3));
        Assert.False(TrackLayout.Overlaps(track, 4.9999996, 3));
    }

    [Fact]
    public void An_audio_clips_trims_decide_how_much_room_it_takes()
    {
        // A three-minute file trimmed to ten seconds occupies ten.
        var audio = new AudioClip { Name = "music", Duration = 186, StartTrim = 20, EndTrim = 30 };
        var track = Track(audio);

        Assert.False(TrackLayout.Overlaps(track, 10, 5));
        Assert.True(TrackLayout.Overlaps(track, 9, 5));
    }

    [Fact]
    public void FirstOverlapping_names_what_is_in_the_way()
    {
        var track = Track(
            Clip(position: 0, duration: 5, name: "first"),
            Clip(position: 5, duration: 5, name: "second"));

        var blocker = TrackLayout.FirstOverlapping(track, 6, 2);

        Assert.Equal("second", blocker?.Name);
    }

    [Fact]
    public void EndOf_is_the_far_edge_of_the_last_clip()
    {
        var track = Track(Clip(0, 5), Clip(8, 4));

        Assert.Equal(12, TrackLayout.EndOf(track));
        Assert.Equal(0, TrackLayout.EndOf(Track()));
    }

    // ── Validate ──────────────────────────────────────────────────────────────

    [Fact]
    public void A_track_in_order_with_a_gap_is_fine()
    {
        var track = Track(Clip(0, 5, "a"), Clip(9, 3, "b"));

        Assert.Null(TrackLayout.Validate(track));
    }

    [Fact]
    public void An_overlap_is_reported_with_both_names_and_the_times()
    {
        var track = Track(Clip(0, 5, "meteor"), Clip(3, 4, "monk"));

        var problem = TrackLayout.Validate(track);

        Assert.NotNull(problem);
        Assert.Contains("monk", problem);
        Assert.Contains("meteor", problem);
    }

    /// <summary>
    /// Order in the list is not the order in time; the check has to sort first. This is the exact
    /// shape a drag used to leave behind — a clip moved in front of the one before it.
    /// </summary>
    [Fact]
    public void The_check_reads_the_track_in_time_order_not_list_order()
    {
        var track = Track(Clip(10, 5, "later"), Clip(0, 5, "earlier"));

        Assert.Null(TrackLayout.Validate(track));
    }

    /// <summary>
    /// A clip is added the moment the file is picked and gains its duration when the probe comes
    /// back, so a length of zero is a normal in-between state rather than a broken track.
    /// </summary>
    [Fact]
    public void A_clip_that_has_no_duration_yet_is_not_a_problem()
    {
        var track = Track(Clip(0, 0, "still probing"), Clip(0, 5, "ready"));

        Assert.Null(TrackLayout.Validate(track));
    }

    [Fact]
    public void A_clip_before_the_beginning_is_reported()
    {
        var track = Track(Clip(-3, 5, "adrift"));

        Assert.Contains("before the beginning", TrackLayout.Validate(track));
    }

    [Fact]
    public void Overlays_never_make_a_track_invalid()
    {
        var track = Track(
            Clip(0, 5, "clip"),
            new CalloutClip { Name = "callout", TimelinePosition = 1, Duration = 5 },
            new TextOverlay { Name = "title",   TimelinePosition = 2, Duration = 5 });

        Assert.Null(TrackLayout.Validate(track));
    }
}
