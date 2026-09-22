using Xunit;
using System.Text.RegularExpressions;

namespace Ben.Web.Tests.Website;

/// <summary>
/// The hand-built markup in <c>ClipBrowser.razor</c> still gets this component's scoped styles.
/// </summary>
/// <remarks>
/// <para><b>Why this is a guard and not a comment.</b> Most of that file's panes are written with
/// raw <c>RenderTreeBuilder</c> calls, and the Razor compiler stamps its <c>b-xxxxxxxx</c> scope
/// attribute only onto markup it compiles. Anything built by hand comes out carrying the right
/// class names and none of the styles — which is not a render error, not a build warning and not
/// visible to anybody reading the stylesheet. It simply looks slightly wrong on screen.</para>
///
/// <para>It has now been shipped twice. Somebody found it in the clip grids, fixed those, wrote the
/// <c>CssScope</c> constant and a comment explaining the trap — and the four empty states, the
/// loading pane, the sign-in prompt and the error strip beside them kept the fault. The empty state
/// was caught on 2026-09-21 by an audit measuring text against its container's border, which is a
/// long way round for "the padding never applied".</para>
///
/// <para>Two things are checked. That the constant is still the scope the compiler assigns — it is
/// a literal hash, so moving or renaming the file silently unhooks every hand-built element at
/// once. And that no hand-built element names a class the scoped stylesheet styles without
/// carrying the attribute, so the next pane written this way fails here rather than on screen.</para>
/// </remarks>
public sealed class ClipBrowserCssScopeTests
{
    private static DirectoryInfo RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx")))
            dir = dir.Parent;
        return dir ?? throw new InvalidOperationException("repo root not found");
    }

    private static string Source() => File.ReadAllText(
        Path.Combine(RepoRoot().FullName, "Ben.Video.Editor", "Components", "ClipBrowser.razor"));

    /// <summary>The scope the component declares by hand is the one the compiler stamps.</summary>
    [Fact]
    public void TheDeclaredScopeIsTheCompiledScope()
    {
        var declared = Regex.Match(Source(), @"CssScope\s*=\s*""(?<scope>b-[a-z0-9]+)""");
        Assert.True(declared.Success, "ClipBrowser.razor no longer declares a CssScope constant.");

        // The compiler rewrites the component's stylesheet into obj/ with the scope appended to
        // every selector. Whichever configuration was built last will do.
        var generated = Directory
            .EnumerateFiles(Path.Combine(RepoRoot().FullName, "Ben.Video.Editor", "obj"),
                            "ClipBrowser.razor.rz.scp.css", SearchOption.AllDirectories)
            .ToList();

        Assert.True(generated.Count > 0,
            "Ben.Video.Editor has not been built, so the compiled scope could not be read.");

        var scope = declared.Groups["scope"].Value;
        foreach (var file in generated)
            Assert.True(File.ReadAllText(file).Contains($"[{scope}]"),
                $"ClipBrowser.razor declares CssScope \"{scope}\", but the compiler stamped a "
                + $"different one in {file}. Every hand-built element in that file has silently "
                + "lost its styles — read the constant's new value out of that file.");
    }

    /// <summary>
    /// Hand-built elements naming a styled class carry the attribute.
    /// </summary>
    /// <remarks>
    /// Only classes the component's own stylesheet actually styles are required to: the file also
    /// builds elements carrying Telerik and Bootstrap classes, which are not scoped to anything.
    /// </remarks>
    [Fact]
    public void HandBuiltElementsCarryTheScope()
    {
        var css = File.ReadAllText(Path.Combine(
            RepoRoot().FullName, "Ben.Video.Editor", "Components", "ClipBrowser.razor.css"));

        var styled = Regex.Matches(css, @"\.(?<name>bv-[a-z0-9_-]+)")
            .Select(m => m.Groups["name"].Value)
            .ToHashSet();

        var missing = new List<string>();
        foreach (var line in Source().Split('\n'))
        {
            if (!line.Contains("OpenElement")) continue;

            var named = Regex.Match(line, @"AddAttribute\([^,]+,\s*""class"",\s*""(?<class>[^""]+)""");
            if (!named.Success) continue;
            if (!named.Groups["class"].Value.Split(' ').Any(styled.Contains)) continue;
            if (line.Contains("CssScope")) continue;

            missing.Add(line.Trim());
        }

        Assert.True(missing.Count == 0,
            "These hand-built elements name a class ClipBrowser.razor.css styles but carry no "
            + "scope attribute, so none of those rules will apply:\n  "
            + string.Join("\n  ", missing));
    }
}
