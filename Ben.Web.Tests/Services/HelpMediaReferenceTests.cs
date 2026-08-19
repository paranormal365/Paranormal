using Ben.Data.Common.Enums;
using Ben.Web.Services.Help;
using System.Text.RegularExpressions;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// The screenshots the help documents embed must exist, must be reachable by the readers of the
/// document that embeds them, and must not outlive the document that referenced them.
/// </summary>
/// <remarks>
/// <para>Screenshots go stale in a way prose does not: a page is restyled, the shot is
/// re-captured under a new name, and the old reference renders as a broken image — or worse, the
/// old file stays behind and nothing says it is unused. None of that fails a build on its own.</para>
///
/// <para>The audience check is the one that matters most. A document for group or site
/// administrators embeds its pictures with the <c>help-media:</c> scheme, which inlines them from
/// the assembly; a document for a wider audience uses a plain path under the site's wwwroot.
/// Getting that backwards puts a screenshot of an administration screen at a URL anyone can
/// guess, which is the exact leak the help text is embedded to avoid.</para>
/// </remarks>
public sealed class HelpMediaReferenceTests
{
    /// <summary>A markdown image whose target is a wwwroot path: <c>![alt](/help/media/slug/x.png)</c>.</summary>
    private static readonly Regex PublicImage = new(
        @"!\[[^\]]*\]\(/help/media/(?<path>[^)]+)\)", RegexOptions.Compiled);

    /// <summary>A markdown image served from the assembly: <c>![alt](help-media:slug/x.png)</c>.</summary>
    private static readonly Regex EmbeddedImage = new(
        @"!\[[^\]]*\]\(help-media:(?<path>[^)]+)\)", RegexOptions.Compiled);

    /// <summary>Audiences whose screenshots must not be reachable by URL.</summary>
    private static bool IsAdministratorDocument(HelpAudience audience) =>
        audience is HelpAudience.OrganizationAdministrator or HelpAudience.AppAdministrator;

    private static DirectoryInfo RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!;
    }

    private static string PublicMediaRoot() =>
        Path.Combine(RepoRoot().FullName, "Ben.Web.Website", "wwwroot", "help", "media");

    /// <summary>
    /// The gated screenshots as files on disk. The orphan check reads these rather than the
    /// assembly's manifest, because MSBuild rewrites hyphens in a resource name's folder segments
    /// and a message naming <c>site_administration</c> sends the reader looking for a folder that
    /// does not exist.
    /// </summary>
    private static string EmbeddedMediaRoot() =>
        Path.Combine(RepoRoot().FullName, "Ben.Web.Services", "Help", "Media");

    private static IReadOnlyList<HelpDocument> Documents() => HelpContentService.LoadAll();

    [Fact]
    public void Every_referenced_screenshot_exists()
    {
        var missing = new List<string>();
        var referenced = 0;

        foreach (var doc in Documents())
        {
            foreach (Match m in PublicImage.Matches(doc.Markdown))
            {
                referenced++;
                var path = m.Groups["path"].Value;
                if (!File.Exists(Path.Combine(PublicMediaRoot(), path.Replace('/', Path.DirectorySeparatorChar))))
                    missing.Add($"{doc.Slug} → wwwroot/help/media/{path}");
            }

            foreach (Match m in EmbeddedImage.Matches(doc.Markdown))
            {
                referenced++;
                var path = m.Groups["path"].Value;
                if (!HelpContentService.EmbeddedMediaExists(path))
                    missing.Add($"{doc.Slug} → embedded {path}");
            }
        }

        Assert.True(referenced > 0, "No help document references a screenshot — has the media moved?");
        Assert.True(missing.Count == 0,
            "Help documents reference screenshots that do not exist. Re-capture them with "
            + "BEN_CAPTURE=1 (see HelpMediaCapture), or fix the reference:\n  "
            + string.Join("\n  ", missing));
    }

    [Fact]
    public void Administrator_screenshots_are_embedded_and_others_are_not()
    {
        var offenders = new List<string>();

        foreach (var doc in Documents())
        {
            var admin = IsAdministratorDocument(doc.Audience);

            foreach (Match m in PublicImage.Matches(doc.Markdown))
            {
                if (admin)
                    offenders.Add(
                        $"{doc.Slug} ({doc.Audience}) serves {m.Groups["path"].Value} from wwwroot — "
                        + "anyone can fetch it by URL. Capture it as gated and reference it with help-media:.");
            }

            foreach (Match m in EmbeddedImage.Matches(doc.Markdown))
            {
                if (!admin)
                    offenders.Add(
                        $"{doc.Slug} ({doc.Audience}) inlines {m.Groups["path"].Value} as a data URI. "
                        + "Only administrator documents need that; a public document should use "
                        + "/help/media/… so the browser can cache it.");
            }
        }

        Assert.True(offenders.Count == 0, string.Join("\n  ", offenders));
    }

    /// <summary>The extensions a screenshot or recording can have — everything else in the folder
    /// is not media, and a stray <c>.DS_Store</c> is not an orphaned picture.</summary>
    private static bool IsMedia(string file) =>
        Path.GetExtension(file) is ".png" or ".gif";

    [Fact]
    public void No_screenshot_is_left_behind_unreferenced()
    {
        var referenced = new HashSet<string>(StringComparer.Ordinal);
        foreach (var doc in Documents())
        {
            foreach (Match m in PublicImage.Matches(doc.Markdown))
                referenced.Add("public:" + m.Groups["path"].Value);
            foreach (Match m in EmbeddedImage.Matches(doc.Markdown))
                referenced.Add("embedded:" + m.Groups["path"].Value);
        }

        var orphans = new List<string>();

        var publicRoot = PublicMediaRoot();
        if (Directory.Exists(publicRoot))
        {
            foreach (var file in Directory.EnumerateFiles(publicRoot, "*.*", SearchOption.AllDirectories)
                                          .Where(IsMedia))
            {
                var rel = Path.GetRelativePath(publicRoot, file).Replace(Path.DirectorySeparatorChar, '/');
                if (!referenced.Contains("public:" + rel))
                    orphans.Add($"wwwroot/help/media/{rel}");
            }
        }

        var embeddedRoot = EmbeddedMediaRoot();
        if (Directory.Exists(embeddedRoot))
        {
            foreach (var file in Directory.EnumerateFiles(embeddedRoot, "*.*", SearchOption.AllDirectories)
                                          .Where(IsMedia))
            {
                var rel = Path.GetRelativePath(embeddedRoot, file).Replace(Path.DirectorySeparatorChar, '/');
                if (!referenced.Contains("embedded:" + rel))
                    orphans.Add($"Ben.Web.Services/Help/Media/{rel}");
            }
        }

        Assert.True(orphans.Count == 0,
            "Screenshots no document references. Delete them, or reference them:\n  "
            + string.Join("\n  ", orphans));
    }

    [Fact]
    public void An_embedded_reference_renders_as_a_data_uri()
    {
        var doc = Documents().FirstOrDefault(d => EmbeddedImage.IsMatch(d.Markdown));
        Assert.NotNull(doc);

        var html = new HelpContentService().ToHtml(doc!);

        Assert.Contains("src=\"data:image/", html);
        // The scheme itself must never reach the browser: an <img src="help-media:…"> is a broken
        // image, and it is what a regression in the inlining would look like.
        Assert.DoesNotContain("help-media:", html);
    }
}
