using Ben.Video.Editor.Models;
using Ben.Video.Editor.Services;

namespace Ben.Video.Tests.Services;

/// <summary>
/// Everything else dropping in level while a narration track plays.
/// </summary>
/// <remarks>
/// Music and room tone are set at a level chosen for the stretches with nobody talking, and the
/// moment a voice comes in they are too loud. Without this the remedy is a volume envelope drawn by
/// hand around every line and redrawn whenever the timing moves (2026-09-05 audit, the completeness
/// critic's ducking item).
/// </remarks>
public sealed class DuckingMixTests
{
    private static readonly ExportSettings Settings = new() { IncludeAudio = true };

    private static string Graph(string[] args) => args[Array.IndexOf(args, "-filter_complex") + 1];

    [Fact]
    public void With_nothing_ducking_the_mix_is_unchanged()
    {
        var args = ExportArgBuilders.BuildAmixArgs("v.mp4", ["a.wav", "b.wav"], "out.mp4", Settings);

        Assert.DoesNotContain("sidechaincompress", Graph(args));
    }

    [Fact]
    public void A_ducking_track_compresses_the_others_against_it()
    {
        var args = ExportArgBuilders.BuildAmixArgs(
            "v.mp4", ["music.wav", "narration.wav"], "out.mp4", Settings, duckingSegments: [1]);

        Assert.Contains("sidechaincompress", Graph(args));
    }

    /// <summary>
    /// The narration is heard as well as ducked against, so it is split rather than consumed once.
    /// </summary>
    [Fact]
    public void The_narration_is_still_in_the_finished_mix()
    {
        var graph = Graph(ExportArgBuilders.BuildAmixArgs(
            "v.mp4", ["music.wav", "narration.wav"], "out.mp4", Settings, duckingSegments: [1]));

        Assert.Contains("asplit=2", graph);
        Assert.Contains("[keymix]", graph);
    }

    [Fact]
    public void The_pictures_own_sound_ducks_too()
    {
        var graph = Graph(ExportArgBuilders.BuildAmixArgs(
            "v.mp4", ["narration.wav"], "out.mp4", Settings, duckingSegments: [0]));

        Assert.Contains("[0:a]", graph);
        Assert.Contains("sidechaincompress", graph);
    }

    /// <summary>
    /// If everything audible is the narration there is nothing to duck, and the ordinary mix is
    /// the right answer rather than a compressor keyed against itself.
    /// </summary>
    [Fact]
    public void With_nothing_left_to_duck_the_ordinary_mix_is_used()
    {
        var graph = Graph(ExportArgBuilders.BuildAmixArgs(
            "v.mp4", ["narration.wav"], "out.mp4", Settings,
            videoHasAudio: false, duckingSegments: [0]));

        Assert.DoesNotContain("sidechaincompress", graph);
    }

    [Fact]
    public void Several_narration_clips_are_summed_into_one_key()
    {
        var graph = Graph(ExportArgBuilders.BuildAmixArgs(
            "v.mp4", ["music.wav", "line1.wav", "line2.wav"], "out.mp4", Settings,
            duckingSegments: [1, 2]));

        Assert.Contains("[key]", graph);
        Assert.Contains("sidechaincompress", graph);
    }

    [Fact]
    public void An_index_that_names_no_segment_is_ignored()
    {
        var graph = Graph(ExportArgBuilders.BuildAmixArgs(
            "v.mp4", ["a.wav"], "out.mp4", Settings, duckingSegments: [7]));

        Assert.DoesNotContain("sidechaincompress", graph);
    }

    /// <summary>The limiter still catches the peaks, as it does on the ordinary mix.</summary>
    [Fact]
    public void The_ducked_mix_is_still_limited()
    {
        var graph = Graph(ExportArgBuilders.BuildAmixArgs(
            "v.mp4", ["music.wav", "narration.wav"], "out.mp4", Settings, duckingSegments: [1]));

        Assert.Contains("alimiter", graph);
        Assert.EndsWith("[aout]", graph);
    }
}
