using Ben.Video.Core.SidecarContracts;
using Ben.Video.Editor.Services;
using Xunit;

namespace Ben.Video.Sidecar.Tests;

/// <summary>
/// Which encoder gets chosen, and how each one is asked for quality.
/// </summary>
/// <remarks>
/// The exporter assumed x264 and x265 throughout: it named them outright and passed their own
/// <c>-crf</c> and <c>-preset</c>. Those are the GPL part of an ffmpeg build, so a build licensed
/// for the Microsoft Store has neither, and an export would have failed twice over - first on the
/// missing encoder, then on options the replacement does not understand.
/// </remarks>
public sealed class VideoEncodersTests
{
    private static readonly string[] Gpl =
        ["libx264", "libx265", "libvpx-vp9", "libopenh264", "h264_mf", "hevc_mf", "libkvazaar"];

    /// <summary>What the permissively licensed Windows build actually offers — measured, not assumed.</summary>
    private static readonly string[] Lgpl =
        ["libvpx-vp9", "libopenh264", "h264_mf", "hevc_mf", "libkvazaar", "h264_nvenc", "h264_amf"];

    [Fact]
    public void With_a_GPL_build_nothing_changes()
    {
        Assert.Equal("libx264", VideoEncoders.Choose(ExportVideoCodec.H264, Gpl));
        Assert.Equal("libx265", VideoEncoders.Choose(ExportVideoCodec.H265, Gpl));
        Assert.Equal("libvpx-vp9", VideoEncoders.Choose(ExportVideoCodec.Vp9, Gpl));
    }

    [Fact]
    public void Knowing_nothing_about_the_build_also_changes_nothing()
    {
        // Every caller that has not probed keeps the behaviour it had before this existed.
        Assert.Equal("libx264", VideoEncoders.Choose(ExportVideoCodec.H264, null));
        Assert.Equal("libx265", VideoEncoders.Choose(ExportVideoCodec.H265, []));
    }

    [Fact]
    public void Without_x264_it_steps_down_to_something_that_is_there()
    {
        Assert.Equal("h264_mf", VideoEncoders.Choose(ExportVideoCodec.H264, Lgpl));
        Assert.Equal("libvpx-vp9", VideoEncoders.Choose(ExportVideoCodec.Vp9, Lgpl));
    }

    /// <summary>
    /// The only H.265 encoder in that build is libkvazaar, and it refuses any frame size that is
    /// not a multiple of 8 - measured on 2026-09-20, and the export dialog's own 480p preset
    /// (854x480) is such a size. An encoder that works for some presets and not others is worse
    /// than none, so H.265 is refused outright and the person is told to pick another format.
    /// </summary>
    [Fact]
    public void Without_x265_there_is_no_H265_and_it_says_what_to_do_instead()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => VideoEncoders.Choose(ExportVideoCodec.H265, Lgpl));

        Assert.Contains("H.264", ex.Message);
        Assert.Contains("VP9", ex.Message);
    }

    [Fact]
    public void OpenH264_is_the_floor_when_the_operating_system_offers_nothing()
    {
        Assert.Equal("libopenh264", VideoEncoders.Choose(ExportVideoCodec.H264, ["libopenh264", "libvpx-vp9"]));
    }

    /// <summary>
    /// ffmpeg lists the graphics-card encoders whether or not the machine has the hardware. On the
    /// box where this was written both h264_nvenc and h264_amf are listed and both write a
    /// zero-byte file, so choosing one from the list alone would hand somebody an empty export.
    /// </summary>
    [Fact]
    public void A_graphics_card_encoder_is_never_chosen_from_the_list_alone()
    {
        Assert.Throws<InvalidOperationException>(
            () => VideoEncoders.Choose(ExportVideoCodec.H264, ["h264_nvenc", "h264_amf", "h264_qsv"]));
    }

    /// <summary>Windows ships no HEVC encoder by default, so hevc_mf writes nothing.</summary>
    [Fact]
    public void The_Windows_HEVC_encoder_is_never_chosen()
    {
        Assert.Throws<InvalidOperationException>(
            () => VideoEncoders.Choose(ExportVideoCodec.H265, ["hevc_mf"]));
    }

    [Fact]
    public void A_build_with_no_encoder_at_all_says_so_plainly()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => VideoEncoders.Choose(ExportVideoCodec.H264, ["libvpx-vp9"]));

        Assert.Contains("H264", ex.Message);
        Assert.Contains("libx264", ex.Message);   // says what it looked for
    }

    // ── asking each encoder for quality, in its own language ─────────────────

    [Fact]
    public void Only_the_x264_family_is_given_crf_and_preset()
    {
        Assert.Equal(["-crf", "23"], VideoEncoders.RateControlArgs("libx264", useCrf: true, 23, 4000));
        Assert.Equal(["-preset", "slow"], VideoEncoders.PresetArgs("libx264", "slow"));
        Assert.Equal(["-crf", "28"], VideoEncoders.RateControlArgs("libx265", useCrf: true, 28, 4000));
    }

    /// <summary>
    /// The old code emitted a bare <c>-crf</c> for VP9. libvpx reads that as a ceiling on its
    /// default 256k bitrate rather than as a quality target, so the export came out far worse than
    /// asked for however low the number went. <c>-b:v 0</c> is what makes it mean quality.
    /// </summary>
    [Fact]
    public void VP9_gets_the_zero_bitrate_that_makes_crf_mean_quality()
    {
        Assert.Equal(["-crf", "31", "-b:v", "0"], VideoEncoders.RateControlArgs("libvpx-vp9", useCrf: true, 31, 4000));
    }

    [Fact]
    public void Media_Foundation_is_asked_on_its_own_scale_the_other_way_up()
    {
        var args = VideoEncoders.RateControlArgs("h264_mf", useCrf: true, 23, 4000);

        Assert.Equal("-rate_control", args[0]);
        Assert.Equal("quality", args[1]);
        Assert.Equal("-quality", args[2]);
        Assert.True(int.Parse(args[3]) is > 60 and < 90, "CRF 23 should land in the upper middle of 0-100");
        Assert.True(VideoEncoders.MediaFoundationQuality(18) > VideoEncoders.MediaFoundationQuality(35),
            "a lower CRF is better quality, and a higher Media Foundation number is better quality");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(51)]
    [InlineData(-5)]
    [InlineData(99)]
    public void The_quality_scale_never_leaves_the_range_it_is_allowed(int crf)
    {
        var q = VideoEncoders.MediaFoundationQuality(crf);
        Assert.InRange(q, 1, 100);
    }

    [Fact]
    public void Encoders_with_no_constant_quality_mode_get_the_chosen_bitrate()
    {
        // Rather than invent a CRF-to-bitrate curve nobody has measured.
        Assert.Equal(["-b:v", "4000k"], VideoEncoders.RateControlArgs("libopenh264", useCrf: true, 23, 4000));
        Assert.Equal(["-b:v", "6000k"], VideoEncoders.RateControlArgs("h264_videotoolbox", useCrf: true, 23, 6000));
        Assert.Equal(["-b:v", "4000k"], VideoEncoders.RateControlArgs("hevc_videotoolbox", useCrf: true, 23, 4000));
    }

    [Fact]
    public void Asking_for_a_bitrate_gives_a_bitrate_whatever_the_encoder()
    {
        foreach (var codec in new[] { "libx264", "libvpx-vp9", "h264_mf", "libopenh264" })
            Assert.Equal(["-b:v", "5000k"], VideoEncoders.RateControlArgs(codec, useCrf: false, 23, 5000));
    }

    /// <summary>
    /// An option an encoder does not know makes ffmpeg refuse the whole command, which the exec
    /// wrapper reports later as a missing output file.
    /// </summary>
    [Fact]
    public void No_encoder_is_handed_a_preset_it_would_refuse()
    {
        Assert.Empty(VideoEncoders.PresetArgs("libopenh264", "slow"));
        Assert.Empty(VideoEncoders.PresetArgs("h264_mf", "slow"));
        Assert.Empty(VideoEncoders.PresetArgs("libvpx-vp9", "slow"));
        Assert.Empty(VideoEncoders.PresetArgs("h264_videotoolbox", "slow"));
        Assert.Empty(VideoEncoders.PresetArgs("libx264", null));
    }
}
