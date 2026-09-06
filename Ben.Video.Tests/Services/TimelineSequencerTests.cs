using Ben.Video.Editor.Models;
using Ben.Video.Editor.Services;

namespace Ben.Video.Tests.Services;

/// <summary>
/// Reading the timeline at an instant, the way a player that plays the timeline itself has to.
/// </summary>
/// <remarks>
/// The proxy preview re-encodes the whole timeline after every edit, so a person editing an hour
/// of footage waits for an encode to see a cut. A sequence player seeks between the source files
/// instead, and every hard part of doing that is the answer this file checks (2026-09-05 audit,
/// decision D5).
/// </remarks>
public sealed class TimelineSequencerTests
{
    private static VideoClip Video(
        double position, double duration, double startTrim = 0, double endTrim = 0,
        double speed = 1.0, double volume = 1.0, bool muted = false, bool hasAudio = true) =>
        new()
        {
            Name             = "clip",
            TimelinePosition = position,
            Duration         = duration,
            StartTrim        = startTrim,
            EndTrim          = endTrim,
            Speed            = speed,
            Volume           = volume,
            MuteAudio        = muted,
            HasAudio         = hasAudio,
        };

    private static ImageClip Image(double position, double duration) =>
        new() { Name = "still", TimelinePosition = position, Duration = duration };

    private static AudioClip Audio(
        double position, double duration, double startTrim = 0, double endTrim = 0,
        double volume = 1.0, bool muted = false) =>
        new()
        {
            Name             = "sound",
            TimelinePosition = position,
            Duration         = duration,
            StartTrim        = startTrim,
            EndTrim          = endTrim,
            Volume           = volume,
            MuteAudio        = muted,
        };

    private static TimelineTrack Track(TrackType type, int order, params TrackItem[] items)
    {
        var track = new TimelineTrack { Type = type, Order = order };
        track.Items.AddRange(items);
        return track;
    }

    private static TimelineTrack VideoTrack(params TrackItem[] items) => Track(TrackType.Video, 0, items);
    private static TimelineTrack AudioTrack(params TrackItem[] items) => Track(TrackType.Audio, 1, items);

    // ── Which frame is on screen ──────────────────────────────────────────────

    [Fact]
    public void An_empty_timeline_shows_nothing()
    {
        var frame = TimelineSequencer.Resolve([], 0);

        Assert.True(frame.IsEmpty);
        Assert.True(frame.IsGap);
    }

    [Fact]
    public void The_clip_under_the_playhead_is_the_one_on_screen()
    {
        var first  = Video(0, 5);
        var second = Video(5, 5);

        Assert.Same(first,  TimelineSequencer.Resolve([VideoTrack(first, second)], 2).Picture);
        Assert.Same(second, TimelineSequencer.Resolve([VideoTrack(first, second)], 7).Picture);
    }

    /// <summary>
    /// A clip owns its start instant and not its end instant, so the cut between two touching
    /// clips shows exactly one of them rather than flickering between both.
    /// </summary>
    [Fact]
    public void The_instant_of_a_cut_belongs_to_the_clip_that_starts_there()
    {
        var first  = Video(0, 5);
        var second = Video(5, 5);

        Assert.Same(second, TimelineSequencer.Resolve([VideoTrack(first, second)], 5).Picture);
    }

    [Fact]
    public void A_gap_between_clips_shows_nothing()
    {
        var frame = TimelineSequencer.Resolve([VideoTrack(Video(0, 2), Video(6, 2))], 4);

        Assert.True(frame.IsGap);
    }

    [Fact]
    public void Past_the_end_of_everything_shows_nothing() =>
        Assert.True(TimelineSequencer.Resolve([VideoTrack(Video(0, 5))], 99).IsGap);

    [Fact]
    public void An_image_is_a_picture_like_any_other() =>
        Assert.IsType<ImageClip>(TimelineSequencer.Resolve([VideoTrack(Image(0, 4))], 2).Picture);

    /// <summary>
    /// The picture comes from the base video track, which is what export composites onto. A second
    /// video track is a layer this player cannot draw, and says so by not pretending to.
    /// </summary>
    [Fact]
    public void The_picture_comes_from_the_first_video_track()
    {
        var baseClip    = Video(0, 10);
        var overlayClip = Video(0, 10);

        var frame = TimelineSequencer.Resolve(
            [Track(TrackType.Video, 1, overlayClip), Track(TrackType.Video, 0, baseClip)], 5);

        Assert.Same(baseClip, frame.Picture);
    }

    /// <summary>
    /// A clock that arrived as NaN shows the first frame rather than black, because black is what
    /// a broken player and an empty timeline both look like.
    /// </summary>
    [Theory]
    [InlineData(double.NaN)]
    [InlineData(-4)]
    public void A_nonsensical_clock_reads_as_the_start(double time)
    {
        var first = Video(0, 5);

        Assert.Same(first, TimelineSequencer.Resolve([VideoTrack(first)], time).Picture);
    }

    // ── Where inside the source ───────────────────────────────────────────────

    [Fact]
    public void A_clip_at_the_start_of_the_timeline_plays_from_its_own_start() =>
        Assert.Equal(2, TimelineSequencer.Resolve([VideoTrack(Video(0, 5))], 2).PictureSourceTime, 6);

    [Fact]
    public void A_clip_further_along_the_timeline_plays_from_where_it_was_reached() =>
        Assert.Equal(3, TimelineSequencer.Resolve([VideoTrack(Video(10, 5))], 13).PictureSourceTime, 6);

    /// <summary>
    /// Trim first: a clip trimmed to start ten seconds in is ten seconds in when it begins.
    /// </summary>
    [Fact]
    public void A_trimmed_clip_plays_from_its_trim()
    {
        var clip = Video(position: 0, duration: 30, startTrim: 10, endTrim: 20);

        Assert.Equal(10, TimelineSequencer.Resolve([VideoTrack(clip)], 0).PictureSourceTime, 6);
        Assert.Equal(13, TimelineSequencer.Resolve([VideoTrack(clip)], 3).PictureSourceTime, 6);
    }

    /// <summary>
    /// Then speed: at double speed the source advances two seconds for every second of timeline.
    /// </summary>
    [Fact]
    public void A_sped_up_clip_moves_through_its_source_faster()
    {
        var clip = Video(position: 0, duration: 20, startTrim: 0, endTrim: 20, speed: 2.0);

        Assert.Equal(6, TimelineSequencer.Resolve([VideoTrack(clip)], 3).PictureSourceTime, 6);
    }

    [Fact]
    public void A_slowed_clip_moves_through_its_source_slower()
    {
        var clip = Video(position: 0, duration: 20, startTrim: 0, endTrim: 20, speed: 0.5);

        Assert.Equal(1.5, TimelineSequencer.Resolve([VideoTrack(clip)], 3).PictureSourceTime, 6);
    }

    /// <summary>
    /// The source time never leaves the trimmed region, so a speed above one cannot ask a video
    /// element to seek past the end of its own file.
    /// </summary>
    [Fact]
    public void The_source_time_stays_inside_the_trim()
    {
        var clip = Video(position: 0, duration: 10, startTrim: 2, endTrim: 6, speed: 4.0);

        var frame = TimelineSequencer.Resolve([VideoTrack(clip)], 3.9);

        Assert.InRange(frame.PictureSourceTime, 2, 6);
    }

    [Fact]
    public void A_speed_of_zero_is_treated_as_normal_speed()
    {
        var clip = Video(position: 0, duration: 10, speed: 0);

        Assert.Equal(3, TimelineSequencer.Resolve([VideoTrack(clip)], 3).PictureSourceTime, 6);
    }

    /// <summary>An image has no clock of its own: every moment of it is the same frame.</summary>
    [Fact]
    public void An_image_has_no_source_time() =>
        Assert.Equal(0, TimelineSequencer.Resolve([VideoTrack(Image(0, 5))], 3).PictureSourceTime);

    // ── When the picture changes ──────────────────────────────────────────────

    [Fact]
    public void The_next_cut_is_the_end_of_the_clip_being_shown()
    {
        var frame = TimelineSequencer.Resolve([VideoTrack(Video(0, 5), Video(5, 5))], 2);

        Assert.Equal(5, frame.NextCutAt, 6);
    }

    /// <summary>
    /// Inside a clip the cut is where that clip ends, not where the next one starts — what is
    /// between them is black, and a player that held the last frame across a gap would be lying.
    /// </summary>
    [Fact]
    public void A_clip_followed_by_a_gap_still_cuts_when_it_ends()
    {
        var frame = TimelineSequencer.Resolve([VideoTrack(Video(0, 5), Video(9, 5))], 2);

        Assert.Equal(5, frame.NextCutAt, 6);
    }

    [Fact]
    public void In_a_gap_the_next_cut_is_where_the_next_clip_begins()
    {
        var frame = TimelineSequencer.Resolve([VideoTrack(Video(0, 5), Video(9, 5))], 7);

        Assert.Equal(9, frame.NextCutAt, 6);
    }

    [Fact]
    public void After_the_last_clip_nothing_cuts_again() =>
        Assert.Equal(
            double.PositiveInfinity,
            TimelineSequencer.Resolve([VideoTrack(Video(0, 5))], 20).NextCutAt);

    [Fact]
    public void The_next_picture_is_what_a_player_should_be_loading()
    {
        var first  = Video(0, 5);
        var second = Video(5, 5);
        var third  = Video(10, 5);

        Assert.Same(second, TimelineSequencer.NextPicture([VideoTrack(first, second, third)], 2));
        Assert.Same(third,  TimelineSequencer.NextPicture([VideoTrack(first, second, third)], 7));
        Assert.Null(TimelineSequencer.NextPicture([VideoTrack(first, second, third)], 12));
    }

    // ── What is audible ───────────────────────────────────────────────────────

    [Fact]
    public void An_audio_clip_under_the_playhead_is_playing()
    {
        var music = Audio(0, 30);

        var cue = Assert.Single(TimelineSequencer.Resolve([VideoTrack(Video(0, 5)), AudioTrack(music)], 3).Audio);

        Assert.Same(music, cue.Clip);
        Assert.Equal(3, cue.SourceTime, 6);
    }

    [Fact]
    public void An_audio_clip_the_playhead_has_not_reached_is_not_playing() =>
        Assert.Empty(TimelineSequencer.Resolve([AudioTrack(Audio(10, 30))], 3).Audio);

    [Fact]
    public void A_trimmed_audio_clip_plays_from_its_trim()
    {
        var music = Audio(position: 5, duration: 60, startTrim: 20, endTrim: 40);

        var cue = Assert.Single(TimelineSequencer.Resolve([AudioTrack(music)], 8).Audio);

        Assert.Equal(23, cue.SourceTime, 6);
    }

    [Fact]
    public void Two_audio_tracks_both_play()
    {
        var narration = Audio(0, 30);
        var music     = Audio(0, 30);

        var frame = TimelineSequencer.Resolve(
            [Track(TrackType.Audio, 1, narration), Track(TrackType.Audio, 2, music)], 3);

        Assert.Equal(2, frame.Audio.Count);
    }

    /// <summary>
    /// Muting a track means what it says. The old preview played muted tracks and mixed them into
    /// the export as well (2026-09-05 audit, audio-5).
    /// </summary>
    [Fact]
    public void A_muted_track_is_silent()
    {
        var track = AudioTrack(Audio(0, 30));
        track.IsMuted = true;

        Assert.Empty(TimelineSequencer.Resolve([track], 3).Audio);
    }

    [Fact]
    public void A_muted_clip_on_an_audible_track_is_silent() =>
        Assert.Empty(TimelineSequencer.Resolve([AudioTrack(Audio(0, 30, muted: true))], 3).Audio);

    [Fact]
    public void A_clips_own_volume_is_carried_through() =>
        Assert.Equal(
            0.4,
            Assert.Single(TimelineSequencer.Resolve([AudioTrack(Audio(0, 30, volume: 0.4))], 3).Audio).Volume,
            6);

    /// <summary>
    /// Automation and the scalar volume both allow more than unity, which ffmpeg can do and a
    /// media element cannot: assigning a volume above one throws, and the player stops.
    /// </summary>
    [Fact]
    public void A_volume_above_unity_is_brought_back_to_something_a_player_accepts() =>
        Assert.Equal(
            1.0,
            Assert.Single(TimelineSequencer.Resolve([AudioTrack(Audio(0, 30, volume: 2.5))], 3).Audio).Volume,
            6);

    [Fact]
    public void A_video_clips_own_sound_plays_with_it() =>
        Assert.Equal(0.8, TimelineSequencer.Resolve([VideoTrack(Video(0, 5, volume: 0.8))], 2).PictureVolume, 6);

    [Fact]
    public void A_video_clip_that_was_muted_is_silent() =>
        Assert.Equal(0, TimelineSequencer.Resolve([VideoTrack(Video(0, 5, muted: true))], 2).PictureVolume);

    [Fact]
    public void A_video_clip_with_no_soundtrack_is_silent() =>
        Assert.Equal(0, TimelineSequencer.Resolve([VideoTrack(Video(0, 5, hasAudio: false))], 2).PictureVolume);

    [Fact]
    public void A_video_clip_on_a_muted_track_is_silent()
    {
        var track = VideoTrack(Video(0, 5));
        track.IsMuted = true;

        Assert.Equal(0, TimelineSequencer.Resolve([track], 2).PictureVolume);
    }

    /// <summary>
    /// A slideshow with a soundtrack: nothing on screen has a clock, and the music still plays.
    /// </summary>
    [Fact]
    public void A_gap_is_not_necessarily_silence()
    {
        var frame = TimelineSequencer.Resolve(
            [VideoTrack(Video(0, 2), Video(6, 2)), AudioTrack(Audio(0, 30))], 4);

        Assert.True(frame.IsGap);
        Assert.False(frame.IsEmpty);
        Assert.Single(frame.Audio);
    }

    /// <summary>
    /// Volume automation is read at the playhead's position inside the clip, so a fade actually
    /// fades rather than jumping at the ends.
    /// </summary>
    [Fact]
    public void Automation_is_read_where_the_playhead_is()
    {
        var music = Audio(0, 10);
        music.VolumeAutomation.Add(new VolumeKeyframe { Position = 0, Volume = 0 });
        music.VolumeAutomation.Add(new VolumeKeyframe { Position = 1, Volume = 1 });

        var quarter = Assert.Single(TimelineSequencer.Resolve([AudioTrack(music)], 2.5).Audio);
        var most    = Assert.Single(TimelineSequencer.Resolve([AudioTrack(music)], 7.5).Audio);

        Assert.Equal(0.25, quarter.Volume, 2);
        Assert.Equal(0.75, most.Volume, 2);
    }
}
