using Ben.Video.Editor.Models;
using Ben.Video.Editor.Services;

namespace Ben.Video.Tests.Services;

/// <summary>
/// Item #70 phase 162 — <see cref="ExportArgBuilders.BuildAmixArgs"/> was extracted from
/// <c>ExportService.MixAudioTracksAsync</c> so the sidecar can run the identical mix.
///
/// <para><b>These expectations were transcribed from the inline code BEFORE the extraction</b>,
/// which is the whole point: an extraction that changes the argv would change exported audio, and
/// a test written afterwards from the new code would happily agree with a mistake. The literal
/// arrays below are what the pre-refactor implementation produced.</para>
/// </summary>
public sealed class AmixArgBuilderTests
{
    private static ExportSettings Settings(string codec = "aac", int bitrate = 192) =>
        new() { AudioCodec = codec, AudioBitrate = bitrate };

    [Fact]
    public void BuildAmixArgs_TwoAudioSegments_MatchesThePreExtractionArgv()
    {
        var args = ExportArgBuilders.BuildAmixArgs(
            "video.mp4", ["audio_seg_000.mp4", "audio_seg_001.mp4"], "mixed.mp4", Settings());

        Assert.Equal(
        [
            "-i", "video.mp4",
            "-i", "audio_seg_000.mp4",
            "-i", "audio_seg_001.mp4",
            "-filter_complex", "[0:a][1:a][2:a]amix=inputs=3:duration=longest[aout]",
            "-map", "0:v",
            "-map", "[aout]",
            "-c:v", "copy",
            "-c:a", "aac",
            "-b:a", "192k",
            "mixed.mp4",
        ], args);
    }

    [Fact]
    public void BuildAmixArgs_SingleAudioSegment_CountsTheVideosOwnAudioAsAnInput()
    {
        // inputs=2, not 1 — input 0 is the video's own audio track, which participates in the
        // mix. Getting this off by one would silently drop or duplicate a stream.
        var args = ExportArgBuilders.BuildAmixArgs("v.mp4", ["a.mp4"], "out.mp4", Settings());

        Assert.Contains("[0:a][1:a]amix=inputs=2:duration=longest[aout]", args);
    }

    [Fact]
    public void BuildAmixArgs_VideoIsAlwaysStreamCopied()
    {
        // The mix must never re-encode video: that would both cost a full re-encode and silently
        // degrade quality at a step the user thinks is audio-only.
        var args = ExportArgBuilders.BuildAmixArgs("v.mp4", ["a.mp4"], "out.mp4", Settings());

        var vIndex = Array.IndexOf(args, "-c:v");
        Assert.True(vIndex >= 0);
        Assert.Equal("copy", args[vIndex + 1]);
    }

    [Theory]
    [InlineData("aac", 128, "128k")]
    [InlineData("libopus", 96, "96k")]
    [InlineData("mp3", 320, "320k")]
    public void BuildAmixArgs_HonorsCodecAndBitrateFromSettings(string codec, int bitrate, string expected)
    {
        var args = ExportArgBuilders.BuildAmixArgs("v.mp4", ["a.mp4"], "out.mp4", Settings(codec, bitrate));

        var cIndex = Array.IndexOf(args, "-c:a");
        Assert.Equal(codec, args[cIndex + 1]);
        var bIndex = Array.IndexOf(args, "-b:a");
        Assert.Equal(expected, args[bIndex + 1]);
    }

    [Fact]
    public void BuildAmixArgs_InputOrderIsVideoThenSegmentsInOrder()
    {
        // Label [k:a] refers to the k-th -i. If inputs were emitted out of order the labels would
        // point at the wrong streams and clips would play at each other's positions.
        var args = ExportArgBuilders.BuildAmixArgs("v.mp4", ["first.mp4", "second.mp4"], "o.mp4", Settings());

        var inputs = new List<string>();
        for (var i = 0; i < args.Length - 1; i++)
            if (args[i] == "-i") inputs.Add(args[i + 1]);

        Assert.Equal(["v.mp4", "first.mp4", "second.mp4"], inputs);
    }

    [Fact]
    public void BuildConcatEncodeArgs_UsesTheConcatDemuxerWithoutAnyScaling()
    {
        // Export never scales at concat time — each segment was already produced at the target
        // size. A stray scale here would resample every export.
        var args = ExportArgBuilders.BuildConcatEncodeArgs("/w/list.txt", "out.mp4", new ExportSettings());

        Assert.Equal("-f", args[0]);
        Assert.Equal("concat", args[1]);
        Assert.Equal("-safe", args[2]);
        Assert.Equal("0", args[3]);
        Assert.Equal("-i", args[4]);
        Assert.Equal("/w/list.txt", args[5]);
        Assert.Equal("out.mp4", args[^1]);
        Assert.DoesNotContain("scale", string.Join(' ', args));
        Assert.DoesNotContain("-vf", args);
    }
}
