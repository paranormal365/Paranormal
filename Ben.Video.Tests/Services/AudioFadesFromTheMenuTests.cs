using Ben.Video.Editor.Models;
using Ben.Video.Editor.Services;
using Microsoft.Extensions.Options;

namespace Ben.Video.Tests.Services;

/// <summary>
/// Fading a sound in and out from the timeline, the way you fade a picture.
/// </summary>
/// <remarks>
/// The clip menu's Fade In and Fade Out were <c>item is not VideoClip</c>, so right-clicking an
/// audio clip showed both greyed out with nothing to say why — while the model, the store and the
/// properties panel had all supported audio fades for a while. The capability was there and the
/// menu refused it (2026-09-18 audit). These tests hold the store half; the menu half is the one
/// line of enabling beside it.
/// </remarks>
public sealed class AudioFadesFromTheMenuTests
{
    private static (ClipStore Store, AudioClip Clip) OneSound(double duration = 8)
    {
        var store = new ClipStore(Options.Create(new VideoEditorOptions { AudioTracks = true, MultiTrack = true }));
        var track = store.AudioTracks.FirstOrDefault() ?? store.AddAudioTrack();
        var clip  = new AudioClip { Name = "narration", Duration = duration };
        store.AddClipToTrack(track.Id, clip);
        return (store, clip);
    }

    [Fact]
    public void Fading_in_does_not_wipe_the_fade_out_already_set()
    {
        // UpdateAudioFade takes both at once, so the menu has to pass the other one through.
        var (store, clip) = OneSound();
        store.UpdateAudioFade(clip.Id, 0, 2);

        store.UpdateAudioFade(clip.Id, 1, clip.FadeOutSeconds);

        Assert.Equal(1, clip.FadeInSeconds, 3);
        Assert.Equal(2, clip.FadeOutSeconds, 3);
    }

    [Fact]
    public void Fading_out_does_not_wipe_the_fade_in_already_set()
    {
        var (store, clip) = OneSound();
        store.UpdateAudioFade(clip.Id, 2, 0);

        store.UpdateAudioFade(clip.Id, clip.FadeInSeconds, 1);

        Assert.Equal(2, clip.FadeInSeconds, 3);
        Assert.Equal(1, clip.FadeOutSeconds, 3);
    }

    [Fact]
    public void A_short_sound_gets_a_fade_it_can_carry()
    {
        // The menu asks for a second, or a quarter of the clip when that is shorter.
        var (store, clip) = OneSound(duration: 1.2);
        var asked = Math.Min(1.0, clip.EffectiveLength / 4);

        store.UpdateAudioFade(clip.Id, asked, 0);

        Assert.True(clip.FadeInSeconds > 0, "a short sound got no fade at all");
        Assert.True(clip.FadeInSeconds <= clip.TrimmedDuration / 2,
            "the fade is longer than half the clip it is fading");
    }

    [Fact]
    public void The_fade_is_undoable()
    {
        var (store, clip) = OneSound();

        store.UpdateAudioFade(clip.Id, 1, 0);
        Assert.Equal(1, clip.FadeInSeconds, 3);

        store.Undo();

        Assert.Equal(0, clip.FadeInSeconds, 3);
    }
}
