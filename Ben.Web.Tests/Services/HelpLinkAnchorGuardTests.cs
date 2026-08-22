using System.Text.RegularExpressions;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// Every in-app help link points at a document and heading that actually exist.
/// </summary>
/// <remarks>
/// <para>A <c>HelpLink</c> with a stale anchor is silent: it renders, it clicks, and it drops the
/// reader at the top of a page that no longer has the section they were promised. Renaming a
/// heading is the ordinary way to break one, and nothing else in the build notices.</para>
///
/// <para>Written after an audit found ten new surfaces shipped with no help link at all
/// (2026-08-22). This guards the other half — that the links which do exist keep working — and it
/// is deliberately a scan rather than a ratchet: there is no reason to ever have a broken one.</para>
/// </remarks>
public sealed class HelpLinkAnchorGuardTests
{
    private static DirectoryInfo RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!;
    }

    /// <summary>The same slug rule <c>HelpContentService</c> uses to build heading anchors.</summary>
    private static string Slugify(string text)
    {
        var chars = text.ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : (c is ' ' or '-' or '_' ? '-' : '\0'))
            .Where(c => c != '\0')
            .ToArray();

        var slug = new string(chars);
        while (slug.Contains("--")) slug = slug.Replace("--", "-");
        return slug.Trim('-');
    }

    [Fact]
    public void Every_HelpLink_points_at_a_document_and_heading_that_exist()
    {
        var root = RepoRoot().FullName;

        var headings = Directory
            .EnumerateFiles(Path.Combine(root, "Ben.Web.Services", "Help", "Content"), "*.md")
            .ToDictionary(
                Path.GetFileNameWithoutExtension,
                f => Regex.Matches(File.ReadAllText(f), @"^#{2,3}\s+(.+)$", RegexOptions.Multiline)
                          .Select(m => Slugify(m.Groups[1].Value.Trim()))
                          .ToHashSet());

        var broken = new List<string>();
        var checkedCount = 0;

        foreach (var razor in new[] { "Ben.Web.Website.Library", "Ben.Web.Website" }
                     .SelectMany(p => Directory.EnumerateFiles(Path.Combine(root, p), "*.razor", SearchOption.AllDirectories))
                     .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                              && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")))
        {
            foreach (Match m in Regex.Matches(File.ReadAllText(razor),
                         """<HelpLink\s+Slug="([^"]+)"(?:\s+Anchor="([^"]+)")?"""))
            {
                checkedCount++;
                var slug   = m.Groups[1].Value;
                var anchor = m.Groups[2].Success ? m.Groups[2].Value : null;
                var name   = Path.GetFileName(razor);

                if (!headings.TryGetValue(slug, out var inDoc))
                    broken.Add($"{name}: no help document named \"{slug}\"");
                else if (anchor is not null && !inDoc.Contains(anchor))
                    broken.Add($"{name}: \"{slug}\" has no heading anchored \"{anchor}\"");
            }
        }

        Assert.True(checkedCount > 20, $"Only {checkedCount} HelpLinks found — the scan is not reaching the pages.");
        Assert.True(broken.Count == 0,
            $"""
             {broken.Count} help link(s) point nowhere:

               {string.Join("\n  ", broken)}

             A stale anchor is silent — it renders, it clicks, and it drops the reader at the top
             of a page missing the section they were promised. Rename the heading back, or point
             the link at the new one.
             """);
    }
}
