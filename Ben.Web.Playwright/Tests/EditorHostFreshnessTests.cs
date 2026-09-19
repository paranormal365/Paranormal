using NUnit.Framework;
using System.Text.RegularExpressions;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// Fails with ONE clear sentence when the video-editor host is serving an older build than the
/// one on disk — instead of the nine unrelated-looking editor failures a stale host actually
/// produces (item 178's first finding; the recurring trap).
/// </summary>
/// <remarks>
/// <para><b>How it knows.</b> Blazor WebAssembly fingerprints its framework assets
/// (<c>dotnet.7c5a14vp1y.js</c>), and the fingerprint changes with the build. The names the
/// running host puts in its index page are compared against the names in this working copy's
/// build output; a host started before the last rebuild serves the old ones.</para>
///
/// <para><b>Ordered first</b> so a developer reads this failure before the wall of editor
/// failures underneath it. It is deliberately not a hard prerequisite — a missing build output
/// or an unreachable host makes it Inconclusive rather than red, because "I have not built the
/// WASM host in this checkout" is not a product defect and must not read as one.</para>
/// </remarks>
[TestFixture]
[Order(-1)]
[Category("Editor")]
public class EditorHostFreshnessTests : BenTestBase
{
    private static string WasmUrl => Environment.GetEnvironmentVariable("BEN_WASM_URL") ?? "http://localhost:5180";

    /// <summary>
    /// The WebAssembly entry script, which is ALWAYS fingerprinted — unlike
    /// <c>dotnet.native.js</c> and <c>dotnet.runtime.js</c>, which appear in the page under
    /// un-fingerprinted aliases too and made the first version of this check cry wolf.
    /// </summary>
    private static readonly Regex EntryScript = new(@"blazor\.webassembly\.[a-z0-9]{6,}\.js",
        RegexOptions.Compiled);

    [Test]
    public async Task The_editor_host_is_serving_the_build_that_is_on_disk()
    {
        // The built copy: whatever this checkout last produced.
        var repoRoot = FindRepoRoot();
        if (repoRoot is null) Assert.Inconclusive("could not locate the repository root");

        var frameworkDir = Path.Combine(repoRoot!, "Ben.Wasm.Video", "bin", "Debug", "net10.0", "wwwroot", "_framework");
        if (!Directory.Exists(frameworkDir))
            Assert.Inconclusive("Ben.Wasm.Video has not been built in this checkout — nothing to compare against.");

        var onDisk = Directory.EnumerateFiles(frameworkDir, "blazor.webassembly.*.js")
            .Select(Path.GetFileName)
            .Where(n => n is not null && EntryScript.IsMatch(n))
            .ToList();
        if (onDisk.Count == 0)
            Assert.Inconclusive("no fingerprinted entry script in the build output — nothing to compare.");

        // What the running host is actually serving.
        string html;
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            html = await http.GetStringAsync(WasmUrl);
        }
        catch (Exception ex)
        {
            Assert.Inconclusive($"the editor host at {WasmUrl} is not reachable ({ex.GetType().Name}) — "
                              + "start it before the editor tests, or they will all fail for this reason.");
            return;
        }

        var served = EntryScript.Matches(html).Select(m => m.Value).ToHashSet(StringComparer.Ordinal);
        if (served.Count == 0)
            Assert.Inconclusive($"{WasmUrl} served a page with no fingerprinted entry script.");

        // The host is fresh when it serves an entry script this checkout actually built.
        var matches = served.Any(onDisk.Contains!);

        Assert.That(matches, Is.True,
            $"""
             The editor host at {WasmUrl} is serving an OLDER build than this checkout.

             Serving : {string.Join(", ", served)}
             On disk : {string.Join(", ", onDisk)}

             Every video-editor test will fail against it, and none of those failures mean the
             product is broken. Restart the host:

               kill $(lsof -ti :5180 -sTCP:LISTEN)
               ASPNETCORE_ENVIRONMENT=Development dotnet run --project Ben.Wasm.Video --no-launch-profile --urls {WasmUrl}
             """);
    }

    private static string? FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx")))
            dir = dir.Parent;
        return dir?.FullName;
    }
}
