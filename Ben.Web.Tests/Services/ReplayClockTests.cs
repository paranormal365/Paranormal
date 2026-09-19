using System.Text.RegularExpressions;
using Ben.Web.Website.Library.Kit;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// A replay must run at the speed its label claims, and nothing may keep its own clock by
/// counting ticks.
/// </summary>
/// <remarks>
/// <para>The phone's replay was found on 2026-09-11 to advance its playhead by a fixed slice per
/// tick. A sleep promises "at least", never "exactly", so playback fell behind on a busy device:
/// a session labelled 8× ran slower than 8×, one-directionally, and nothing on the screen
/// disagreed because the readout, the scrubber and the trace all lagged together. The same fault
/// was then found in the two players on the web.</para>
///
/// <para><b>Why a source scan as well as arithmetic.</b> Both players keep time inside a Razor
/// component, in a private loop with no seam a unit test can reach. The arithmetic below pins
/// what the answer should be; the scan is the only thing that can say the players are asking.</para>
/// </remarks>
public sealed class ReplayClockTests
{
    // ── The arithmetic ───────────────────────────────────────────────────────

    [Fact]
    public void The_playhead_is_measured_not_counted()
    {
        var start = new DateTime(2026, 9, 11, 20, 0, 0, DateTimeKind.Utc);

        // 400ms of real time at 16× is 6.4 seconds of the session, however many ticks fired.
        var after = ReplayClock.Playhead(
            start, TimeSpan.FromMilliseconds(400), rate: 16, end: start.AddHours(1));
        Assert.Equal(6.4, (after - start).TotalSeconds, 3);

        // One slow tick covers exactly what several quick ones would have.
        var slow = ReplayClock.Playhead(
            start, TimeSpan.FromMilliseconds(900), rate: 1, end: start.AddHours(1));
        Assert.Equal(0.9, (slow - start).TotalSeconds, 3);
    }

    [Fact]
    public void The_playhead_never_runs_past_the_end_of_the_session()
    {
        // A starved ticker can wake long after the session finished. The clamp is what stops it
        // reporting a moment the night never had.
        var start = new DateTime(2026, 9, 11, 20, 0, 0, DateTimeKind.Utc);
        var end = start.AddSeconds(10);

        Assert.Equal(end, ReplayClock.Playhead(start, TimeSpan.FromMinutes(1), rate: 8, end: end));
    }

    // ── The scan ─────────────────────────────────────────────────────────────

    private static DirectoryInfo RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!;
    }

    /// <summary>
    /// Comments first, because this file's neighbours have fired on their own prose and the
    /// players necessarily describe the very thing being banned.
    /// </summary>
    private static string StripComments(string source)
    {
        var withoutBlocks = Regex.Replace(source, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);

        return string.Join('\n', withoutBlocks.Split('\n').Select(line =>
        {
            var razorComment = line.IndexOf("@*", StringComparison.Ordinal);
            if (razorComment >= 0) line = line[..razorComment];

            var slashes = line.IndexOf("//", StringComparison.Ordinal);
            return slashes >= 0 ? line[..slashes] : line;
        }));
    }

    private static IEnumerable<FileInfo> Sources()
    {
        var root = RepoRoot();
        string[] projects =
        [
            "Ben.Web.Website", "Ben.Web.Website.Library", "Ben.Web.Services",
            "Ben.Data.WebApi", "Ben.Video.Editor", "Ben.Wasm.Video",
        ];

        return projects
            .Select(p => new DirectoryInfo(Path.Combine(root.FullName, p)))
            .Where(d => d.Exists)
            .SelectMany(d => d.EnumerateFiles("*.*", SearchOption.AllDirectories))
            .Where(f => f.Extension is ".cs" or ".razor")
            .Where(f => !f.FullName.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.FullName.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));
    }

    /// <summary>A playhead that advances itself, in the two spellings that have turned up here.</summary>
    private static readonly (string Name, Regex Pattern)[] SelfAdvancing =
    [
        ("a playhead adding to itself",
            new Regex(@"(?i)\b([\w.]*(?:playhead|currenttime|position))\s*=\s*\1\s*\.\s*Add(?:Seconds|Milliseconds|Minutes)\s*\(")),
        ("a playhead assigned from itself plus a step",
            new Regex(@"(?i)=\s*([\w.]*(?:playhead|currenttime))\s*\+\s*[\w(]")),
    ];

    [Fact]
    public void No_playback_loop_keeps_time_by_counting_its_own_ticks()
    {
        var offences = new List<string>();

        // Only files with a delay loop in them: adding to a playhead is perfectly ordinary
        // everywhere else, and it is the loop that turns it into a clock.
        foreach (var file in Sources())
        {
            var text = StripComments(File.ReadAllText(file.FullName));
            if (!text.Contains("Task.Delay", StringComparison.Ordinal)) continue;

            foreach (var (name, pattern) in SelfAdvancing)
            {
                foreach (Match match in pattern.Matches(text))
                {
                    var line = text.Take(match.Index).Count(c => c == '\n') + 1;
                    offences.Add($"{file.Name}:{line} — {name}: {match.Value.Trim()}");
                }
            }
        }

        Assert.True(offences.Count == 0,
            $"""
             {offences.Count} playback loop(s) advance a playhead by a fixed step per tick.

             A tick is a delay plus whatever the loop does — a render, a map update, a round trip
             down the circuit — so it always takes longer than it asked for and never less. Adding
             a fixed slice per tick therefore plays slower than the speed on the button, and says
             nothing about it.

             Measure instead: anchor where the playhead is and when that was, then compute from
             the elapsed time on a monotonic clock. ReplayClock.Playhead and SlideshowClock.At do
             the arithmetic; Stopwatch.GetElapsedTime gives the elapsed time.

               {string.Join("\n  ", offences.Take(40))}
             """);
    }
}
