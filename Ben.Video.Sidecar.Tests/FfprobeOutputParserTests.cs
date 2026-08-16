using Ben.Video.Sidecar.Jobs;

namespace Ben.Video.Sidecar.Tests;

/// <summary>
/// Item #70 phase 159. The load-bearing property here is <b>parity with the browser</b>: the same
/// clip imported with the sidecar connected and without it must produce identical metadata, or a
/// project would behave differently depending on whether a companion process happened to be
/// running. These fixtures pin the precedence rules <c>ffmpegInterop.js getMetadata</c> uses.
/// </summary>
public sealed class FfprobeOutputParserTests
{
    [Fact]
    public void Parse_VideoStream_ReadsDurationWidthHeight()
    {
        var info = FfprobeOutputParser.TryParse("""
        {"streams":[{"codec_type":"video","duration":"13.80","width":640,"height":360}]}
        """);

        Assert.NotNull(info);
        Assert.Equal(13.80, info!.Duration, 3);
        Assert.Equal(640, info.Width);
        Assert.Equal(360, info.Height);
    }

    [Fact]
    public void Parse_AudioOnly_FallsBackToAudioDuration()
    {
        // The exact case the JS comments call out: no video stream at all, so reading the video
        // duration would silently produce a 0-second clip.
        var info = FfprobeOutputParser.TryParse("""
        {"streams":[{"codec_type":"audio","duration":"42.5"}]}
        """);

        Assert.NotNull(info);
        Assert.Equal(42.5, info!.Duration, 3);
        Assert.Equal(0, info.Width);
        Assert.Equal(0, info.Height);
    }

    [Fact]
    public void Parse_VideoWithoutDuration_FallsBackToAudioDuration()
    {
        // Video stream present but durationless (some containers) — the JS falls through to the
        // audio stream rather than reporting 0, and so must this.
        var info = FfprobeOutputParser.TryParse("""
        {"streams":[{"codec_type":"video","width":1920,"height":1080},
                    {"codec_type":"audio","duration":"7.25"}]}
        """);

        Assert.NotNull(info);
        Assert.Equal(7.25, info!.Duration, 3);
        Assert.Equal(1920, info.Width);
    }

    [Fact]
    public void Parse_NumericJsonTypes_AreAccepted()
    {
        // ffprobe emits durations as strings in most builds but numbers in some; both must work.
        var info = FfprobeOutputParser.TryParse("""
        {"streams":[{"codec_type":"video","duration":9.5,"width":"320","height":"240"}]}
        """);

        Assert.NotNull(info);
        Assert.Equal(9.5, info!.Duration, 3);
        Assert.Equal(320, info.Width);
        Assert.Equal(240, info.Height);
    }

    [Fact]
    public void Parse_NoStreams_ReturnsZeroesNotNull()
    {
        // An empty stream list is valid JSON from a real (if useless) file — distinct from
        // unparseable output, which is what null is reserved for.
        var info = FfprobeOutputParser.TryParse("""{"streams":[]}""");

        Assert.NotNull(info);
        Assert.Equal(0, info!.Duration);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json at all")]
    [InlineData("{\"format\":{}}")] // no streams array
    public void Parse_Unusable_ReturnsNull(string payload)
    {
        // Null specifically means "fall back to wasm" — it must never be confused with a real
        // zero-length clip.
        Assert.Null(FfprobeOutputParser.TryParse(payload));
    }
}
