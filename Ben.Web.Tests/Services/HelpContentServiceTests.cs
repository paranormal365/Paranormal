using Ben.Data.Common.Enums;
using Ben.Web.Services;
using Ben.Web.Services.Help;
using Markdig;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// Covers the help catalog: what each reader may see, and the two places where a mistake would
/// be silent — a document dropped for bad front matter, and a contents link pointing at an
/// anchor Markdig never generated.
/// </summary>
public sealed class HelpContentServiceTests
{
    private static readonly HelpContentService Service = new();

    private static HelpViewer Viewer(HelpAudience a) => new(a);

    // ── Feature gating ────────────────────────────────────────────────────────
    //
    // 2026-09-03: "Reading Publications" sat on the public Help index while Publications was
    // switched off, so the topic advertised a page that answered "not found". A document may now
    // name the feature it belongs to; the two Help pages pass the site's switch.

    [Fact]
    public void The_publication_topics_belong_to_the_publications_feature()
    {
        var all = Service.SectionsFor(Viewer(HelpAudience.AppAdministrator)).SelectMany(s => s.Documents).ToList();
        foreach (var slug in new[] { "reading-publications", "publishing-with-publications" })
        {
            var doc = Assert.Single(all, d => d.Slug == slug);
            Assert.Equal(SiteFeatures.Publications, doc.Feature);
        }
    }

    [Fact]
    public void A_gated_topic_is_absent_while_its_feature_is_off_and_present_when_on()
    {
        var reader = Viewer(HelpAudience.AppAdministrator);

        var off = Service.SectionsFor(reader, _ => false).SelectMany(s => s.Documents).Select(d => d.Slug).ToList();
        var on  = Service.SectionsFor(reader, _ => true).SelectMany(s => s.Documents).Select(d => d.Slug).ToList();

        Assert.DoesNotContain("reading-publications", off);
        Assert.Contains("reading-publications", on);
        Assert.Null(Service.Find("reading-publications", reader, _ => false));
        Assert.NotNull(Service.Find("reading-publications", reader, _ => true));

        // Ungated topics are untouched by the switch.
        Assert.Contains("getting-started", off);
    }

    [Fact]
    public void The_gate_only_ever_hides_topics_that_name_a_feature()
    {
        // With every feature off, exactly the gated topics disappear — nothing else.
        var reader = Viewer(HelpAudience.AppAdministrator);
        var everything = Service.SectionsFor(reader).SelectMany(s => s.Documents).ToList();
        var allOff     = Service.SectionsFor(reader, _ => false).SelectMany(s => s.Documents).Select(d => d.Slug).ToHashSet();

        foreach (var doc in everything)
            Assert.Equal(doc.Feature is null, allOff.Contains(doc.Slug));
    }

    // ── Audience gating ───────────────────────────────────────────────────────

    [Fact]
    public void Anonymous_reader_sees_only_Everyone_documents()
    {
        var docs = Service.SectionsFor(HelpViewer.Anonymous).SelectMany(s => s.Documents).ToList();

        Assert.NotEmpty(docs);
        Assert.All(docs, d => Assert.Equal(HelpAudience.Everyone, d.Audience));
    }

    [Fact]
    public void Each_step_up_the_ladder_adds_documents_and_removes_none()
    {
        var ladder = new[]
        {
            HelpAudience.Everyone,
            HelpAudience.SignedIn,
            HelpAudience.OrganizationMember,
            HelpAudience.OrganizationAdministrator,
            HelpAudience.AppAdministrator,
        };

        var previous = new HashSet<string>();
        var previousCount = -1;

        foreach (var rung in ladder)
        {
            var slugs = Service.SectionsFor(Viewer(rung))
                .SelectMany(s => s.Documents)
                .Select(d => d.Slug)
                .ToHashSet();

            // The stated rule: higher roles *add* what they can see, never swap it out.
            Assert.True(previous.IsSubsetOf(slugs), $"{rung} lost a document a lower audience could see.");
            Assert.True(slugs.Count > previousCount, $"{rung} added nothing — is a document filed at the wrong audience?");

            previous = slugs;
            previousCount = slugs.Count;
        }
    }

    [Fact]
    public void Find_hides_a_document_the_reader_may_not_see()
    {
        // Picked by audience rather than by name so the test survives a rename.
        var restricted = Service.SectionsFor(Viewer(HelpAudience.AppAdministrator))
            .SelectMany(s => s.Documents)
            .First(d => d.Audience == HelpAudience.AppAdministrator);

        Assert.NotNull(Service.Find(restricted.Slug, Viewer(HelpAudience.AppAdministrator)));
        Assert.Null(Service.Find(restricted.Slug, HelpViewer.Anonymous));
        // Indistinguishable from a slug that does not exist — that is the point.
        Assert.Null(Service.Find("no-such-document", HelpViewer.Anonymous));
    }

    // ── The shipped documents ─────────────────────────────────────────────────

    [Fact]
    public void Every_embedded_markdown_file_becomes_a_document()
    {
        var embedded = typeof(HelpContentService).Assembly
            .GetManifestResourceNames()
            .Count(n => n.StartsWith("Ben.Web.Services.Help.Content.") && n.EndsWith(".md"));

        // A file dropped for missing or malformed front matter simply vanishes from the index,
        // which is exactly the kind of failure nobody notices. Count it instead.
        Assert.Equal(embedded, HelpContentService.LoadAll().Count);
        Assert.True(embedded > 0, "No help documents were embedded — check the csproj EmbeddedResource glob.");
    }

    [Fact]
    public void Every_document_has_a_summary_and_at_least_one_heading()
    {
        foreach (var doc in HelpContentService.LoadAll())
        {
            Assert.False(string.IsNullOrWhiteSpace(doc.Summary), $"{doc.Slug} has no summary.");
            Assert.NotEmpty(HelpContentService.HeadingsOf(doc));
        }
    }

    // ── Front matter ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Nonsense")]   // typo
    [InlineData("")]           // present but blank
    [InlineData("everyone ")]  // trailing space is fine — parses
    public void Audience_that_does_not_parse_falls_back_to_the_most_restrictive(string audience)
    {
        var raw = $"---\ntitle: T\nsection: S\naudience: {audience}\norder: 1\n---\n\n## H\n\nbody\n";

        var parsed = HelpContentService.Parse("t", raw);

        Assert.NotNull(parsed);
        var expected = audience.Trim().Equals("everyone", StringComparison.OrdinalIgnoreCase)
            ? HelpAudience.Everyone
            : HelpAudience.AppAdministrator;
        Assert.Equal(expected, parsed!.Audience);
    }

    [Fact]
    public void Document_without_front_matter_is_skipped_rather_than_guessed_at()
    {
        Assert.Null(HelpContentService.Parse("t", "## Just a heading\n\nbody"));
        Assert.Null(HelpContentService.Parse("t", "---\nsection: S\n---\n\nbody"));  // no title
    }

    // ── Anchors ───────────────────────────────────────────────────────────────

    [Fact]
    public void Every_contents_anchor_matches_an_id_Markdig_actually_emits()
    {
        // The contents list is built from our own Slugify; the ids in the page come from Markdig.
        // If the two disagree, every "On this page" link silently scrolls nowhere — so check the
        // real headings of the real documents against real rendered output, not a sample.
        foreach (var doc in HelpContentService.LoadAll())
        {
            var html = Service.ToHtml(doc);
            foreach (var (text, anchor) in HelpContentService.HeadingsOf(doc))
            {
                Assert.True(html.Contains($"id=\"{anchor}\"", StringComparison.Ordinal),
                    $"{doc.Slug}: heading \"{text}\" was linked as #{anchor}, which the rendered page does not contain.");
            }
        }
    }

    [Fact]
    public void Headings_list_covers_level_two_only()
    {
        var doc = HelpContentService.Parse(
            "t",
            "---\ntitle: T\naudience: Everyone\n---\n\n# One\n\n## Two\n\n### Three\n\n## Four\n")!;

        Assert.Equal(["Two", "Four"], HelpContentService.HeadingsOf(doc).Select(h => h.Text));
    }
}
