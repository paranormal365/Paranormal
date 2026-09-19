using Ben.Data.Common.Enums;
using Ben.Web.Services.Help;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// Every help document that exists is a help document somebody can find.
/// </summary>
/// <remarks>
/// <para>The failure this guards against is silent. A document with a typo in its front matter —
/// a misspelled <c>audience</c>, a missing <c>section</c>, a stray character before the opening
/// <c>---</c> — is not an error: it is simply not loaded, and the file sits in the folder looking
/// finished while nobody can reach it. Somebody writes the help, ticks it off, and the feature
/// ships undocumented.</para>
///
/// <para>Nothing else catches it. <c>HelpLinkTargetTests</c> checks that links point at documents
/// that exist, which passes happily when the document is unreachable for every reader; the media
/// tests check pictures. This checks the documents themselves.</para>
/// </remarks>
public sealed class HelpCatalogCompletenessTests
{
    private static DirectoryInfo RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx")))
            dir = dir.Parent;
        return dir ?? throw new InvalidOperationException("repo root not found");
    }

    private static IReadOnlyList<string> ContentFiles()
        => Directory.GetFiles(
            Path.Combine(RepoRoot().FullName, "Ben.Web.Services", "Help", "Content"), "*.md");

    [Fact]
    public void Every_markdown_file_in_the_content_folder_is_loaded()
    {
        var onDisk = ContentFiles()
            .Select(Path.GetFileNameWithoutExtension)
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

        var loaded = HelpContentService.LoadAll()
            .Select(d => d.Slug)
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

        var missing = onDisk.Except(loaded, StringComparer.Ordinal).ToList();

        Assert.True(missing.Count == 0,
            "These help documents exist as files but were not loaded, so nobody can reach them. "
            + "Almost always a front-matter problem: a missing or misspelled title, summary, "
            + "section, audience or order, or something before the opening '---'. The file looks "
            + "finished and the feature ships undocumented:\n  "
            + string.Join("\n  ", missing));

        // The reverse would mean the loader inventing documents, which cannot happen — but a
        // count of zero would leave this test passing while checking nothing.
        Assert.True(onDisk.Count > 5, $"Only {onDisk.Count} help files were found — has the folder moved?");
    }

    [Fact]
    public void Every_document_is_visible_to_the_audience_it_names()
    {
        // A document nobody can see is the same as no document. This catches an audience set
        // higher than anyone will ever hold, which the loader accepts silently.
        var service = new HelpContentService();

        foreach (var document in HelpContentService.LoadAll())
        {
            var viewer = new HelpViewer(document.Audience);

            Assert.True(service.Find(document.Slug, viewer) is not null,
                $"'{document.Slug}' declares audience {document.Audience} but is not returned to a "
                + "reader who holds exactly that audience. Nobody can reach it.");
        }
    }

    [Fact]
    public void Everyone_documents_are_reachable_without_signing_in()
    {
        // The public microsite links help, and a document marked Everyone that an anonymous
        // visitor cannot open is the "authors see what visitors cannot" trap in a new place.
        var service = new HelpContentService();

        foreach (var document in HelpContentService.LoadAll().Where(d => d.Audience == HelpAudience.Everyone))
        {
            Assert.True(service.Find(document.Slug, HelpViewer.Anonymous) is not null,
                $"'{document.Slug}' is marked Everyone but an anonymous visitor cannot open it.");
        }
    }
}
