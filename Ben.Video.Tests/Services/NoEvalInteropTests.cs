namespace Ben.Video.Tests.Services;

/// <summary>
/// Audit #4 — guards the property that the eval removal bought, rather than trusting a one-off
/// cleanup to stay clean.
///
/// <para>Two reasons this matters beyond tidiness:</para>
/// <list type="bullet">
///   <item><b>CSP.</b> Every <c>JS.InvokeAsync("eval", …)</c> requires <c>unsafe-eval</c> in the
///   Content-Security-Policy of whatever host embeds this editor. One reintroduced call silently
///   re-imposes that on <c>Ben.Web.WebApp</c>.</item>
///   <item><b>Crash class.</b> The eval form built DOM lookups as source strings, so a missing
///   element produced a raw <c>TypeError</c> that propagated as an unhandled Blazor render
///   exception — audit #7 was exactly that: the toolbar's Open button killed the whole circuit
///   when the Media panel was closed. The typed helpers null-guard every lookup by construction.</item>
/// </list>
/// </summary>
public sealed class NoEvalInteropTests
{
    private static string EditorRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "Ben.Video.Editor")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "Ben.Video.Editor");
    }

    private static IEnumerable<string> SourceFiles() =>
        Directory.EnumerateFiles(EditorRoot(), "*.*", SearchOption.AllDirectories)
                 .Where(f => f.EndsWith(".cs", StringComparison.Ordinal) || f.EndsWith(".razor", StringComparison.Ordinal))
                 .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                          && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    [Fact]
    public void NoCSharpCallSiteInvokesJsEval()
    {
        var offenders = SourceFiles()
            .Where(f => File.ReadAllText(f).Contains("\"eval\"", StringComparison.Ordinal))
            .Select(f => Path.GetFileName(f))
            .ToList();

        Assert.True(offenders.Count == 0,
            "JS eval() reintroduced in: " + string.Join(", ", offenders) +
            ". Use the typed helpers in domInterop.js / storageInterop.js instead — see this test's " +
            "summary for the CSP and crash-class reasons.");
    }

    [Fact]
    public void InteropModulesExposeTheHelpersTheCallSitesUse()
    {
        // Guards the other half: the call sites are only correct if the module actually exports
        // what they invoke. A rename on either side would otherwise fail silently at runtime
        // (a missing export is `undefined`, not an error, until it's called).
        var js = Path.Combine(EditorRoot(), "wwwroot", "js");
        var dom = File.ReadAllText(Path.Combine(js, "domInterop.js"));
        var storage = File.ReadAllText(Path.Combine(js, "storageInterop.js"));

        foreach (var fn in new[]
        {
            "focusAndSelect", "click", "fileCount", "fileName", "fileSize",
            "fileAt", "fileObjectUrl", "clearFileInput", "imageDimensions", "downloadText",
        })
        {
            Assert.True(dom.Contains($"export function {fn}", StringComparison.Ordinal)
                     || dom.Contains($"export async function {fn}", StringComparison.Ordinal),
                $"domInterop.js is missing an export used by C#: {fn}");
        }

        foreach (var fn in new[] { "getItem", "setItem", "removeItem" })
        {
            Assert.True(storage.Contains($"export function {fn}", StringComparison.Ordinal),
                $"storageInterop.js is missing an export used by C#: {fn}");
        }
    }

    [Fact]
    public void DownloadHelperKeepsItsDeferredRevoke()
    {
        // Phase 144 found that revoking a download's blob URL in the same tick as a.click() races
        // the browser's own fetch of it and can silently drop the file. Moving that logic into a
        // shared helper made it easy to "tidy" into an immediate revoke, so the delay is pinned.
        var dom = File.ReadAllText(Path.Combine(EditorRoot(), "wwwroot", "js", "domInterop.js"));
        Assert.Contains("setTimeout(() => URL.revokeObjectURL(url), 30000)", dom, StringComparison.Ordinal);
    }
}
