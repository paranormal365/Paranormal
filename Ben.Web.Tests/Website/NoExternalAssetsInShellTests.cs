using System.Text.RegularExpressions;
using Xunit;

namespace Ben.Web.Tests.Website;

/// <summary>
/// The page shell loads nothing from a third party.
/// </summary>
/// <remarks>
/// <para><c>App.razor</c> is the shell every single page renders through — the sign-in page, the
/// public microsite, every case screen. A <c>&lt;script&gt;</c> or <c>&lt;link&gt;</c> to an
/// external host there is paid for by every visitor on every page view, and it sits on the
/// critical path: <c>load</c> does not fire until it resolves.</para>
///
/// <para>This is not hypothetical. Fabric.js was loaded here from jsdelivr for a library used by
/// exactly one component — the image editor — and nothing else on the site touched it. It cost a
/// DNS lookup and TLS handshake to a third party before any page finished loading, told a CDN
/// about every page view, and would have hung the whole site on a restricted or air-gapped
/// network. It surfaced as intermittent 30-second Playwright timeouts on the first navigation
/// (item 114).</para>
///
/// <para>Vendor it instead, as <c>plugins/apexcharts/</c> and <c>plugins/fabric/</c> both are:
/// commit the file with its licence and a VENDORED.md saying where it came from and why.</para>
///
/// <para><b>Google Fonts is the deliberate exception</b> — it is a stylesheet the template ships
/// with, and self-hosting fonts is a separate decision with its own trade-offs. If that changes,
/// remove the allowance rather than widening it.</para>
/// </remarks>
public sealed class NoExternalAssetsInShellTests
{
    /// <summary>
    /// Nothing. The shell is expected to load everything from this origin.
    /// </summary>
    /// <remarks>
    /// Google Fonts used to be allowed here, on the reasoning that self-hosting fonts is a separate
    /// decision. That allowance turned out to be sheltering the actual problem: after Fabric was
    /// vendored, the two font stylesheets were the <b>only</b> external requests left, the browser
    /// reported both as render-blocking, and they matched the remaining intermittent
    /// "waiting until load" timeouts exactly. The fonts are self-hosted now
    /// (<c>wwwroot/fonts/</c>), so the list is empty and should stay that way.
    /// </remarks>
    private static readonly string[] AllowedHosts = [];

    private static DirectoryInfo RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx")))
            dir = dir.Parent;
        return dir ?? throw new InvalidOperationException("repo root not found");
    }

    private static string ShellPath() =>
        Path.Combine(RepoRoot().FullName, "Ben.Web.Website", "Components", "App.razor");

    /// <summary>
    /// The site's own stylesheets pull nothing from another host either.
    /// </summary>
    /// <remarks>
    /// App.razor was only half the story. Both font fetches were <c>@import</c> statements buried
    /// inside <c>css/smartapp.min.css</c> and <c>app.css</c> — invisible to a scan of the shell,
    /// and worse than a tag in the head because the browser cannot even discover them until it has
    /// fetched and parsed the stylesheet that contains them.
    /// </remarks>
    [Fact]
    public void The_sites_stylesheets_import_nothing_from_another_host()
    {
        var root = Path.Combine(RepoRoot().FullName, "Ben.Web.Website", "wwwroot");
        var offenders = new List<string>();

        foreach (var css in Directory.EnumerateFiles(root, "*.css", SearchOption.AllDirectories))
        {
            // Comments are stripped first. Vendored CSS carries licence URLs, and fonts.css
            // documents the very @import it replaced — a doc comment is not a fetch, and a
            // guard that cannot tell the difference flags the fix as the bug.
            var text = Regex.Replace(File.ReadAllText(css), @"/\*.*?\*/", "", RegexOptions.Singleline);

            foreach (Match m in Regex.Matches(text, @"@import\s+url\(['""]?(https?://[^)'""]+)"))
                offenders.Add($"{Path.GetFileName(css)} → {m.Groups[1].Value}");
        }

        Assert.True(offenders.Count == 0,
            "A stylesheet @imports from a third party. The browser cannot discover this until it "
            + "has already fetched and parsed the containing stylesheet, so it is a serial chain "
            + "on the critical path of every page. Self-host it under wwwroot/:\n  "
            + string.Join("\n  ", offenders));
    }

    [Fact]
    public void The_shell_loads_no_scripts_or_styles_from_another_host()
    {
        var source = File.ReadAllText(ShellPath());
        var offenders = new List<string>();

        foreach (Match m in Regex.Matches(source, @"(?:src|href)=""(https?://[^""]+)"""))
        {
            var url = m.Groups[1].Value;
            if (AllowedHosts.Any(h => url.Contains(h, StringComparison.OrdinalIgnoreCase))) continue;

            var line = source.Take(m.Index).Count(c => c == '\n') + 1;
            offenders.Add($"App.razor:{line} → {url}");
        }

        Assert.True(offenders.Count == 0,
            "The page shell pulls an asset from a third party. Every visitor pays for this on "
            + "every page, and the site waits on that host to finish loading. Vendor it under "
            + "wwwroot/plugins/ with its licence and a VENDORED.md, and load it from the one "
            + "component that needs it:\n  " + string.Join("\n  ", offenders));
    }

    /// <summary>
    /// Guards the guard: the regex must actually be looking at the real shell.
    /// </summary>
    [Fact]
    public void The_shell_file_is_where_this_test_thinks_it_is()
    {
        Assert.True(File.Exists(ShellPath()), $"App.razor not found at {ShellPath()}.");

        // If the shell stopped loading any local assets, it has been restructured and this test is
        // probably reading the wrong file rather than passing honestly.
        var source = File.ReadAllText(ShellPath());
        Assert.Contains("<script", source, StringComparison.OrdinalIgnoreCase);
    }
}
