using Xunit;

namespace Ben.Web.Tests.Website;

/// <summary>
/// The clip browser's styles reach the page, now that they are not scoped.
/// </summary>
/// <remarks>
/// <para><b>What this used to guard, and why it could not work.</b> Most of
/// <c>ClipBrowser.razor</c> is written with raw <c>RenderTreeBuilder</c> calls, and the Razor
/// compiler stamps its <c>b-xxxxxxxxxx</c> scope attribute only onto markup it compiles. Hand-built
/// elements came out with the right class names and none of the styles — not a render error, not a
/// build warning, just a screen that looked slightly wrong. The fix was a <c>CssScope</c> constant
/// holding that hash, copied by hand onto sixty elements, and this test checked the constant still
/// matched what the compiler produced.</para>
///
/// <para><b>The constant could not be right.</b> The hash differs between machines — this
/// repository's Windows server stamps <c>b-ix6pdo4zfh</c> where the author's Mac stamps
/// <c>b-xbjan6uni8</c>, because it is derived from a path and the two platforms spell paths
/// differently. On 2026-09-22 two sessions corrected it to opposite values within a day, each
/// measuring honestly, each unhooking every hand-built element on the other's machine. A guard that
/// can only pass on one machine at a time is worse than no guard at all.</para>
///
/// <para><b>So the styles left CSS isolation.</b> They live in
/// <c>Ben.Video.Editor/wwwroot/css/clip-browser.css</c> and are linked by both hosts like any
/// ordinary stylesheet. No attribute, no hash, nothing to disagree about. What is guarded now is the
/// new failure mode: that stylesheet existing but not being linked, which would take the styles away
/// from the compiled markup too — a bigger and more visible break than the one it replaced, and
/// still not something the compiler would mention.</para>
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

    private static string Read(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { RepoRoot().FullName }.Concat(parts).ToArray()));

    private const string StylesheetPath = "css/clip-browser.css";

    [Fact]
    public void The_stylesheet_is_where_both_hosts_expect_it()
    {
        var path = Path.Combine(
            RepoRoot().FullName, "Ben.Video.Editor", "wwwroot", "css", "clip-browser.css");

        Assert.True(File.Exists(path),
            "clip-browser.css is gone. The clip browser's styles are not scoped, so this file is "
          + "the only thing that styles it — and nothing else will report its absence.");
    }

    /// <summary>
    /// Both hosts render the editor, so both have to link it. Linking it in one is the failure that
    /// looks like "it works on my machine" and is really "it works on that page".
    /// </summary>
    [Theory]
    [InlineData("Ben.Wasm.Video", "wwwroot", "index.html")]
    [InlineData("Ben.Web.Website", "Components", "App.razor")]
    public void Every_host_that_shows_the_editor_links_it(string a, string b, string c)
    {
        Assert.Contains(StylesheetPath, Read(a, b, c));
    }

    /// <summary>
    /// A scoped stylesheet beside the component would come back silently: the compiler would pick
    /// it up, stamp its rules with an attribute the hand-built markup does not carry, and those
    /// rules would then reach some of this component and not the rest.
    /// </summary>
    [Fact]
    public void The_scoped_stylesheet_has_not_come_back()
    {
        var scoped = Path.Combine(
            RepoRoot().FullName, "Ben.Video.Editor", "Components", "ClipBrowser.razor.css");

        Assert.False(File.Exists(scoped),
            "ClipBrowser.razor.css is back. CSS isolation stamps a scope attribute onto compiled "
          + "markup only, so its rules would reach part of this component and not the panes built "
          + "by hand. Put the rule in wwwroot/css/clip-browser.css instead.");
    }

    /// <summary>
    /// Nothing in the component should carry a hand-copied scope attribute any more; one left
    /// behind would name a hash nothing stamps.
    /// </summary>
    [Fact]
    public void No_hand_copied_scope_attribute_is_left_in_the_component()
    {
        Assert.DoesNotContain("CssScope", Read("Ben.Video.Editor", "Components", "ClipBrowser.razor"));
    }
}
