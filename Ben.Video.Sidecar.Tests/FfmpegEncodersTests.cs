using Ben.Video.Sidecar.Jobs;
using Xunit;

namespace Ben.Video.Sidecar.Tests;

/// <summary>
/// Reading <c>ffmpeg -encoders</c>.
/// </summary>
/// <remarks>
/// The listing below is copied verbatim from the two builds that matter: the GPL one shipped today
/// and the permissively licensed one a Store package would carry. Everything downstream turns on
/// whether a name is in this list, so a parser that quietly dropped one - or invented one from a
/// header line - would pick the wrong encoder without ever failing.
/// </remarks>
public sealed class FfmpegEncodersTests
{
    private const string RealListing = """
        Encoders:
         V..... = Video
         A..... = Audio
         S..... = Subtitle
         .F.... = Frame-level multithreading
         ..S... = Slice-level multithreading
         ...X.. = Codec is experimental
         ....B. = Supports draw_horiz_band
         .....D = Supports direct rendering method 1
         ------
         V....D libx264              libx264 H.264 / AVC / MPEG-4 AVC (codec h264)
         V....D libx264rgb           libx264 H.264 / AVC / MPEG-4 AVC RGB (codec h264)
         V....D libx265              libx265 H.265 / HEVC (codec hevc)
         V....D libvpx-vp9           libvpx VP9 (codec vp9)
         V....D h264_mf              H264 via MediaFoundation (codec h264)
         V....D hevc_mf              HEVC via MediaFoundation (codec hevc)
         A....D aac                  AAC (Advanced Audio Coding)
         A....D libopus              libopus Opus
        """;

    [Fact]
    public void It_finds_every_encoder_in_a_real_listing()
    {
        var names = FfmpegEncoders.Parse(RealListing);

        Assert.Contains("libx264", names);
        Assert.Contains("libx265", names);
        Assert.Contains("libvpx-vp9", names);
        Assert.Contains("h264_mf", names);
        Assert.Contains("aac", names);
        Assert.Contains("libopus", names);
        Assert.Equal(8, names.Count);
    }

    /// <summary>
    /// The header explains the capability letters before listing anything. Those lines look enough
    /// like an encoder line to fool a loose parser, and "Video" or "Frame-level" as an encoder name
    /// would be harmless noise - but "------" landing in the set would not be obvious later.
    /// </summary>
    [Fact]
    public void The_header_is_not_mistaken_for_encoders()
    {
        var names = FfmpegEncoders.Parse(RealListing);

        Assert.DoesNotContain("=", names);
        Assert.DoesNotContain("Video", names);
        Assert.DoesNotContain("------", names);
        Assert.DoesNotContain("Encoders:", names);
    }

    [Fact]
    public void Windows_line_endings_do_not_stick_to_the_names()
    {
        var names = FfmpegEncoders.Parse(RealListing.Replace("\n", "\r\n"));

        Assert.Contains("libx264", names);
        Assert.DoesNotContain("libx264\r", names);
    }

    /// <summary>
    /// A probe that cannot run returns nothing, and nothing has to stay safe to pass along: every
    /// caller reads an empty list as "unknown" and keeps its original behaviour.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("\n\n")]
    [InlineData("ffmpeg: command not found")]
    public void Nothing_useful_produces_nothing(string listing)
    {
        Assert.Empty(FfmpegEncoders.Parse(listing));
    }
}
