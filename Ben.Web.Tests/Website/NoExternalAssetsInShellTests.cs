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
    private static readonly string[] AllowedHosts =
    [
        "fonts.googleapis.com",
        "fonts.gstatic.com",
    ];

    private static DirectoryInfo RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx")))
            dir = dir.Parent;
        return dir ?? throw new InvalidOperationException("repo root not found");
    }

    private static string ShellPath() =>
        Path.Combine(RepoRoot().FullName, "Ben.Web.Website", "Components", "App.razor");

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
