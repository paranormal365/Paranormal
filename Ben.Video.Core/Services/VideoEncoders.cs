using Ben.Video.Core.SidecarContracts;

namespace Ben.Video.Editor.Services;

/// <summary>
/// Which encoder to ask ffmpeg for, and how to ask that particular encoder for quality.
/// </summary>
/// <remarks>
/// <para><b>Why this exists.</b> The exporter has always assumed x264 and x265: it names
/// <c>libx264</c> outright and passes x264's own <c>-crf</c> and <c>-preset</c>. Those two encoders
/// are the GPL part of an ffmpeg build, so a build licensed for the Microsoft Store does not have
/// them, and an export would fail with "Unknown encoder". The replacements exist in that build and
/// do produce video - measured on 2026-09-20 - but none of them accepts <c>-crf</c> or
/// <c>-preset</c>, so swapping the name alone produces a different failure.</para>
///
/// <para>So the rule is written against <b>what the bundled ffmpeg actually offers</b> rather than
/// against an operating system or a licence. One sidecar works with either build: it keeps x264
/// where x264 is there, and steps down where it is not.</para>
/// </remarks>
public static class VideoEncoders
{
    /// <summary>
    /// Encoders for H.264, best first.
    /// </summary>
    /// <remarks>
    /// x264 first because it is the quality bar the presets were written for. Then the operating
    /// system's own encoders, which are hardware-backed and whose patent position belongs to the
    /// platform vendor rather than to us. OpenH264 last of the three: it is BSD and works on any
    /// machine, which makes it the dependable floor.
    ///
    /// The graphics-card encoders are deliberately NOT here. ffmpeg lists them whether or not the
    /// machine has the hardware - on the box where this was written, h264_nvenc and h264_amf are
    /// both listed and both produce a zero-byte file - so choosing one from the list alone would
    /// hand somebody an empty export.
    /// </remarks>
    private static readonly string[] H264 = ["libx264", "h264_videotoolbox", "h264_mf", "libopenh264"];

    /// <summary>
    /// Encoders for H.265, best first.
    /// </summary>
    /// <remarks>
    /// <para>hevc_mf is absent on purpose. Windows ships no HEVC encoder by default - the one that
    /// would back it is a paid add-on - so ffmpeg lists hevc_mf on a machine that cannot use it,
    /// and it writes a zero-byte file.</para>
    ///
    /// <para>libkvazaar is absent for a harder reason, found by running it on 2026-09-20: it
    /// refuses any frame size that is not a multiple of 8, with "Video dimensions are not a
    /// multiple of 8". The export dialog's own 480p preset is 854x480, and "source resolution" can
    /// be anything at all, so an encoder that works for some of the presets and not others would
    /// fail unpredictably - worse than not offering it. x265 has no such rule, which is why this
    /// never came up before.</para>
    ///
    /// <para>So on a build without x265 there is no H.265 at all, and <see cref="Choose"/> says so
    /// plainly. H.264 and VP9 both work on such a build - both measured the same day.</para>
    /// </remarks>
    private static readonly string[] H265 = ["libx265", "hevc_videotoolbox"];

    /// <summary>Encoders for VP9. libvpx is BSD, so it is in every build already.</summary>
    private static readonly string[] Vp9 = ["libvpx-vp9"];

    /// <summary>Encoders that understand x264's <c>-crf</c> and <c>-preset</c>.</summary>
    private static readonly HashSet<string> X264Style = new(StringComparer.Ordinal) { "libx264", "libx265" };

    /// <summary>
    /// The best encoder for <paramref name="family"/> that <paramref name="available"/> contains.
    /// </summary>
    /// <param name="family">What the person asked for: H.264, H.265 or VP9.</param>
    /// <param name="available">Encoder names this ffmpeg reports. Empty or null means "unknown",
    /// and the first choice is returned unchecked - which keeps every existing caller behaving
    /// exactly as it did before anything probed anything.</param>
    /// <exception cref="InvalidOperationException">The build has none of them, which is a broken
    /// bundle rather than a bad request, and is worth saying plainly.</exception>
    public static string Choose(ExportVideoCodec family, IReadOnlyCollection<string>? available)
    {
        var candidates = family switch
        {
            ExportVideoCodec.H264 => H264,
            ExportVideoCodec.H265 => H265,
            ExportVideoCodec.Vp9  => Vp9,
            _ => throw new InvalidOperationException($"Unknown video codec '{family}'."),
        };

        if (available is null || available.Count == 0) return candidates[0];

        foreach (var c in candidates)
            if (available.Contains(c))
                return c;

        // Worth being specific: the person chose a format in the export dialog and can choose
        // another one, so the message should tell them that rather than only naming what is missing.
        throw new InvalidOperationException(
            $"This build of ffmpeg cannot encode {family} — it has none of: {string.Join(", ", candidates)}. "
            + "Choose H.264 or VP9 instead.");
    }

    /// <summary>
    /// How to ask <paramref name="codec"/> for the quality wanted.
    /// </summary>
    /// <remarks>
    /// <para><b>Constant quality is not universal.</b> Only the x264 family takes <c>-crf</c>.
    /// Media Foundation has its own quality scale, 0 to 100 and the other way up. OpenH264 and
    /// Kvazaar have no constant-quality mode worth mapping onto a CRF number, so they get the
    /// bitrate the person already chose. That is the honest answer: rather than invent a
    /// CRF-to-bitrate curve nobody has measured, use the number the export settings already carry.</para>
    ///
    /// <para><b>VP9 needs <c>-b:v 0</c> alongside <c>-crf</c>.</b> Without it libvpx treats the CRF
    /// as a ceiling on a default 256k bitrate rather than as a quality target, so the export comes
    /// out far worse than asked for however low the CRF is set. The old code emitted a bare
    /// <c>-crf</c> for VP9, so this also fixes that.</para>
    /// </remarks>
    public static IReadOnlyList<string> RateControlArgs(string codec, bool useCrf, int crf, int bitrateKbps)
    {
        var bitrate = new[] { "-b:v", $"{bitrateKbps}k" };

        if (!useCrf) return bitrate;

        if (X264Style.Contains(codec)) return ["-crf", crf.ToString()];

        if (codec == "libvpx-vp9") return ["-crf", crf.ToString(), "-b:v", "0"];

        if (codec is "h264_mf" or "hevc_mf")
            return ["-rate_control", "quality", "-quality", MediaFoundationQuality(crf).ToString()];

        // libopenh264, videotoolbox and anything added later: bitrate, which they all take.
        return bitrate;
    }

    /// <summary>
    /// The speed preset, for the encoders that have one.
    /// </summary>
    /// <remarks>
    /// The preset names the export settings carry - ultrafast through veryslow - are x264's own,
    /// and nothing else in <see cref="H264"/> or <see cref="H265"/> has anything of the kind.
    /// Passing an unknown option makes ffmpeg refuse the whole command, so silence is correct
    /// everywhere else.
    /// </remarks>
    public static IReadOnlyList<string> PresetArgs(string codec, string? preset)
    {
        if (string.IsNullOrEmpty(preset)) return [];
        if (X264Style.Contains(codec)) return ["-preset", preset];
        return [];
    }

    /// <summary>
    /// CRF (0 best, 51 worst) onto Media Foundation's quality (100 best, 0 worst).
    /// </summary>
    /// <remarks>
    /// A straight inversion over the range people actually use. CRF 18 is "visually lossless" and
    /// lands near 90; CRF 23, the default, lands near 74; CRF 35, the rough-preview pass, near 40.
    /// Clamped because the scales do not share endpoints and an out-of-range value is refused.
    /// </remarks>
    internal static int MediaFoundationQuality(int crf)
    {
        var q = 100 - (int)Math.Round(crf * 1.7);
        return Math.Clamp(q, 1, 100);
    }
}
