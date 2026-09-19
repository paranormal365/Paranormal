using System.Globalization;

namespace Ben.Video.Sidecar.Jobs;

/// <summary>
/// Builds the one-exec, N-output thumbnail argv — item #70 phase 159.
///
/// <para><b>Parity with <c>ffmpegInterop.js</c>'s <c>extractThumbnails</c> is the whole contract
/// here</b>, and unlike segment rendering it can't be guaranteed by sharing code: that argv lives
/// in JavaScript, not in <c>ExportArgBuilders</c>, so there is nothing for the sidecar to reuse via
/// <c>InternalsVisibleTo</c>. The two implementations are kept in lock-step by a fixture test
/// instead (<c>ThumbnailArgvFactoryTests</c>), and any change to either must update both.</para>
///
/// <para>The shape being mirrored: one input per frame, each with its seek BEFORE it and
/// <c>-skip_frame nokey</c>, then one mapped output per input. The seeks used to sit after a single
/// <c>-i</c>, which makes them output-side seeks — ffmpeg decodes from the beginning and discards
/// frames until it reaches the timestamp, so a half-hour clip was decoded once per thumbnail
/// (2026-09-05 audit, media-1). An input-side seek jumps straight there, and decoding only
/// keyframes on the way makes each jump cheap. The frames land on the nearest keyframe rather than
/// the exact timestamp, which in a 160-pixel-wide filmstrip is a difference nobody can see.</para>
///
/// <para>The explicit <c>-map</c> per output is not optional once there are several inputs: without
/// one, every output would take its picture from input 0.</para>
///
/// <para>Frame timestamps are <c>interval * i</c> for <c>i = 1..count</c> with
/// <c>interval = duration / (count + 1)</c> — evenly spaced, never at 0 and never at the very end,
/// where a frame can legitimately not exist.</para>
/// </summary>
public static class ThumbnailArgvFactory
{
    /// <summary>Fixed server-side rather than sent over the wire — the browser has no reason to
    /// choose it, and a wire parameter would be one more thing to validate. Matches the JS.</summary>
    public const string ScaleFilter = "scale=160:-1";

    public const string FileExtension = ".webp";

    /// <summary>Deterministic per-index output name. Flat (no directory component) because it's
    /// resolved against the job's own working directory, and validated on the way back out in
    /// <c>JobEndpoints</c> against the recorded manifest rather than trusted from a request.</summary>
    public static string OutputName(int index) => $"thumb_{index}{FileExtension}";

    public static IReadOnlyList<string> Build(string inputPath, int count, double duration)
    {
        // Guard against a zero/negative duration (a probe that failed upstream, or a still image):
        // the interval would be 0 or negative and every frame would land at the same timestamp.
        // Falling back to 1s spacing yields a usable strip instead of N copies of frame 0.
        var interval = duration > 0 ? duration / (count + 1) : 1.0;

        var inputs  = new List<string>();
        var outputs = new List<string>();

        for (var i = 1; i <= count; i++)
        {
            var t = (interval * i).ToString("F2", CultureInfo.InvariantCulture);

            inputs.AddRange(["-skip_frame", "nokey", "-ss", t, "-i", inputPath]);
            outputs.AddRange(["-map", $"{i - 1}:v:0", "-frames:v", "1", "-vf", ScaleFilter, OutputName(i)]);
        }

        return [.. inputs, .. outputs];
    }
}
