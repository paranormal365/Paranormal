using Ben.Web.Services;
using Jint;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// The browser's ticker (wwwroot/js/eventCountdown.js) says what the server says (10/16/2026).
/// </summary>
/// <remarks>
/// <para>The server draws every countdown right for the moment the page is made; the ticker keeps
/// it moving. If the two disagreed, a row would jump on the first tick — "2d 04h 12m" becoming
/// "52h 12m" — so the ticker's arithmetic is run here under Jint, as the page loads it, against
/// <see cref="CountdownMath"/>.</para>
/// </remarks>
public sealed class EventCountdownScriptTests
{
    private static Engine Harness()
    {
        var module = File.ReadAllText(Path.Combine(RepoRoot(), "Ben.Web.Website.Library", "wwwroot", "js", "eventCountdown.js"));
        var engine = new Engine();
        engine.Modules.Add("countdown", module);
        engine.Modules.Add("harness", """
            import { stepAt, remaining, progress, cells } from 'countdown';
            globalThis.wordsAt = (json, now) => stepAt(JSON.parse(json), now)[2];
            globalThis.phaseAt = (json, now) => stepAt(JSON.parse(json), now)[1];
            globalThis.remaining = remaining;
            globalThis.progress = progress;
            globalThis.cells = (ms, s) => cells(ms, s).join(',');
            """);
        engine.Modules.Import("harness");
        return engine;
    }

    private static double Ms(DateTime utc) => utc == DateTime.MinValue ? 0 : (utc - DateTime.UnixEpoch).TotalMilliseconds;

    private static string StepsJson(IReadOnlyList<CountdownStep> steps)
        => System.Text.Json.JsonSerializer.Serialize(steps.Select(s => new object[] { Ms(s.FromUtc), s.Phase.ToString(), s.Words }));

    [Theory]
    [InlineData(2 * 86400 + 4 * 3600 + 12 * 60 + 7, true)]
    [InlineData(86400, true)]
    [InlineData(3 * 3600 + 5 * 60 + 59, true)]
    [InlineData(9 * 60 + 5, true)]
    [InlineData(9 * 60 + 5, false)]
    [InlineData(20, false)]
    [InlineData(0, true)]
    public void The_digits_match_the_server(int seconds, bool withSeconds)
    {
        var js = Harness();
        var expected = CountdownMath.Remaining(TimeSpan.FromSeconds(seconds), withSeconds);
        Assert.Equal(expected, js.Invoke("remaining", seconds * 1000.0, withSeconds).AsString());
    }

    /// <summary>The steps are the server's; the ticker only picks the one whose moment has come.</summary>
    [Fact]
    public void The_words_follow_the_servers_steps_across_the_clock_change()
    {
        var start = new DateTime(2026, 11, 2, 1, 0, 0, DateTimeKind.Utc);   // Sun 11/01 7:00 PM CST
        var steps = CountdownMath.Steps(start, start.AddHours(3), "America/Chicago", start.AddDays(-20));
        var json = StepsJson(steps);
        var js = Harness();

        foreach (var now in new[]
                 {
                     start.AddDays(-20), start.AddDays(-3), new DateTime(2026, 11, 1, 4, 59, 0, DateTimeKind.Utc),
                     new DateTime(2026, 11, 1, 5, 0, 0, DateTimeKind.Utc), start.AddMinutes(-1), start, start.AddHours(3),
                 })
        {
            var server = CountdownMath.At(steps, now);
            Assert.Equal(server.Words, js.Invoke("wordsAt", json, Ms(now)).AsString());
            Assert.Equal(server.Phase.ToString(), js.Invoke("phaseAt", json, Ms(now)).AsString());
        }
    }

    [Theory]
    [InlineData(-10.0)]
    [InlineData(0.0)]
    [InlineData(0.25)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    [InlineData(1.5)]
    public void The_bar_matches_the_server(double fraction)
    {
        var baseline = new DateTime(2026, 10, 15, 0, 0, 0, DateTimeKind.Utc);
        var start = new DateTime(2026, 10, 25, 0, 0, 0, DateTimeKind.Utc);
        var now = baseline + (start - baseline) * fraction;

        var expected = CountdownMath.Progress(baseline, start, now);
        Assert.Equal(expected, Harness().Invoke("progress", Ms(baseline), Ms(start), Ms(now)).AsNumber(), 9);
    }

    [Theory]
    [InlineData(2 * 86400, true, "d,h,m")]
    [InlineData(5 * 3600, true, "h,m")]
    [InlineData(30 * 60, true, "m,s")]       // seconds only in the last hour
    [InlineData(30 * 60, false, "m")]        // and never for less motion
    public void The_tiles_show_days_only_while_there_are_any_and_seconds_only_at_the_end(int seconds, bool withSeconds, string expected)
        => Assert.Equal(expected, Harness().Invoke("cells", seconds * 1000.0, withSeconds).AsString());

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
