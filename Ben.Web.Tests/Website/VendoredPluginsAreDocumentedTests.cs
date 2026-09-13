using Xunit;

namespace Ben.Web.Tests.Website;

/// <summary>
/// Every third-party library committed to the site carries its licence and says where it came from.
/// </summary>
/// <remarks>
/// <para><b>Why this exists.</b> <c>NoExternalAssetsInShellTests</c> tells anybody reaching for a CDN
/// to vendor the file instead, "with its licence and a VENDORED.md saying where it came from and
/// why" — and until now nothing checked that they did. A library with no licence beside it is a
/// licence obligation nobody can show they met; one with no record of its version is a file nobody
/// can safely update, because nobody knows what it was.</para>
///
/// <para><b>What it checks</b> is only that the two files are there and that the record names a
/// version and a source. Whether the words are good is a review's job; whether they exist at all is
/// the part that silently stops being true.</para>
///
/// <para>Written for item 235 phase 7, which vendored jsQR for the door — the first plugin added
/// since the rule was written down, and so the first one that could have skipped it.</para>
/// </remarks>
public sealed class VendoredPluginsAreDocumentedTests
{
    private static DirectoryInfo Plugins()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return new DirectoryInfo(Path.Combine(dir!.FullName, "Ben.Web.Website", "wwwroot", "plugins"));
    }

    /// <summary>
    /// Folders that came with the site's theme rather than being vendored by us.
    /// </summary>
    /// <remarks>
    /// Kept short and each with a reason, because every entry is a hole in the rule. Bootstrap and
    /// Waves arrived inside the purchased SmartAdmin template, under the template's own licence,
    /// before this rule existed.
    /// </remarks>
    private static readonly Dictionary<string, string> CameWithTheTheme = new(StringComparer.OrdinalIgnoreCase)
    {
        ["bootstrap"] = "Shipped inside the SmartAdmin template, under its licence.",
        ["waves"] = "Shipped inside the SmartAdmin template, under its licence.",
    };

    [Fact]
    public void Every_vendored_plugin_has_a_licence_and_a_record_of_where_it_came_from()
    {
        var plugins = Plugins();
        Assert.True(plugins.Exists, $"no plugins folder at {plugins.FullName}");

        var missing = new List<string>();

        foreach (var folder in plugins.EnumerateDirectories())
        {
            if (CameWithTheTheme.ContainsKey(folder.Name)) continue;

            // LICENSE, LICENSE.txt, LICENSE.md — whatever the package itself called it. The first
            // version of this demanded the bare name and accused ApexCharts, whose licence has been
            // sitting beside it as LICENSE.txt since it was vendored.
            if (!folder.EnumerateFiles("LICENSE*").Any())
                missing.Add($"{folder.Name}: no LICENSE");

            var record = Path.Combine(folder.FullName, "VENDORED.md");
            if (!File.Exists(record))
            {
                missing.Add($"{folder.Name}: no VENDORED.md");
                continue;
            }

            // The substance and not a format: a version number and an address. ApexCharts writes
            // them on one line and jsQR under labels, and both answer the questions this is for.
            var text = File.ReadAllText(record);
            if (!System.Text.RegularExpressions.Regex.IsMatch(text, @"\b\d+\.\d+"))
                missing.Add($"{folder.Name}: VENDORED.md does not say which version");
            if (!text.Contains("http", StringComparison.OrdinalIgnoreCase))
                missing.Add($"{folder.Name}: VENDORED.md does not say where it came from");
        }

        Assert.True(missing.Count == 0,
            "a vendored library needs its licence and a record of what it is:\n  "
          + string.Join("\n  ", missing));
    }
}
