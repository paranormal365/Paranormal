using Ben.Video.Editor.Models;
using Ben.Video.Editor.Services;
using Microsoft.Extensions.Options;

namespace Ben.Video.Tests.Services;

/// <summary>
/// Splitting at the playhead, which is an absolute position on the timeline.
/// </summary>
/// <remarks>
/// <see cref="ClipStore.SplitClip"/> takes an offset measured from the clip's own start, and every
/// caller in the editor handed it the playhead instead. For the first clip on a track the two are
/// the same number, which is exactly why nobody noticed: the cut was right on the clip everyone
/// tested with and wrong on every clip after it (2026-09-05 audit, timeline-1 and audio-9).
/// </remarks>
public sealed class SplitAtTimelineTimeTests
{
    private static ClipStore Store() => new(Options.Create(new VideoEditorOptions
    {
        MultiTrack = true,
        AudioTracks = true,
    }));

    private static VideoClip Placed(double position, double duration, string name = "clip")
        => new() { Name = name, Duration = duration, TimelinePosition = position };

    [Fact]
    public void A_clip_that_does_not_start_at_zero_is_cut_where_the_playhead_is()
    {
        var store = Store();
        var track = store.Tracks[0];
        var clip  = Placed(position: 10, duration: 8);
        store.AddClipToTrack(track.Id, clip);

        // Playhead at 13s: three seconds into a clip that starts at ten.
        var split = store.SplitClipAtTimelineTime(clip.Id, 13);

        Assert.True(split);
        var pieces = store.Tracks[0].Items.OfType<VideoClip>().OrderBy(c => c.TimelinePosition).ToList();
        Assert.Equal(2, pieces.Count);
        Assert.Equal(3, pieces[0].EffectiveLength, 3);
        Assert.Equal(5, pieces[1].EffectiveLength, 3);

        // The old call passed 13 straight through as an offset, which for this clip is past its end.
        Assert.Equal(13, pieces[1].TimelinePosition, 3);
    }

    [Fact]
    public void The_playhead_outside_the_clip_splits_nothing_and_says_so()
    {
        var store = Store();
        var clip  = Placed(position: 10, duration: 8);
        store.AddClipToTrack(store.Tracks[0].Id, clip);

        Assert.False(store.SplitClipAtTimelineTime(clip.Id, 4));    // before it
        Assert.False(store.SplitClipAtTimelineTime(clip.Id, 25));   // after it
        Assert.Single(store.Tracks[0].Items);
    }

    /// <summary>A cut exactly on an edge would make a piece of nothing.</summary>
    [Theory]
    [InlineData(10)]
    [InlineData(18)]
    public void The_playhead_on_an_edge_splits_nothing(double playhead)
    {
        var store = Store();
        var clip  = Placed(position: 10, duration: 8);
        store.AddClipToTrack(store.Tracks[0].Id, clip);

        Assert.False(store.SplitClipAtTimelineTime(clip.Id, playhead));
        Assert.Single(store.Tracks[0].Items);
    }

    [Fact]
    public void An_unknown_item_splits_nothing()
    {
        var store = Store();

        Assert.False(store.SplitClipAtTimelineTime(Guid.NewGuid(), 3));
    }

    /// <summary>
    /// Audio measures its own length by its trims, so a trimmed clip is cut inside what is on the
    /// timeline rather than inside the whole source file.
    /// </summary>
    [Fact]
    public void A_trimmed_audio_clip_is_cut_by_what_is_on_the_timeline()
    {
        var store = Store();
        var audioTrack = store.AudioTracks.FirstOrDefault() ?? store.AddAudioTrack();

        // A three-minute file trimmed to ten seconds, sitting at 5s.
        var audio = new AudioClip
        {
            Name = "music", Duration = 186, StartTrim = 20, EndTrim = 30, TimelinePosition = 5,
        };
        store.AddClipToTrack(audioTrack.Id, audio);

        Assert.Equal(10, audio.EffectiveLength, 3);

        // 4 seconds in.
        Assert.True(store.SplitClipAtTimelineTime(audio.Id, 9));

        // And past the end of the trimmed region, but well inside the source file, is a refusal.
        var still = store.AudioTracks.SelectMany(t => t.AudioClips).ToList();
        Assert.Equal(2, still.Count);
        Assert.False(store.SplitClipAtTimelineTime(still[0].Id, 100));
    }

    [Fact]
    public void An_untrimmed_audio_clip_still_measures_by_its_source_length()
    {
        var audio = new AudioClip { Duration = 186 };

        Assert.Equal(186, audio.EffectiveLength, 3);
    }

    [Fact]
    public void Splitting_is_undoable_through_the_absolute_entry_point()
    {
        var store = Store();
        var clip  = Placed(position: 10, duration: 8);
        store.AddClipToTrack(store.Tracks[0].Id, clip);

        store.SplitClipAtTimelineTime(clip.Id, 13);
        Assert.Equal(2, store.Tracks[0].Items.Count);

        store.Undo();

        Assert.Single(store.Tracks[0].Items);
    }
}
