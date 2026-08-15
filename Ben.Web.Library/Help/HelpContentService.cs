using Ben.Data.Common.Enums;
using System.Reflection;
using Markdig;

namespace Ben.Web.Library.Help;

/// <summary>
/// Loads the help documents and renders them.
/// </summary>
/// <remarks>
/// <para>Documents are markdown files embedded in this assembly (<c>Help/Content/*.md</c>), so
/// they version with the code that they describe and cannot drift into a separate deployment.
/// Embedded rather than under <c>wwwroot</c> on purpose: a file in wwwroot is served raw to
/// anyone who guesses its name, which would leak the app-administration documents past the
/// audience gate.</para>
///
/// <para>Parsed once and cached — the content cannot change without a redeploy, so re-reading it
/// per request would buy nothing.</para>
/// </remarks>
public sealed class HelpContentService
{
    private const string ResourcePrefix = "Ben.Web.Library.Help.Content.";

    private readonly Lazy<IReadOnlyList<HelpDocument>> _documents;
    private readonly MarkdownPipeline _pipeline;

    public HelpContentService()
    {
        _documents = new Lazy<IReadOnlyList<HelpDocument>>(LoadAll);
        _pipeline = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            // Heading anchors are the reason the in-app help links can deep-link at all.
            .UseAutoIdentifiers()
            // Raw HTML is stripped even though every document is repo-authored and therefore
            // trusted. The rendered output goes through MarkupString, so if a document ever does
            // become editable this is already the safe default rather than a retrofit.
            .DisableHtml()
            .Build();
    }

    /// <summary>Every document this reader may see, grouped into sections in display order.</summary>
    public IReadOnlyList<HelpSection> SectionsFor(HelpViewer viewer)
        => _documents.Value
            .Where(d => viewer.CanSee(d.Audience))
            .GroupBy(d => d.Section)
            .OrderBy(g => g.Min(d => d.Order))
            .Select(g => new HelpSection(g.Key, g.OrderBy(d => d.Order).ThenBy(d => d.Title).ToList()))
            .ToList();

    /// <summary>
    /// One document by slug, or null when it doesn't exist <i>or</i> the reader may not see it.
    /// </summary>
    /// <remarks>
    /// The two cases are deliberately indistinguishable. Returning "forbidden" for a document
    /// that exists would let anyone enumerate the app-administration topics by guessing slugs,
    /// which is a small leak but a free one to avoid.
    /// </remarks>
    public HelpDocument? Find(string slug, HelpViewer viewer)
    {
        var doc = _documents.Value.FirstOrDefault(
            d => string.Equals(d.Slug, slug, StringComparison.OrdinalIgnoreCase));
        return doc is not null && viewer.CanSee(doc.Audience) ? doc : null;
    }

    /// <summary>Renders a document's markdown to HTML.</summary>
    public string ToHtml(HelpDocument document) => Markdown.ToHtml(document.Markdown, _pipeline);

    /// <summary>
    /// The headings inside a document, for its on-page contents list. Level-2 headings only —
    /// deeper ones make the list longer than the document.
    /// </summary>
    public static IReadOnlyList<(string Text, string Anchor)> HeadingsOf(HelpDocument document)
        => document.Markdown
            .Split('\n')
            .Select(line => line.TrimEnd('\r'))
            .Where(line => line.StartsWith("## ") && !line.StartsWith("###"))
            .Select(line => line[3..].Trim())
            .Select(text => (text, Slugify(text)))
            .ToList();

    /// <summary>
    /// Mirrors Markdig's UseAutoIdentifiers so the contents list links to anchors that exist.
    /// </summary>
    /// <remarks>
    /// Duplicating the algorithm is unfortunate but the alternative is parsing the document twice
    /// to read Markdig's own ids. Kept simple and covered by tests that assert a rendered heading
    /// carries the id this produces — if Markdig's scheme ever changes, those tests fail rather
    /// than the contents list quietly linking nowhere.
    /// </remarks>
    internal static string Slugify(string text)
    {
        var chars = text.ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : (c is ' ' or '-' or '_' ? '-' : '\0'))
            .Where(c => c != '\0')
            .ToArray();

        var slug = new string(chars);
        while (slug.Contains("--")) slug = slug.Replace("--", "-");
        return slug.Trim('-');
    }

    // ── Loading ───────────────────────────────────────────────────────────────

    internal static IReadOnlyList<HelpDocument> LoadAll()
    {
        var assembly = typeof(HelpContentService).Assembly;
        var docs = new List<HelpDocument>();

        foreach (var name in assembly.GetManifestResourceNames()
                                     .Where(n => n.StartsWith(ResourcePrefix) && n.EndsWith(".md")))
        {
            using var stream = assembly.GetManifestResourceStream(name);
            if (stream is null) continue;
            using var reader = new StreamReader(stream);
            var raw = reader.ReadToEnd();

            var slug = name[ResourcePrefix.Length..^3];
            var parsed = Parse(slug, raw);
            if (parsed is not null) docs.Add(parsed);
        }

        return docs.OrderBy(d => d.Order).ThenBy(d => d.Title).ToList();
    }

    /// <summary>
    /// Splits a document's front matter from its body. A file with no front matter is skipped
    /// rather than guessed at — an untitled, unfiled document in the navigation is worse than
    /// a missing one, and the omission is obvious the moment someone looks for it.
    /// </summary>
    internal static HelpDocument? Parse(string slug, string raw)
    {
        var text = raw.Replace("\r\n", "\n");
        if (!text.StartsWith("---\n")) return null;

        var end = text.IndexOf("\n---", 4, StringComparison.Ordinal);
        if (end < 0) return null;

        var frontMatter = text[4..end];
        var body = text[(end + 4)..].TrimStart('\n');

        var fields = frontMatter
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Split(':', 2))
            .Where(p => p.Length == 2)
            .ToDictionary(p => p[0].Trim().ToLowerInvariant(), p => p[1].Trim());

        if (!fields.TryGetValue("title", out var title) || string.IsNullOrWhiteSpace(title))
            return null;

        return new HelpDocument(
            Slug:     slug,
            Title:    title,
            Summary:  fields.GetValueOrDefault("summary", ""),
            Section:  fields.GetValueOrDefault("section", "General"),
            Audience: Enum.TryParse<HelpAudience>(fields.GetValueOrDefault("audience"), true, out var a)
                        ? a
                        // Unparseable audience falls back to the most restrictive, not the most
                        // open. A typo in front matter must never publish an internal document.
                        : HelpAudience.AppAdministrator,
            Order:    int.TryParse(fields.GetValueOrDefault("order"), out var o) ? o : 500,
            Markdown: body);
    }
}
