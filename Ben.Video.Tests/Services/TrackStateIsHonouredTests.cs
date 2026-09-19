using Ben.Video.Editor.Models;
using Ben.Video.Editor.Services;
using Microsoft.Extensions.Options;

namespace Ben.Video.Tests.Services;

/// <summary>
/// Muting and locking a track have to mean something.
/// </summary>
/// <remarks>
/// <see cref="TimelineTrack.IsMuted"/> is documented as "audio suppressed during playback and
/// export" and nothing read it: the menu flipped the flag straight on the model, so muting changed
/// the icon and the render was identical. It was not undoable either (2026-09-05 audit, audio-5 and
/// timeline-11).
/// </remarks>
public sealed class TrackStateIsHonouredTests
{
    private static ClipStore Store() => new(Options.Create(new VideoEditorOptions
    {
        MultiTrack = true,
        AudioTracks = true,
    }));

    [Fact]
    public void A_muted_audio_track_is_left_out_of_the_mix()
    {
        var store = Store();
        var music = store.AudioTracks.FirstOrDefault() ?? store.AddAudioTrack();
        store.AddClipToTrack(music.Id, new AudioClip { Name = "music", Duration = 30 });

        Assert.Single(store.AudibleAudioClips);

        store.MuteTrack(music.Id, true);

        Assert.Empty(store.AudibleAudioClips);
    }

    [Fact]
    public void A_clip_on_a_muted_video_track_is_not_audible()
    {
        var store = Store();
        var track = store.Tracks[0];
        var clip  = new VideoClip { Name = "clip", Duration = 5 };
        store.AddClipToTrack(track.Id, clip);

        Assert.True(store.IsAudible(clip));

        store.MuteTrack(track.Id, true);

        Assert.False(store.IsAudible(clip));
    }

    /// <summary>A clip silenced on its own stays silent whatever the track says.</summary>
    [Fact]
    public void A_clip_muted_on_its_own_is_not_audible_either()
    {
        var store = Store();
        var track = store.Tracks[0];
        var clip  = new VideoClip { Name = "clip", Duration = 5, MuteAudio = true };
        store.AddClipToTrack(track.Id, clip);

        Assert.False(store.IsAudible(clip));
    }

    [Fact]
    public void Muting_is_undoable()
    {
        var store = Store();
        var track = store.Tracks[0];

        store.MuteTrack(track.Id, true);
        Assert.True(track.IsMuted);

        store.Undo();

        Assert.False(track.IsMuted);
    }

    [Fact]
    public void Muting_what_is_already_muted_does_nothing()
    {
        var store = Store();
        var track = store.Tracks[0];
        var raised = 0;
        store.OnChange += () => raised++;

        store.MuteTrack(track.Id, false);

        Assert.Equal(0, raised);
    }

    [Fact]
    public void A_locked_track_refuses_every_edit()
    {
        var store = Store();
        var track = store.Tracks[0];
        var clip  = new VideoClip { Name = "clip", Duration = 5 };
        store.AddClipToTrack(track.Id, clip);
        store.LockTrack(track.Id, true);

        store.RemoveClip(clip.Id);
        store.SplitClipAtTimelineTime(clip.Id, 2);
        clip.TimelinePosition = 9;
        store.CommitDraggedPosition(clip.Id, 0);

        Assert.Single(track.Items);
        Assert.Equal(0, clip.TimelinePosition, 3);
    }
}
