using Ben.Video.Editor.Models;
using Ben.Video.Editor.Services;

namespace Ben.Video.Tests.Services;

/// <summary>
/// Writing the timeline out as something a player can follow on its own.
/// </summary>
/// <remarks>
/// A player running at sixty frames a second cannot ask .NET what to show on each one, so the
/// timeline is resolved once into a list and rebuilt when it changes (decision D5).
/// </remarks>
public sealed class LivePlaybackPlanTests
{
    private static VideoClip Video(
        double position, double duration, double startTrim = 0, double endTrim = 0,
        double speed = 1.0, double volume = 1.0, bool muted = false) =>
        new()
        {
            Name = "clip", TimelinePosition = position, Duration = duration,
            StartTrim = startTrim, EndTrim = endTrim, Speed = speed,
            Volume = volume, MuteAudio = muted,
        };

    private static ImageClip Image(double position, double duration) =>
        new() { Name = "still", TimelinePosition = position, Duration = duration };

    private static AudioClip Audio(
        double position, double duration, double startTrim = 0, double volume = 1.0, bool muted = false) =>
        new()
        {
            Name = "sound", TimelinePosition = position, Duration = duration,
            StartTrim = startTrim, Volume = volume, MuteAudio = muted,
        };

    private static TimelineTrack Track(TrackType type, int order, params TrackItem[] items)
    {
        var track = new TimelineTrack { Type = type, Order = order };
        track.Items.AddRange(items);
        return track;
    }

    private static TimelineTrack VideoTrack(params TrackItem[] items) => Track(TrackType.Video, 0, items);
    private static TimelineTrack AudioTrack(params TrackItem[] items) => Track(TrackType.Audio, 1, items);

    // ── The picture ───────────────────────────────────────────────────────────

    [Fact]
    public void A_timeline_with_nothing_on_it_has_nothing_to_play()
    {
        Assert.True(LivePlaybackPlan.Build([]).IsEmpty);
        Assert.True(LivePlaybackPlan.Build(null).IsEmpty);
    }

    [Fact]
    public void Two_clips_become_two_stretches()
    {
        var plan = LivePlaybackPlan.Build([VideoTrack(Video(0, 5), Video(5, 5))]);

        Assert.Equal(2, plan.Picture.Count);
        Assert.Equal(0, plan.Picture[0].Start);
        Assert.Equal(5, plan.Picture[0].End);
        Assert.Equal(5, plan.Picture[1].Start);
        Assert.Equal(10, plan.Picture[1].End);
    }

    /// <summary>
    /// Black is written out rather than left as a hole, so the player never has to decide what to
    /// do with a moment the plan does not mention.
    /// </summary>
    [Fact]
    public void A_gap_between_clips_is_written_out()
    {
        var plan = LivePlaybackPlan.Build([VideoTrack(Video(0, 2), Video(6, 2))]);

        Assert.Equal(3, plan.Picture.Count);
        Assert.Equal(LiveSegmentKind.Gap, plan.Picture[1].Kind);
        Assert.Equal(2, plan.Picture[1].Start);
        Assert.Equal(6, plan.Picture[1].End);
    }

    [Fact]
    public void A_timeline_that_starts_late_begins_with_black()
    {
        var plan = LivePlaybackPlan.Build([VideoTrack(Video(3, 2))]);

        Assert.Equal(LiveSegmentKind.Gap, plan.Picture[0].Kind);
        Assert.Equal(0, plan.Picture[0].Start);
        Assert.Equal(3, plan.Picture[0].End);
    }

    /// <summary>
    /// The stretches cover the timeline end to end, which is what lets the player find the current
    /// one by a single comparison.
    /// </summary>
    [Fact]
    public void The_picture_has_no_holes_in_it()
    {
        var plan = LivePlaybackPlan.Build([VideoTrack(Video(1, 2), Image(5, 3), Video(9, 1))]);

        var cursor = 0.0;
        foreach (var segment in plan.Picture)
        {
            Assert.Equal(cursor, segment.Start, 6);
            Assert.True(segment.End > segment.Start);
            cursor = segment.End;
        }

        Assert.Equal(plan.Duration, cursor, 6);
    }

    [Fact]
    public void An_image_is_its_own_kind_of_stretch() =>
        Assert.Equal(LiveSegmentKind.Image, Assert.Single(LivePlaybackPlan.Build([VideoTrack(Image(0, 4))]).Picture).Kind);

    [Fact]
    public void A_trimmed_clip_says_where_in_its_source_to_start()
    {
        var plan = LivePlaybackPlan.Build([VideoTrack(Video(0, 30, startTrim: 10, endTrim: 20))]);

        Assert.Equal(10, Assert.Single(plan.Picture).SourceStart, 6);
    }

    [Fact]
    public void A_sped_up_clip_carries_its_rate() =>
        Assert.Equal(2.0, Assert.Single(LivePlaybackPlan.Build([VideoTrack(Video(0, 10, speed: 2.0))]).Picture).Speed);

    [Fact]
    public void A_clip_with_no_rate_plays_at_normal_speed() =>
        Assert.Equal(1.0, Assert.Single(LivePlaybackPlan.Build([VideoTrack(Video(0, 10, speed: 0))]).Picture).Speed);

    [Fact]
    public void The_picture_comes_from_the_first_video_track()
    {
        var baseClip = Video(0, 4);

        var plan = LivePlaybackPlan.Build(
            [Track(TrackType.Video, 1, Video(0, 9)), Track(TrackType.Video, 0, baseClip)]);

        Assert.Equal(baseClip.Id, Assert.Single(plan.Picture).ClipId);
    }

    /// <summary>
    /// A project written before overlap was prevented can still contain one, and the plan has to
    /// stay a sequence: overlapping stretches would leave the player with two answers.
    /// </summary>
    [Fact]
    public void Overlapping_clips_still_produce_a_sequence()
    {
        var plan = LivePlaybackPlan.Build([VideoTrack(Video(0, 6), Video(2, 6))]);

        var cursor = 0.0;
        foreach (var segment in plan.Picture)
        {
            Assert.True(segment.Start >= cursor - 0.000001, "the plan doubled back on itself");
            cursor = segment.End;
        }
    }

    [Fact]
    public void A_clip_wholly_buried_under_another_is_left_out()
    {
        var plan = LivePlaybackPlan.Build([VideoTrack(Video(0, 10), Video(2, 3))]);

        Assert.Single(plan.Picture);
    }

    // ── The sound ─────────────────────────────────────────────────────────────

    [Fact]
    public void An_audio_clip_becomes_a_stretch_of_its_own()
    {
        var plan = LivePlaybackPlan.Build([VideoTrack(Video(0, 5)), AudioTrack(Audio(2, 30, startTrim: 4))]);

        var sound = Assert.Single(plan.Audio);
        Assert.Equal(2,  sound.Start, 6);
        Assert.Equal(32, sound.End, 6);
        Assert.Equal(4,  sound.SourceStart, 6);
    }

    [Fact]
    public void A_muted_audio_track_is_not_in_the_plan()
    {
        var track = AudioTrack(Audio(0, 10));
        track.IsMuted = true;

        Assert.Empty(LivePlaybackPlan.Build([track]).Audio);
    }

    [Fact]
    public void A_muted_audio_clip_is_not_in_the_plan() =>
        Assert.Empty(LivePlaybackPlan.Build([AudioTrack(Audio(0, 10, muted: true))]).Audio);

    [Fact]
    public void A_muted_video_clip_plays_without_its_sound() =>
        Assert.Equal(0, Assert.Single(LivePlaybackPlan.Build([VideoTrack(Video(0, 5, muted: true))]).Picture).Volume);

    [Fact]
    public void A_video_clip_on_a_muted_track_plays_without_its_sound()
    {
        var track = VideoTrack(Video(0, 5));
        track.IsMuted = true;

        Assert.Equal(0, Assert.Single(LivePlaybackPlan.Build([track]).Picture).Volume);
    }

    /// <summary>
    /// A media element's volume is 0 to 1, and assigning anything else throws — which stops the
    /// player, not just the sound.
    /// </summary>
    [Fact]
    public void A_volume_above_unity_is_brought_back_to_something_a_player_accepts() =>
        Assert.Equal(1, Assert.Single(LivePlaybackPlan.Build([AudioTrack(Audio(0, 10, volume: 3))]).Audio).Volume);

    // ── The whole plan ────────────────────────────────────────────────────────

    /// <summary>
    /// A slideshow with a soundtrack: the music is longer than the pictures, and the plan lasts as
    /// long as the longer of the two.
    /// </summary>
    [Fact]
    public void The_plan_lasts_as_long_as_the_longest_thing_in_it()
    {
        var plan = LivePlaybackPlan.Build([VideoTrack(Image(0, 4)), AudioTrack(Audio(0, 30))]);

        Assert.Equal(30, plan.Duration, 6);
    }

    [Fact]
    public void The_plan_names_every_source_the_player_will_need()
    {
        var clip  = Video(0, 5);
        var still = Image(5, 5);
        var music = Audio(0, 30);

        var plan = LivePlaybackPlan.Build([VideoTrack(clip, still), AudioTrack(music)]);

        Assert.Equal(3, plan.RequiredSources.Count);
        Assert.Contains(clip.Id,  plan.RequiredSources);
        Assert.Contains(still.Id, plan.RequiredSources);
        Assert.Contains(music.Id, plan.RequiredSources);
    }

    /// <summary>
    /// Black needs no file, so a timeline of gaps asks the browser for nothing.
    /// </summary>
    [Fact]
    public void Black_is_not_a_source_to_load()
    {
        var plan = LivePlaybackPlan.Build([VideoTrack(Video(4, 2))]);

        Assert.Single(plan.RequiredSources);
    }

    [Fact]
    public void A_source_used_twice_is_only_named_once()
    {
        var clip = Video(0, 5);

        var plan = LivePlaybackPlan.Build([VideoTrack(clip), AudioTrack(Audio(0, 5))]);

        Assert.Equal(2, plan.RequiredSources.Count);
    }
}
