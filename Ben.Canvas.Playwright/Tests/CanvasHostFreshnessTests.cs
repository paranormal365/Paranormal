using System.Text.RegularExpressions;

namespace Ben.Canvas.Playwright.Tests;

/// <summary>
/// Fails with one clear sentence when the canvas host is serving an older build than the one on disk.
/// </summary>
/// <remarks>
/// Copied from Ben.Web.Playwright/Tests/EditorHostFreshnessTests.cs. A host started before the last
/// rebuild produces a wall of unrelated-looking failures; this names the cause first. Blazor
/// fingerprints its entry script, so the name the running host serves is compared with the name this
/// checkout built. A missing build or an unreachable host is Inconclusive, not red.
/// </remarks>
[TestFixture]
[Order(-1)]
[Category("Shell")]
public sealed class CanvasHostFreshnessTests
{
    private static string CanvasUrl => (Environment.GetEnvironmentVariable("BEN_CANVAS_URL") ?? "http://localhost:5125").TrimEnd('/');

    private static readonly Regex EntryScript = new(@"blazor\.webassembly\.[a-z0-9]{6,}\.js", RegexOptions.Compiled);

    [Test]
    public async Task The_canvas_host_is_serving_the_build_that_is_on_disk()
    {
        if (new Uri(CanvasUrl).Host is not ("localhost" or "127.0.0.1"))
            Assert.Ignore("A deployed site has no local build to compare with.");

        var root = FindMessengerRoot();
        if (root is null) Assert.Inconclusive("Could not locate the folder holding Ben.Wasm.Canvas.");

        var frameworkDir = Path.Combine(root!, "Ben.Wasm.Canvas", "bin", "Debug", "net10.0", "wwwroot", "_framework");
        if (!Directory.Exists(frameworkDir))
            Assert.Inconclusive("Ben.Wasm.Canvas has not been built in this checkout - nothing to compare against.");

        var onDisk = Directory.EnumerateFiles(frameworkDir, "blazor.webassembly.*.js")
            .Select(Path.GetFileName)
            .Where(n => n is not null && EntryScript.IsMatch(n))
            .ToList();
        if (onDisk.Count == 0) Assert.Inconclusive("No fingerprinted entry script in the build output.");

        string html;
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            html = await http.GetStringAsync(CanvasUrl + "/");
        }
        catch (Exception ex)
        {
            Assert.Inconclusive($"The canvas host at {CanvasUrl} is not reachable ({ex.GetType().Name}).");
            return;
        }

        var served = EntryScript.Matches(html).Select(m => m.Value).ToHashSet(StringComparer.Ordinal);
        if (served.Count == 0) Assert.Inconclusive($"{CanvasUrl} served a page with no fingerprinted entry script.");

        Assert.That(served.Any(onDisk.Contains!), Is.True,
            $"""
             The canvas host on 5125 is older than the last build; restart dotnet run --project Ben.Wasm.Canvas.

             Serving : {string.Join(", ", served)}
             On disk : {string.Join(", ", onDisk)}
             """);
    }

    private static string? FindMessengerRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "Ben.Wasm.Canvas")))
            dir = dir.Parent;
        return dir?.FullName;
    }
}
