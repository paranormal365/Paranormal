using Ben.Video.Editor.Models;
using Ben.Video.Editor.Services;
using Microsoft.Extensions.Options;

namespace Ben.Video.Tests.Services;

/// <summary>
/// Editing a sound: trimming it, silencing it, fading it, and a locked track refusing all of it.
/// </summary>
public sealed class AudioEditTests
{
    private static (ClipStore Store, AudioClip Clip, TimelineTrack Track) Store()
    {
        var store = new ClipStore(Options.Create(
            new VideoEditorOptions { MultiTrack = true, AudioTracks = true }));

        var track = store.AddAudioTrack();
        var clip  = new AudioClip { Name = "evp", Duration = 186, Volume = 0.8 };
        store.AddClipToTrack(track.Id, clip);

        return (store, clip, track);
    }

    // ── Trimming by the edges ─────────────────────────────────────────────────

    /// <summary>
    /// A drag mutates without recording; the commit at the end records the whole gesture.
    /// </summary>
    /// <remarks>
    /// Every pointermove used to push its own undo entry, so a two-second drag left dozens of
    /// steps and undoing a trim meant pressing Ctrl+Z until something moved (2026-09-05 audit,
    /// timeline-6).
    /// </remarks>
    [Fact]
    public void Dragging_a_trim_handle_records_one_step_not_dozens()
    {
        var (store, clip, _) = Store();

        for (var i = 1; i <= 20; i++) store.SetTrimLive(clip.Id, 0, 186 - i);
        store.CommitTrim(clip.Id, 0, 186);

        Assert.Equal(166, clip.EndTrim, precision: 3);
        store.Undo();
        Assert.Equal(186, clip.EndTrim, precision: 3);
    }

    [Fact]
    public void A_drag_that_changed_nothing_records_nothing()
    {
        var (store, clip, _) = Store();
        store.SetClipVolume(clip.Id, 0.5);

        store.SetTrimLive(clip.Id, 0, 186);
        store.CommitTrim(clip.Id, 0, 186);

        // The undo step still available is the volume change, not an empty trim.
        store.Undo();
        Assert.Equal(0.8, clip.Volume, precision: 3);
    }

    [Fact]
    public void A_video_clip_trims_the_same_way()
    {
        var store = new ClipStore(Options.Create(new VideoEditorOptions { MultiTrack = true }));
        var clip  = new VideoClip { Name = "porch", Duration = 30 };
        store.AddClip(clip);

        store.SetTrimLive(clip.Id, 2, 10);
        store.CommitTrim(clip.Id, 0, 30);

        Assert.Equal(2, clip.StartTrim, precision: 3);
        store.Undo();
        Assert.Equal(0, clip.StartTrim, precision: 3);
    }

    // ── Silencing one clip ────────────────────────────────────────────────────

    [Fact]
    public void One_sound_can_be_silenced_without_losing_its_level()
    {
        var (store, clip, _) = Store();

        store.SetClipMuted(clip.Id, true);

        Assert.True(clip.MuteAudio);
        Assert.Equal(0.8, clip.Volume, precision: 3);
    }

    [Fact]
    public void A_muted_clip_is_left_out_of_the_mix()
    {
        var (store, clip, _) = Store();

        store.SetClipMuted(clip.Id, true);

        Assert.Empty(store.AudibleAudioClips);
    }

    [Fact]
    public void And_muting_can_be_undone()
    {
        var (store, clip, _) = Store();

        store.SetClipMuted(clip.Id, true);
        store.Undo();

        Assert.False(clip.MuteAudio);
    }

    // ── Fades ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// A fade is clamped to half the clip, not half the source it was cut from.
    /// </summary>
    /// <remarks>
    /// A ten-second excerpt of a three-minute recording accepted a ninety-second fade, which the
    /// render then had to apply to ten seconds of sound (2026-09-05 audit, audio-16).
    /// </remarks>
    [Fact]
    public void A_fade_cannot_be_longer_than_half_the_clip()
    {
        var (store, clip, _) = Store();
        store.UpdateAudioTrim(clip.Id, 0, 10);

        store.UpdateAudioFade(clip.Id, 90, 0);

        Assert.Equal(5, clip.FadeInSeconds, precision: 3);
    }

    // ── A locked track ────────────────────────────────────────────────────────

    /// <summary>
    /// Locking a track stopped it being moved and left its levels wide open.
    /// </summary>
    [Fact]
    public void A_locked_track_refuses_a_volume_change()
    {
        var (store, clip, track) = Store();
        store.LockTrack(track.Id, true);

        store.SetClipVolume(clip.Id, 0.1);

        Assert.Equal(0.8, clip.Volume, precision: 3);
    }

    [Fact]
    public void And_a_fade()
    {
        var (store, clip, track) = Store();
        store.LockTrack(track.Id, true);

        store.UpdateAudioFade(clip.Id, 2, 2);

        Assert.Equal(0, clip.FadeInSeconds, precision: 3);
    }

    [Fact]
    public void And_a_balance_change()
    {
        var (store, clip, track) = Store();
        store.LockTrack(track.Id, true);

        store.SetClipChannelVolume(clip.Id, 0.2, 0.2);

        Assert.Equal(1.0, clip.LeftVolume, precision: 3);
    }

    [Fact]
    public void And_a_volume_keyframe()
    {
        var (store, clip, track) = Store();
        store.LockTrack(track.Id, true);

        store.AddVolumeKeyframe(clip.Id, 0.5, 0.2);

        Assert.Empty(clip.VolumeAutomation);
    }
}
