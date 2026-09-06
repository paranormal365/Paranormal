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
            "-filter_complex",
            "[0:a][1:a][2:a]amix=inputs=3:duration=longest:normalize=0:dropout_transition=0[amixed];"
                + "[amixed]alimiter=limit=0.95[aout]",
            "-map", "0:v",
            "-map", "[aout]",
            "-c:v", "copy",
            "-c:a", "aac",
            "-b:a", "192k",
            "mixed.mp4",
        ], args);
    }

    /// <summary>
    /// A clip at full volume stays at full volume when another track is added beside it.
    /// </summary>
    /// <remarks>
    /// amix's default normalize=1 divides every input by the number of inputs, so dropping in one
    /// music bed pulled the dialogue down by about 6 dB — the newly added track was the one that
    /// made everything else quieter, which reads as a bug in whatever was edited last. Its
    /// dropout_transition then swelled the survivors back up over two seconds whenever an input
    /// ended (2026-09-05 audit, audio-3).
    /// </remarks>
    [Fact]
    public void BuildAmixArgs_DoesNotDuckEveryTrackByTheNumberOfTracks()
    {
        var args = ExportArgBuilders.BuildAmixArgs("v.mp4", ["a.mp4"], "out.mp4", Settings());
        var filter = args[Array.IndexOf(args, "-filter_complex") + 1];

        Assert.Contains("normalize=0", filter);
        Assert.Contains("dropout_transition=0", filter);
    }

    /// <summary>
    /// Summing instead of averaging can exceed full scale, so the peaks are caught rather than
    /// left to clip.
    /// </summary>
    [Fact]
    public void BuildAmixArgs_LimitsThePeaksItNoLongerAveragesAway()
    {
        var args = ExportArgBuilders.BuildAmixArgs("v.mp4", ["a.mp4"], "out.mp4", Settings());
        var filter = args[Array.IndexOf(args, "-filter_complex") + 1];

        Assert.Contains("alimiter=limit=0.95[aout]", filter);
    }

    /// <summary>
    /// A silent video is mixed by leaving it out of the graph, not by naming a stream that is not
    /// there.
    /// </summary>
    /// <remarks>
    /// The mix referenced <c>[0:a]</c> unconditionally, so "Separate Audio" on the only clip — and
    /// any slideshow with a music track — asked ffmpeg for a stream the assembled video did not
    /// have and failed the whole export (2026-09-05 audit, audio-1).
    /// </remarks>
    [Fact]
    public void BuildAmixArgs_SilentVideo_DoesNotReferenceAnAbsentStream()
    {
        var args = ExportArgBuilders.BuildAmixArgs(
            "v.mp4", ["a.mp4"], "out.mp4", Settings(), videoHasAudio: false);
        var filter = args[Array.IndexOf(args, "-filter_complex") + 1];

        Assert.DoesNotContain("[0:a]", filter);
        Assert.StartsWith("[1:a]amix=inputs=1:", filter);
        Assert.Contains("-map", args);
        Assert.Equal("0:v", args[Array.IndexOf(args, "-map") + 1]);
    }

    [Fact]
    public void BuildAmixArgs_SingleAudioSegment_CountsTheVideosOwnAudioAsAnInput()
    {
        // inputs=2, not 1 — input 0 is the video's own audio track, which participates in the
        // mix. Getting this off by one would silently drop or duplicate a stream.
        var args = ExportArgBuilders.BuildAmixArgs("v.mp4", ["a.mp4"], "out.mp4", Settings());

        var filter = args[Array.IndexOf(args, "-filter_complex") + 1];
        Assert.StartsWith("[0:a][1:a]amix=inputs=2:duration=longest", filter);
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
