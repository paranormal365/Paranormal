using System.Text.RegularExpressions;
using Ben.Video.Sidecar.Jobs;

namespace Ben.Video.Sidecar.Tests;

/// <summary>
/// Item #70 phase 159. Unlike segment rendering — where the sidecar and the browser literally share
/// <c>ExportArgBuilders</c> via <c>InternalsVisibleTo</c>, so parity is free — the thumbnail argv
/// lives in JavaScript (<c>ffmpegInterop.js extractThumbnails</c>) and cannot be shared. These
/// tests are the substitute for that shared code: they pin the exact shape the JS produces, and
/// <see cref="Argv_MatchesTheJavaScriptImplementation"/> reads the real JS file so the two can't
/// silently drift.
/// </summary>
public sealed class ThumbnailArgvFactoryTests
{
    /// <summary>
    /// Each frame seeks before its own input and decodes keyframes only.
    /// </summary>
    /// <remarks>
    /// There used to be one input with the seeks after it, which makes them output-side seeks:
    /// ffmpeg decodes from the beginning and discards frames until it reaches the timestamp, so a
    /// half-hour clip was decoded once per thumbnail (2026-09-05 audit, media-1).
    /// </remarks>
    [Fact]
    public void Build_SeeksBeforeEachInputAndDecodesKeyframesOnly()
    {
        var args = ThumbnailArgvFactory.Build("/src/clip.mp4", count: 3, duration: 12.0);

        Assert.Equal(3, args.Count(a => a == "-i"));
        Assert.Equal(3, args.Count(a => a == "-frames:v"));
        Assert.Equal(3, args.Count(a => a == "-skip_frame"));

        // Every -ss precedes its own -i.
        for (var i = 0; i < args.Count; i++)
        {
            if (args[i] != "-ss") continue;
            Assert.Equal("-i", args[i + 2]);
            Assert.Equal("/src/clip.mp4", args[i + 3]);
        }
    }

    /// <summary>
    /// With several inputs, an output with no map takes its picture from input 0 — so all three
    /// thumbnails would be the same frame.
    /// </summary>
    [Fact]
    public void Build_MapsEachOutputToItsOwnInput()
    {
        var args = ThumbnailArgvFactory.Build("/src/clip.mp4", count: 3, duration: 12.0);

        var maps = new List<string>();
        for (var i = 0; i < args.Count - 1; i++)
            if (args[i] == "-map") maps.Add(args[i + 1]);

        Assert.Equal(["0:v:0", "1:v:0", "2:v:0"], maps);
    }

    [Fact]
    public void Build_TimestampsAreEvenlySpacedAndNeverAtZeroOrEnd()
    {
        // interval = duration / (count + 1) — frames at 3, 6, 9 for a 12s clip and 3 frames.
        // Never 0 (black leader) and never duration (a frame that may not exist).
        var args = ThumbnailArgvFactory.Build("/src/clip.mp4", count: 3, duration: 12.0);
        var timestamps = TimestampsFrom(args);

        Assert.Equal(["3.00", "6.00", "9.00"], timestamps);
    }

    [Fact]
    public void Build_ZeroDuration_FallsBackToOneSecondSpacing()
    {
        // A failed upstream probe would otherwise put every frame at t=0, yielding N copies of the
        // same image instead of a usable strip.
        var args = ThumbnailArgvFactory.Build("/src/clip.mp4", count: 3, duration: 0);

        Assert.Equal(["1.00", "2.00", "3.00"], TimestampsFrom(args));
    }

    [Fact]
    public void Build_OutputNamesAreFlatAndDeterministic()
    {
        // Flat names matter: they're resolved against the job's own working directory and are the
        // authorization list for the per-file result endpoint, so they must contain no path
        // separators of any kind.
        var args = ThumbnailArgvFactory.Build("/src/clip.mp4", count: 2, duration: 10);
        var outputs = args.Where(a => a.EndsWith(".webp", StringComparison.Ordinal)).ToList();

        Assert.Equal(["thumb_1.webp", "thumb_2.webp"], outputs);
        Assert.All(outputs, o => Assert.DoesNotContain('/', o));
        Assert.All(outputs, o => Assert.DoesNotContain('\\', o));
    }

    /// <summary>
    /// Reads the real <c>ffmpegInterop.js</c> and asserts the browser still builds the same argv
    /// this factory does. If someone changes the JS scale filter, the per-frame flags, or the
    /// interval formula without changing the C#, this fails — which is the entire reason it exists.
    /// </summary>
    [Fact]
    public void Argv_MatchesTheJavaScriptImplementation()
    {
        var js = File.ReadAllText(ResolveJsPath());
        var body = ExtractFunction(js, "extractThumbnails");

        // Same interval formula.
        Assert.Contains("duration / (count + 1)", body);
        // Same per-frame input and output shapes, and the same fixed scale.
        Assert.Contains("'-skip_frame', 'nokey', '-ss', t, '-i', inputName", body);
        Assert.Contains("'-frames:v', '1', '-vf', 'scale=160:-1'", body);
        Assert.Contains("'-map', `${i - 1}:v:0`", body);
        Assert.Contains(ThumbnailArgvFactory.ScaleFilter, body);
        // Same 2-decimal timestamp formatting the C# uses via "F2".
        Assert.Contains("toFixed(2)", body);
        // Same output extension.
        Assert.Contains(ThumbnailArgvFactory.FileExtension, body);
    }

    private static List<string> TimestampsFrom(IReadOnlyList<string> args)
    {
        var result = new List<string>();
        for (var i = 0; i < args.Count - 1; i++)
            if (args[i] == "-ss") result.Add(args[i + 1]);
        return result;
    }

    private static string ExtractFunction(string js, string name)
    {
        var start = js.IndexOf($"export async function {name}", StringComparison.Ordinal);
        Assert.True(start >= 0, $"{name} not found in ffmpegInterop.js — did it get renamed?");
        var next = js.IndexOf("\nexport ", start + 1, StringComparison.Ordinal);
        return next < 0 ? js[start..] : js[start..next];
    }

    private static string ResolveJsPath()
    {
        // Walk up from the test binary to the repo root, then into the editor's wwwroot.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "Ben.Video.Editor")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        var path = Path.Combine(dir!.FullName, "Ben.Video.Editor", "wwwroot", "js", "ffmpegInterop.js");
        Assert.True(File.Exists(path), $"ffmpegInterop.js not found at {path}");
        return path;
    }
}
