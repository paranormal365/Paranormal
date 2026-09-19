using Ben.Video.Editor.Models;
using Ben.Video.Editor.Services;

namespace Ben.Video.Tests.Services;

/// <summary>
/// A clip on a track above the primary one reaches the render, at the place the timeline puts it.
/// </summary>
/// <remarks>
/// <para>Secondary video tracks were never exported at all: a clip on track 2 was drawn on the
/// timeline, editable in the properties panel, and simply absent from the file. Multi-track was a
/// feature of the editor and not of the product (2026-09-05 audit, export-1).</para>
///
/// <para>The unused composite that existed for this fed each layer in whole and unpositioned, so
/// shipping it as-is would have put a clip from ten seconds in at the very start and then left its
/// final frame frozen over everything after it. These pin the two things that stop that.</para>
/// </remarks>
public sealed class LayerCompositeArgsTests
{
    private static ExportSettings Settings(bool includeAudio = true) =>
        new() { IncludeAudio = includeAudio, AudioCodec = "aac", AudioBitrate = 192,
                VideoCodec = "libx264", UseCrf = true, Crf = 23, PixelFormat = "yuv420p" };

    private static string Filter(string[] args) =>
        args[Array.IndexOf(args, "-filter_complex") + 1];

    [Fact]
    public void The_layer_is_shifted_to_its_own_place_on_the_timeline()
    {
        var args = ExportArgBuilders.BuildLayerCompositeArgs(
            "base.mp4", "layer.mp4", "out.mp4", start: 10, duration: 4, Settings(), layerHasAudio: false);

        Assert.Contains("[1:v]setpts=PTS-STARTPTS+10.000/TB[ov]", Filter(args));
    }

    /// <summary>
    /// Outside its own span the picture underneath is what plays.
    /// </summary>
    [Fact]
    public void The_layer_shows_only_across_its_own_span()
    {
        var args = ExportArgBuilders.BuildLayerCompositeArgs(
            "base.mp4", "layer.mp4", "out.mp4", start: 10, duration: 4, Settings(), layerHasAudio: false);

        Assert.Contains("enable='between(t,10.000,14.000)'", Filter(args));
    }

    /// <summary>
    /// overlay repeats its last frame by default, which would leave a four-second clip covering
    /// the rest of the export.
    /// </summary>
    [Fact]
    public void The_layer_hands_the_picture_back_when_it_ends()
    {
        var args = ExportArgBuilders.BuildLayerCompositeArgs(
            "base.mp4", "layer.mp4", "out.mp4", start: 0, duration: 4, Settings(), layerHasAudio: false);

        Assert.Contains("eof_action=pass", Filter(args));
    }

    [Fact]
    public void A_silent_layer_keeps_the_sound_already_there()
    {
        var args = ExportArgBuilders.BuildLayerCompositeArgs(
            "base.mp4", "layer.mp4", "out.mp4", start: 2, duration: 4, Settings(), layerHasAudio: false);

        Assert.DoesNotContain("amix", Filter(args));
        Assert.Contains("0:a?", args);
    }

    [Fact]
    public void A_layer_with_sound_is_mixed_in_at_its_own_position()
    {
        var args = ExportArgBuilders.BuildLayerCompositeArgs(
            "base.mp4", "layer.mp4", "out.mp4", start: 2.5, duration: 4, Settings(), layerHasAudio: true);

        var filter = Filter(args);
        Assert.Contains("[1:a]adelay=2500:all=1[oa]", filter);
        Assert.Contains("amix=inputs=2:duration=first:normalize=0:dropout_transition=0", filter);
        Assert.Contains("[aout]", filter);
        Assert.Contains("-map", args);
        Assert.Equal("[aout]", args[Array.LastIndexOf(args, "-map") + 1]);
    }

    /// <summary>
    /// The mix ends when the picture underneath does — the layer is a piece of a longer timeline,
    /// not the thing that decides how long the export runs.
    /// </summary>
    [Fact]
    public void A_layers_sound_does_not_extend_the_export()
    {
        var args = ExportArgBuilders.BuildLayerCompositeArgs(
            "base.mp4", "layer.mp4", "out.mp4", start: 0, duration: 4, Settings(), layerHasAudio: true);

        Assert.Contains("duration=first", Filter(args));
        Assert.DoesNotContain("duration=longest", Filter(args));
    }

    [Fact]
    public void An_export_without_audio_maps_none()
    {
        var args = ExportArgBuilders.BuildLayerCompositeArgs(
            "base.mp4", "layer.mp4", "out.mp4", start: 0, duration: 4, Settings(includeAudio: false),
            layerHasAudio: true);

        Assert.DoesNotContain("amix", Filter(args));
        Assert.DoesNotContain("0:a?", args);
        Assert.DoesNotContain("[aout]", args);
    }

    [Fact]
    public void The_base_is_input_zero_and_the_layer_input_one()
    {
        var args = ExportArgBuilders.BuildLayerCompositeArgs(
            "base.mp4", "layer.mp4", "out.mp4", start: 0, duration: 4, Settings(), layerHasAudio: false);

        var inputs = new List<string>();
        for (var i = 0; i < args.Length - 1; i++)
            if (args[i] == "-i") inputs.Add(args[i + 1]);

        Assert.Equal(["base.mp4", "layer.mp4"], inputs);
        Assert.Equal("out.mp4", args[^1]);
    }
}
