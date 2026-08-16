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
/// <para>The shape being mirrored: one <c>-i</c> followed by N <c>-ss/-frames:v/-vf</c> output
/// groups, so ffmpeg opens and decodes the input exactly once for the whole strip (phase 145 found
/// the old N-separate-execs version was the dominant cost of a library import). Frame timestamps
/// are <c>interval * i</c> for <c>i = 1..count</c> with <c>interval = duration / (count + 1)</c> —
/// evenly spaced, never at 0 and never at the very end, where a frame can legitimately not
/// exist.</para>
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
        var args = new List<string> { "-i", inputPath };

        // Guard against a zero/negative duration (a probe that failed upstream, or a still image):
        // the interval would be 0 or negative and every frame would land at the same timestamp.
        // Falling back to 1s spacing yields a usable strip instead of N copies of frame 0.
        var interval = duration > 0 ? duration / (count + 1) : 1.0;

        for (var i = 1; i <= count; i++)
        {
            var t = (interval * i).ToString("F2", CultureInfo.InvariantCulture);
            args.Add("-ss");
            args.Add(t);
            args.Add("-frames:v");
            args.Add("1");
            args.Add("-vf");
            args.Add(ScaleFilter);
            args.Add(OutputName(i));
        }

        return args;
    }
}
