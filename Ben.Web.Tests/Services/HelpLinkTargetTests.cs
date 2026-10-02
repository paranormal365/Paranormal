using Ben.Data.Common.Enums;
using Ben.Web.Services.Help;
using System.Text.RegularExpressions;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// Every <c>&lt;HelpLink&gt;</c> placed in the app must point at a document that exists and an
/// anchor that document actually has.
/// </summary>
/// <remarks>
/// A broken help link fails silently — it opens a "topic isn't available" page, or lands at the
/// top of the right document instead of the right section, and nothing logs it. Renaming a
/// heading is the ordinary way this happens, so the check is by source scan rather than by
/// remembering to look.
/// </remarks>
public sealed class HelpLinkTargetTests
{
    private static readonly Regex LinkPattern = new(
        """<HelpLink\s+Slug="(?<slug>[^"]+)"(?:\s+Anchor="(?<anchor>[^"]*)")?""",
        RegexOptions.Compiled);

    /// <summary>
    /// A plain link into help — <c>href="/help/slug#anchor"</c> — which the pattern above never saw.
    /// The equipment catalog's "How the catalog works" was one, pointing into a guide visitors
    /// cannot read (walk, 2026-10-02).
    /// </summary>
    private static readonly Regex HrefPattern = new(
        @"href=""/help/(?<slug>[a-z0-9-]+)(?:#(?<anchor>[a-z0-9-]+))?""",
        RegexOptions.Compiled);

    /// <summary>Walks up from the test binaries to the repository root.</summary>
    private static DirectoryInfo RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!;
    }

    [Fact]
    public void Every_help_link_in_the_app_resolves()
    {
        var root = RepoRoot();
        var service = new HelpContentService();
        // Resolve against the widest audience — this checks the link's *target*, not who may
        // read it. Whether the reader is allowed in is the resolver's job, tested separately.
        var viewer = new HelpViewer(HelpAudience.AppAdministrator);

        var razorFiles = new[] { "Ben.Web.Website.Library", "Ben.Web.Website" }
            .Select(p => Path.Combine(root.FullName, p))
            .Where(Directory.Exists)
            .SelectMany(p => Directory.EnumerateFiles(p, "*.razor", SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .ToList();

        Assert.NotEmpty(razorFiles);

        var found = 0;
        foreach (var file in razorFiles)
        {
            var text = File.ReadAllText(file);
            foreach (Match match in LinkPattern.Matches(text).Concat(HrefPattern.Matches(text)))
            {
                found++;
                var slug = match.Groups["slug"].Value;
                var name = Path.GetFileName(file);

                var doc = service.Find(slug, viewer);
                Assert.True(doc is not null, $"{name} links to help document '{slug}', which does not exist.");

                if (!match.Groups["anchor"].Success) continue;

                var anchor = match.Groups["anchor"].Value;
                var anchors = HelpContentService.HeadingsOf(doc!).Select(h => h.Anchor).ToList();
                Assert.True(anchors.Contains(anchor),
                    $"{name} links to '{slug}#{anchor}', but that document's headings are: {string.Join(", ", anchors)}");
            }
        }

        // Guards against the regex quietly matching nothing after a syntax change — a passing
        // test that examined zero links would be worse than no test.
        Assert.True(found > 0, "No HelpLink usages were found — has the component been renamed?");
    }

    [Fact]
    public void The_public_equipment_pages_link_to_a_guide_a_visitor_can_read()
    {
        // The catalog, a model's page and its questions are public; their help links led to
        // "That help topic isn't available" for anybody signed out (walk, 2026-10-02).
        var root = RepoRoot();
        var visitor = new HelpViewer(HelpAudience.Everyone);
        var service = new HelpContentService();
        foreach (var page in new[] { "EquipmentCatalogBrowse.razor", "EquipmentModelPage.razor", "EquipmentFaqSection.razor" })
        {
            var text = File.ReadAllText(Path.Combine(root.FullName, "Ben.Web.Website.Library", "Equipment", page));
            foreach (Match match in LinkPattern.Matches(text).Concat(HrefPattern.Matches(text)))
            {
                var slug = match.Groups["slug"].Value;
                Assert.True(service.Find(slug, visitor) is not null,
                    $"{page} is public but links to '{slug}', which a visitor cannot read.");
            }
        }
    }
}
