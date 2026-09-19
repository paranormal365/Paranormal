using Ben.Video.Editor.Models;
using Ben.Video.Editor.Services;

namespace Ben.Video.Tests.Services;

/// <summary>
/// What "Source resolution" means, and what the finished file is written as.
/// </summary>
public sealed class ExportCanvasTests
{
    /// <summary>
    /// The first entry in the export dialog's resolution list promised not to touch the picture
    /// and stored an empty string, which every reader turned into 1920x1080 — so footage from a
    /// phone, a DVR or a 4K camera was rescaled to Full HD by the one option that said it would
    /// not (2026-09-05 audit, export-5).
    /// </summary>
    [Fact]
    public void Source_resolution_means_the_sources_own_size()
    {
        Assert.Equal((3840, 2160), ExportCanvas.Resolve("", 3840, 2160));
        Assert.Equal((1080, 1920), ExportCanvas.Resolve(null, 1080, 1920));
    }

    [Fact]
    public void A_chosen_resolution_wins_over_the_source()
    {
        Assert.Equal((1280, 720), ExportCanvas.Resolve("1280x720", 3840, 2160));
    }

    [Fact]
    public void Full_HD_is_the_answer_only_when_nothing_else_is_known()
    {
        Assert.Equal((1920, 1080), ExportCanvas.Resolve(""));
        Assert.Equal((1920, 1080), ExportCanvas.Resolve("not a size"));
        Assert.Equal((1920, 1080), ExportCanvas.Resolve("0x0", 0, 0));
    }

    /// <summary>
    /// H.264 and H.265 in 4:2:0 cannot encode an odd dimension, so a source one pixel short of
    /// even would fail the encode rather than the check.
    /// </summary>
    [Fact]
    public void An_odd_dimension_is_rounded_down_to_even()
    {
        Assert.Equal((1918, 1078), ExportCanvas.Resolve("", 1919, 1079));
    }
}

/// <summary>
/// The finished render is written into the container the person chose.
/// </summary>
/// <remarks>
/// The pipeline works in mp4 intermediates and the last step used to be a rename, so choosing WebM
/// produced an MP4 file called .webm — right codecs, wrong container, and whether it played at all
/// came down to how forgiving the player was (2026-09-05 audit, export-14).
/// </remarks>
public sealed class ContainerArgsTests
{
    private static ExportSettings Settings(string format, string codec = "libx264") =>
        new() { OutputFormat = format, VideoCodec = codec };

    [Fact]
    public void Nothing_is_re_encoded()
    {
        var args = ExportArgBuilders.BuildContainerArgs("in.mp4", "out.webm", Settings("webm", "libvpx-vp9"));

        Assert.Contains("-c", args);
        Assert.Equal("copy", args[Array.IndexOf(args, "-c") + 1]);
        Assert.Equal("out.webm", args[^1]);
    }

    /// <summary>
    /// H.265 in an MP4 is tagged hev1 by default, which QuickTime, Safari and Apple hardware
    /// decline to play. It is the same bytes either way.
    /// </summary>
    [Fact]
    public void H265_in_an_mp4_is_tagged_so_players_will_open_it()
    {
        var args = ExportArgBuilders.BuildContainerArgs("in.mp4", "out.mp4", Settings("mp4", "libx265"));

        Assert.Contains("-tag:v", args);
        Assert.Equal("hvc1", args[Array.IndexOf(args, "-tag:v") + 1]);
    }

    [Fact]
    public void H264_needs_no_tag()
    {
        var args = ExportArgBuilders.BuildContainerArgs("in.mp4", "out.mp4", Settings("mp4"));

        Assert.DoesNotContain("-tag:v", args);
    }

    /// <summary>
    /// The index moves to the front so a browser can start playing before the download finishes.
    /// </summary>
    [Theory]
    [InlineData("mp4")]
    [InlineData("mov")]
    public void An_mp4_family_container_starts_playing_before_it_finishes_downloading(string format)
    {
        var args = ExportArgBuilders.BuildContainerArgs("in.mp4", $"out.{format}", Settings(format));

        Assert.Contains("-movflags", args);
        Assert.Equal("+faststart", args[Array.IndexOf(args, "-movflags") + 1]);
    }

    [Fact]
    public void WebM_gets_neither_flag()
    {
        var args = ExportArgBuilders.BuildContainerArgs("in.mp4", "out.webm", Settings("webm", "libvpx-vp9"));

        Assert.DoesNotContain("-movflags", args);
        Assert.DoesNotContain("-tag:v", args);
    }
}

/// <summary>
/// One frame, saved as a picture.
/// </summary>
/// <remarks>
/// For a site whose members cut evidence reels, the frame where something appears is the artefact
/// that actually gets shared, and the editor could only produce video (2026-09-05 audit, the
/// completeness critic's list).
/// </remarks>
public sealed class StillFrameArgsTests
{
    [Fact]
    public void Exactly_one_frame_is_written()
    {
        var args = ExportArgBuilders.BuildStillFrameArgs("clip.mp4", "frame.png", 12.5);

        Assert.Contains("-frames:v", args);
        Assert.Equal("1", args[Array.IndexOf(args, "-frames:v") + 1]);
        Assert.Equal("frame.png", args[^1]);
    }

    /// <summary>
    /// Seeking before the input means ffmpeg decodes to the frame and stops, instead of decoding
    /// everything before it first — the difference between instant and a wait proportional to how
    /// far into the clip you are.
    /// </summary>
    [Fact]
    public void Seeking_happens_before_the_input()
    {
        var args = ExportArgBuilders.BuildStillFrameArgs("clip.mp4", "frame.png", 12.5);

        Assert.True(Array.IndexOf(args, "-ss") < Array.IndexOf(args, "-i"));
        Assert.Equal("12.500", args[Array.IndexOf(args, "-ss") + 1]);
    }

    [Fact]
    public void A_negative_time_is_the_beginning()
    {
        var args = ExportArgBuilders.BuildStillFrameArgs("clip.mp4", "frame.png", -3);

        Assert.Equal("0.000", args[Array.IndexOf(args, "-ss") + 1]);
    }
}

/// <summary>
/// No encoder is ever handed an odd width or height.
/// </summary>
/// <remarks>
/// H.264 and H.265 in 4:2:0 cannot encode one. A 1007x675 photo — an ordinary screenshot or phone
/// crop — was passed to the preview as its own canvas and ffmpeg aborted; in the browser that
/// abort showed up as nothing at all, with the preview still showing the timeline as it had been
/// before the picture was added. Found on screen while verifying the resolution work (2026-09-05
/// audit, alongside export-5).
/// </remarks>
public sealed class EvenCanvasTests
{
    private static ExportSettings Settings() =>
        new() { VideoCodec = "libx264", UseCrf = true, Crf = 23, PixelFormat = "yuv420p" };

    [Theory]
    [InlineData(1007, 675, "1006:674")]
    [InlineData(1920, 1080, "1920:1080")]
    [InlineData(1919, 1080, "1918:1080")]
    public void An_image_segment_scales_to_an_even_canvas(int w, int h, string expected)
    {
        var args = ExportArgBuilders.BuildImageSegmentArgs(
            "img.png", "seg.mp4", 5.0, Settings(), outputWidth: w, outputHeight: h);
        var vf = args[Array.IndexOf(args, "-vf") + 1];

        Assert.Contains($"scale={expected}:force_original_aspect_ratio=decrease", vf);
        Assert.Contains($"pad={expected}:(ow-iw)/2:(oh-ih)/2", vf);
    }

    [Fact]
    public void A_video_segment_scales_to_an_even_canvas()
    {
        var args = ExportArgBuilders.BuildTrimArgs(
            "in.mp4", "out.mp4", 0, 5, 1.0, Settings(), outputWidth: 1007, outputHeight: 675);
        var vf = args[Array.IndexOf(args, "-filter:v") + 1];

        Assert.Contains("scale=1006:674", vf);
    }

    [Fact]
    public void A_gap_filler_is_rendered_at_an_even_canvas()
    {
        var args = ExportArgBuilders.BuildFillerSegmentArgs("gap.mp4", 1.5, Settings(), 1007, 675);

        Assert.Contains("color=c=black:s=1006x674:r=30", args);
    }

    [Fact]
    public void An_unknown_canvas_stays_unknown_rather_than_becoming_minus_one()
    {
        Assert.Equal((0, 0), ExportArgBuilders.EvenCanvas(0, 0));
    }
}
