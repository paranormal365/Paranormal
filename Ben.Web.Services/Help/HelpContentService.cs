using Ben.Data.Common.Enums;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text.RegularExpressions;
using Markdig;

namespace Ben.Web.Services.Help;

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
    // Derived rather than hard-coded: a literal assembly name here survives a project move as a
    // clean compile that silently finds zero documents, which reads as "help is empty" rather
    // than as a build failure.
    private static readonly string ResourcePrefix =
        typeof(HelpContentService).Assembly.GetName().Name + ".Help.Content.";

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
    public IReadOnlyList<HelpSection> SectionsFor(HelpViewer viewer, Func<string, bool>? featureOn = null)
        => _documents.Value
            .Where(d => viewer.CanSee(d) && FeatureAllows(d, featureOn))
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
    public HelpDocument? Find(string slug, HelpViewer viewer, Func<string, bool>? featureOn = null)
    {
        var doc = _documents.Value.FirstOrDefault(
            d => string.Equals(d.Slug, slug, StringComparison.OrdinalIgnoreCase));
        return doc is not null && viewer.CanSee(doc) && FeatureAllows(doc, featureOn) ? doc : null;
    }

    /// <summary>
    /// A document that names a feature is shown only while that feature is on, so a help topic
    /// never advertises a page that answers "not found" - found 2026-09-03, when Reading
    /// Publications sat on the public Help index while Publications was switched off. Both pages
    /// that show help pass the site's feature switch. A caller with no switch to ask (the tests, the
    /// PDF build) sees the whole catalogue, which is what a catalogue check wants.
    /// </summary>
    private static bool FeatureAllows(HelpDocument doc, Func<string, bool>? featureOn)
        => doc.Feature is null || featureOn is null || featureOn(doc.Feature);

    /// <summary>Renders a document's markdown to HTML.</summary>
    /// <remarks>
    /// Screenshots for the administrator documents are embedded in this assembly and inlined here
    /// as data URIs — see <see cref="InlineEmbeddedMedia"/>. Every other document's screenshots are
    /// ordinary static files under the site's wwwroot and pass through untouched.
    /// </remarks>
    public string ToHtml(HelpDocument document)
        => ToHtml(document, live: null);

    /// <summary>
    /// Renders a document with today's prices in it and the passages that apply today (10/21/2026).
    /// </summary>
    /// <remarks>
    /// Ben: the help text must read the live prices from the site settings, not numbers typed into it.
    /// See <see cref="ApplyLiveValues"/> for the two markers a document may use.
    /// </remarks>
    public string ToHtml(HelpDocument document, HelpLiveValues? live)
        => Markdown.ToHtml(InlineEmbeddedMedia(ApplyLiveValues(document.Markdown, live)), _pipeline);

    // ── Live values (10/21/2026) ──────────────────────────────────────────────

    /// <summary>A value marker, e.g. <c>{{price.event-credit}}</c>.</summary>
    private static readonly Regex ValueMarker = new(@"\{\{(?<name>[a-z][a-z0-9.-]*)\}\}", RegexOptions.Compiled);

    /// <summary>A passage marker on a line of its own: <c>{{#if name}}</c> … <c>{{/if}}</c>.</summary>
    private static readonly Regex PassageMarker = new(
        @"^[ \t]*\{\{#if (?<name>[a-z][a-z0-9.-]*)\}\}[ \t]*\r?\n(?<body>.*?)^[ \t]*\{\{/if\}\}[ \t]*(\r?\n|$)",
        RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.Singleline);

    /// <summary>
    /// What a value reads as when the live one isn't known: a page whose prices could not be read,
    /// and the printed product documentation, which has no live site behind it. Words, never an old
    /// number — except the ghost-walk price, which is Ben's own default (10/21/2026) and is only ever
    /// printed, since the on-screen passages that use it are hidden whenever the offer can't be read.
    /// <c>docs/build-documentation-pdf.py</c> carries the same table.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> Fallbacks = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [HelpLiveValues.EventCreditPrice]           = "the price shown on the Pricing page",
        [HelpLiveValues.EventCreditSubscriberPrice] = "a share of that price",
        [HelpLiveValues.EventCreditSubscriberPercent] = "the percentage shown on the Pricing page",
        [HelpLiveValues.GhostWalkMonthlyPrice]      = "$29",
    };

    /// <summary>
    /// Fills a document's value markers and keeps or drops its passages.
    /// </summary>
    /// <remarks>
    /// <para><b>Values.</b> <c>{{price.event-credit}}</c> and the others in <see cref="HelpLiveValues"/>
    /// become today's figure, or the words in <see cref="Fallbacks"/> when it isn't known.</para>
    ///
    /// <para><b>Passages.</b> A block between <c>{{#if ghost-walk}}</c> and <c>{{/if}}</c>, each on a line
    /// of its own, is shown only while that condition holds — so when the SuperAdmin takes the Ghost Walk
    /// Tour subscription off sale, the help stops mentioning it, as Ben's rule asks. With no live values
    /// at all (the catalogue checks, the printed documentation) every passage is kept: a catalogue check
    /// wants the whole text.</para>
    /// </remarks>
    public static string ApplyLiveValues(string markdown, HelpLiveValues? live)
    {
        var text = PassageMarker.Replace(markdown, m =>
            live is null || live.Conditions.Contains(m.Groups["name"].Value) ? m.Groups["body"].Value : string.Empty);

        return ValueMarker.Replace(text, m =>
        {
            var name = m.Groups["name"].Value;
            if (live?.Values.TryGetValue(name, out var value) == true) return value;
            return Fallbacks.TryGetValue(name, out var words) ? words : m.Value;
        });
    }

    // ── Embedded media ────────────────────────────────────────────────────────

    /// <summary>
    /// Matches an image whose target uses the <c>help-media:</c> scheme, e.g.
    /// <c>![The audit log](help-media:site-administration/audit-log.png)</c>.
    /// </summary>
    private static readonly Regex EmbeddedImage = new(
        @"!\[(?<alt>[^\]]*)\]\(help-media:(?<path>[A-Za-z0-9][A-Za-z0-9._/-]*)\)",
        RegexOptions.Compiled);

    // Documents cannot change without a redeploy, so a screenshot's base64 form is computed once
    // rather than on every page view. Without this, opening an administration document re-encoded
    // a megabyte of PNG each time.
    private static readonly ConcurrentDictionary<string, string?> DataUriCache = new();

    /// <summary>
    /// Replaces <c>help-media:</c> image targets with data URIs read from this assembly's embedded
    /// resources.
    /// </summary>
    /// <remarks>
    /// <para>Data URIs rather than an image endpoint because a plain <c>&lt;img&gt;</c> sends no
    /// bearer token: an endpoint that applied the document's own audience gate would refuse
    /// exactly the readers allowed to see the picture. Inlining also means an administration
    /// screenshot has no URL to guess, which is the same reason the text is embedded.</para>
    ///
    /// <para>A reference with no matching resource degrades to the alt text in italics. A broken
    /// image icon in a help document is worse than a caption, and <c>HelpMediaReferenceTests</c>
    /// fails the build for a missing file, so this path only runs if that guard is bypassed.</para>
    /// </remarks>
    internal static string InlineEmbeddedMedia(string markdown)
        => EmbeddedImage.Replace(markdown, match =>
        {
            var alt  = match.Groups["alt"].Value;
            var path = match.Groups["path"].Value;

            // No traversal: the path becomes a resource name, and ".." in one would only ever be
            // a mistake or an attempt at one.
            if (path.Contains("..", StringComparison.Ordinal))
                return $"*{alt}*";

            var uri = DataUriCache.GetOrAdd(path, BuildDataUri);
            return uri is null ? $"*{alt}*" : $"![{alt}]({uri})";
        });

    /// <summary>Reads one embedded screenshot and returns it as a data URI, or null if absent.</summary>
    private static string? BuildDataUri(string path)
    {
        var assembly = typeof(HelpContentService).Assembly;

        using var stream = assembly.GetManifestResourceStream(ResourceNameFor(path));
        if (stream is null) return null;

        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);

        var mime = path.EndsWith(".gif", StringComparison.OrdinalIgnoreCase) ? "image/gif" : "image/png";
        return $"data:{mime};base64,{Convert.ToBase64String(buffer.ToArray())}";
    }

    /// <summary>The manifest resource name for a <c>slug/file.png</c> reference.</summary>
    /// <remarks>
    /// MSBuild builds a resource name by turning path separators into dots — but it also replaces
    /// characters that cannot appear in an identifier within each *directory* segment, so
    /// <c>Help/Media/site-administration/audit-log.png</c> embeds as
    /// <c>…Help.Media.site_administration.audit-log.png</c>: the folder's hyphen becomes an
    /// underscore and the file name's does not. Reconstructing the name without that asymmetry
    /// reported every screenshot as missing, which is what the guard test caught.
    /// </remarks>
    private static string ResourceNameFor(string path)
    {
        var prefix = typeof(HelpContentService).Assembly.GetName().Name + ".Help.Media.";

        var split = path.LastIndexOf('/');
        if (split < 0) return prefix + path;

        var folders = path[..split].Replace('-', '_').Replace('/', '.');
        var file = path[(split + 1)..];
        return $"{prefix}{folders}.{file}";
    }

    /// <summary>
    /// Whether this assembly carries the screenshot a document references, e.g.
    /// <c>site-administration/audit-log.png</c>. Used by the guard test that pairs references
    /// against the files that back them.
    /// </summary>
    internal static bool EmbeddedMediaExists(string path)
        => !path.Contains("..", StringComparison.Ordinal)
           && typeof(HelpContentService).Assembly
                  .GetManifestResourceNames()
                  .Contains(ResourceNameFor(path), StringComparer.Ordinal);

    /// <summary>
    /// The headings inside a document, for its on-page contents list. Level-2 headings only —
    /// deeper ones make the list longer than the document.
    /// </summary>
    public static IReadOnlyList<(string Text, string Anchor)> HeadingsOf(HelpDocument document)
        => HeadingsOf(document, live: null);

    /// <summary>The contents list for the text as it will actually be shown, passages applied.</summary>
    public static IReadOnlyList<(string Text, string Anchor)> HeadingsOf(HelpDocument document, HelpLiveValues? live)
        => ApplyLiveValues(document.Markdown, live)
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
            Markdown: body,
            Feature:  fields.TryGetValue("feature", out var feature) && !string.IsNullOrWhiteSpace(feature) ? feature : null,
            Role:     fields.TryGetValue("role", out var role) && !string.IsNullOrWhiteSpace(role) ? role : null);
    }
}

/// <summary>
/// Today's prices and switches, for a help document to read (10/21/2026).
/// </summary>
/// <param name="Values">Marker name to the text it becomes, e.g. "price.event-credit" → "$99".</param>
/// <param name="Conditions">The passage conditions that hold today, e.g. "ghost-walk".</param>
public sealed record HelpLiveValues(IReadOnlyDictionary<string, string> Values, IReadOnlySet<string> Conditions)
{
    /// <summary>One event credit at the full price.</summary>
    public const string EventCreditPrice = "price.event-credit";

    /// <summary>One event credit at the paid-subscriber price.</summary>
    public const string EventCreditSubscriberPrice = "price.event-credit-subscriber";

    /// <summary>The paid-subscriber percentage, with its % sign.</summary>
    public const string EventCreditSubscriberPercent = "percent.event-credit-subscriber";

    /// <summary>One month of the Ghost Walk Tour subscription.</summary>
    public const string GhostWalkMonthlyPrice = "price.ghost-walk-month";

    /// <summary>The passage condition that holds while the Ghost Walk Tour subscription is on sale.</summary>
    public const string GhostWalkOnSale = "ghost-walk";

    /// <summary>
    /// The passage condition that holds while it is NOT on sale — where the per-tour business plan
    /// still prices tours (10/21/2026).
    /// </summary>
    public const string GhostWalkOffSale = "no-ghost-walk";

    /// <summary>
    /// Builds the values from the price list's extras, the same figures the Pricing page shows.
    /// Null extras gives an empty set, so every value reads as its fallback words and every
    /// conditional passage is left out.
    /// </summary>
    public static HelpLiveValues From(Ben.Service.Models.PublicPricingExtras? extras, Func<decimal, string> money)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var conditions = new HashSet<string>(StringComparer.Ordinal);
        if (extras is { } x)
        {
            values[EventCreditPrice] = money(x.Events.UnitPrice);
            values[EventCreditSubscriberPrice] = money(x.Events.SubscriberUnitPrice);
            values[EventCreditSubscriberPercent] = $"{x.Events.SubscriberPercent:0.##}%";
            if (x.GhostWalk is { } walk)
            {
                values[GhostWalkMonthlyPrice] = money(walk.MonthlyPrice);
                conditions.Add(GhostWalkOnSale);
            }
            else
            {
                conditions.Add(GhostWalkOffSale);
            }
        }
        return new HelpLiveValues(values, conditions);
    }
}
