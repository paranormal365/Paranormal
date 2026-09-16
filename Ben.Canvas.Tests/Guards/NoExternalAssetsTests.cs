using System.Text.RegularExpressions;
using Ben.Canvas.Tests.Support;

namespace Ben.Canvas.Tests.Guards;

/// <summary>
/// The canvas loads nothing from a third party, except Apple's MapKit script, and only from the map module.
/// </summary>
/// <remarks>
/// The site's standing rule (NoExternalAssetsInShellTests): a third party on the critical path hangs the
/// page on a restricted network, leaks every page view, and has already caused intermittent test
/// timeouts. MapKit is the one exception, because Apple does not permit self-hosting it.
/// </remarks>
public sealed class NoExternalAssetsTests
{
    private static readonly Dictionary<string, string> AllowedHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        ["cdn.apple-mapkit.com"] = "mapInterop.js",
    };

    [Fact]
    public void No_shell_stylesheet_or_script_names_a_third_party()
    {
        var files = RepoFiles.Files(RepoFiles.HostWwwroot(), "*.html", "*.css", "*.js")
            .Concat(RepoFiles.Files(RepoFiles.EditorWwwroot(), "*.css", "*.js"));

        var offenders = new List<string>();

        foreach (var file in files)
        {
            var text = RepoFiles.ReadWithoutComments(file);
            foreach (Match m in Regex.Matches(text, @"(src=|href=|@import\s+|url\(|import\()\s*[""']?(https?://([^/""')\s]+)[^""')\s]*)", RegexOptions.IgnoreCase))
            {
                var host = m.Groups[3].Value;
                if (AllowedHosts.TryGetValue(host, out var onlyIn) && Path.GetFileName(file) == onlyIn) continue;
                offenders.Add($"{RepoFiles.Relative(file)}: {m.Groups[2].Value}");
            }
        }

        Assert.True(offenders.Count == 0,
            "These load from a third party. Vendor the file under wwwroot/plugins with a LICENSE and "
            + "VENDORED.md instead:\n  " + string.Join("\n  ", offenders));
    }
}
