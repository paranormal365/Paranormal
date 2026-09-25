using System.Text.RegularExpressions;
using Xunit;

namespace Ben.Web.Tests.Website;

/// <summary>
/// Library JavaScript that reaches for a host asset by absolute path must find it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Four days of silent breakage is why this exists.</b> <c>WaveSurferPlayer.razor.js</c> lives in
/// the <c>Ben.Web.Website.Library</c> RCL and imports <c>/js/wavesurfer/wavesurfer.esm.js</c> by
/// absolute path. When <c>Ben.Web.WebApp</c> was removed on 2026-08-19 that folder went with it, and
/// every audio preview on the site answered "Player init failed" until 2026-08-23. Nothing failed at
/// build. The error rendered inside the player, on click, and no test ever clicked one.
/// </para>
/// <para>
/// An RCL's <c>.razor.js</c> runs on whatever host serves it, so an absolute <c>/js/...</c> import is
/// a hidden dependency on that host's wwwroot — invisible to the compiler and to every green build.
/// The scan below was run by hand after the outage; this is that scan, kept.
/// </para>
/// <para>
/// It does not forbid the pattern. Moving those assets into <c>_content/</c> would be a bigger
/// change than it looks — see <c>wwwroot/js/wavesurfer/VENDORED.md</c> for why that one stayed put.
/// It only insists the path resolves.
/// </para>
/// </remarks>
public sealed class HostAssetPathTests
{
    /// <summary>Absolute references to a host folder: "/js/...", "/css/...", "/lib/...".</summary>
    private static readonly Regex HostAsset = new(
        @"['""](?<path>/(?:js|css|lib)/[^'""\s)]+)['""]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    [Fact]
    public void Every_host_asset_the_library_reaches_for_is_actually_there()
    {
        var repo = RepoRoot();
        var library = Path.Combine(repo, "Ben.Web.Website.Library");
        var hostRoot = Path.Combine(repo, "Ben.Web.Website", "wwwroot");

        var missing = new List<string>();

        foreach (var file in Directory
                     .EnumerateFiles(library, "*.js", SearchOption.AllDirectories)
                     .Concat(Directory.EnumerateFiles(library, "*.razor", SearchOption.AllDirectories))
                     .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                              && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")))
        {
            foreach (Match match in HostAsset.Matches(File.ReadAllText(file)))
            {
                var asked = match.Groups["path"].Value;

                // A query string or fragment is addressing, not a file name.
                var bare = asked.Split('?')[0].Split('#')[0];

                // "/js/foo/{id}.js" and friends are built at runtime; there is no file to find.
                if (bare.Contains('{') || bare.Contains("${")) continue;

                var onDisk = Path.Combine(hostRoot, bare.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(onDisk) && !Directory.Exists(onDisk))
                    missing.Add($"{Relative(repo, file)} asks for {asked}");
            }
        }

        Assert.True(missing.Count == 0,
            "Library code reaches for host assets that are not in Ben.Web.Website/wwwroot. Nothing "
          + "will fail at build; it fails in the browser, on the click that needs it:\n  "
          + string.Join("\n  ", missing.Distinct().Order()));
    }

    /// <summary>
    /// A vendored third-party library says what it is: which version, from where, under what licence.
    /// </summary>
    /// <remarks>
    /// Without it nobody can tell a stock build from a patched one, or answer "is this the version
    /// with the security fix?". That is not hypothetical here — wavesurfer's bundle does not match
    /// any published npm dist, and it took reading a deleted project's package.json out of git
    /// history to establish that it is stock 7.12.11 built with a different rollup config.
    /// </remarks>
    [Theory]
    [InlineData("wwwroot/js/wavesurfer")]
    public void Every_vendored_library_says_what_it_is(string folder)
    {
        var directory = Path.Combine(RepoRoot(), "Ben.Web.Website", folder.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(Directory.Exists(directory), $"{folder} is not there any more — update this test with it.");

        var note = Path.Combine(directory, "VENDORED.md");
        Assert.True(File.Exists(note), $"{folder} has no VENDORED.md saying what it is.");

        var text = File.ReadAllText(note);
        Assert.True(Regex.IsMatch(text, @"\*\*Version:\*\*\s*\d+\.\d+\.\d+"), $"{folder}: VENDORED.md names no version.");
        Assert.Contains("https://", text, StringComparison.Ordinal);
        Assert.True(Regex.IsMatch(text, @"\b[0-9a-f]{64}\b"), $"{folder}: VENDORED.md carries no SHA-256.");
        Assert.True(File.Exists(Path.Combine(directory, "LICENSE")), $"{folder} has no LICENSE beside it.");
    }

    /// <summary>The recorded hash is the file's hash — so a swapped bundle is noticed here.</summary>
    [Fact]
    public void The_wavesurfer_bundle_is_the_one_the_note_pins()
    {
        var directory = Path.Combine(RepoRoot(), "Ben.Web.Website", "wwwroot", "js", "wavesurfer");
        var recorded = Regex.Match(File.ReadAllText(Path.Combine(directory, "VENDORED.md")), @"\b[0-9a-f]{64}\b").Value;

        var actual = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                File.ReadAllBytes(Path.Combine(directory, "wavesurfer.esm.js")))).ToLowerInvariant();

        Assert.Equal(recorded, actual);
    }

    private static string Relative(string repo, string path) =>
        Path.GetRelativePath(repo, path).Replace(Path.DirectorySeparatorChar, '/');

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Ben.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Ben.slnx not found above the test binary.");
    }
}
