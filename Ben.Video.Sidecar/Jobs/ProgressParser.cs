using System.Globalization;
using System.Text.RegularExpressions;

namespace Ben.Video.Sidecar.Jobs;

/// <summary>
/// Turns one line of ffmpeg stdout into a 0-99 progress percentage against a known total
/// duration. Understands two formats: the machine-readable <c>-progress pipe:1</c> output
/// (<c>out_time_ms=</c>, what <see cref="SegmentJobRunner"/> actually requests from real ffmpeg)
/// and the classic human-readable <c>time=HH:MM:SS.ms</c> stats line (what
/// <c>Ben.Video.Sidecar.FakeFfmpeg</c> emits, and what real ffmpeg still prints if
/// <c>-progress</c> is ever dropped) — supporting both means test coverage against the fake
/// exercises the same parser real usage depends on. Deliberately caps at 99, not 100: ffmpeg can
/// still be muxing/flushing after the last progress tick, and 100% should mean "the job actually
/// finished successfully," which <see cref="SegmentJobRunner"/> sets explicitly on completion.
/// </summary>
public static partial class ProgressParser
{
    [GeneratedRegex(@"out_time_ms=(\d+)")]
    private static partial Regex OutTimeMsPattern();

    [GeneratedRegex(@"time=(\d+):(\d+):(\d+(?:\.\d+)?)")]
    private static partial Regex ClassicTimePattern();

    public static int? TryParsePercent(string line, double totalDurationSeconds)
    {
        if (totalDurationSeconds <= 0) return null;

        var outTimeMatch = OutTimeMsPattern().Match(line);
        if (outTimeMatch.Success && long.TryParse(outTimeMatch.Groups[1].Value, out var micros))
            return ToPercent(micros / 1_000_000.0, totalDurationSeconds);

        var classicMatch = ClassicTimePattern().Match(line);
        if (!classicMatch.Success) return null;

        var hours = double.Parse(classicMatch.Groups[1].Value, CultureInfo.InvariantCulture);
        var minutes = double.Parse(classicMatch.Groups[2].Value, CultureInfo.InvariantCulture);
        var seconds = double.Parse(classicMatch.Groups[3].Value, CultureInfo.InvariantCulture);
        return ToPercent(hours * 3600 + minutes * 60 + seconds, totalDurationSeconds);
    }

    private static int ToPercent(double elapsedSeconds, double totalDurationSeconds) =>
        (int)Math.Clamp(Math.Round(elapsedSeconds / totalDurationSeconds * 100), 0, 99);
}
