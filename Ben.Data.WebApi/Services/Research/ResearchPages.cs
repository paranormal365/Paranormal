using System.Text.RegularExpressions;
using Ben.Data.Common.Blocks;
using Ben.Data.Common.Enums;
using Ben.Data.Common.Text;
using Ben.Data.Source.Entities;

namespace Ben.Data.WebApi.Services.Research;

/// <summary>
/// The rules of a research page: which copy a reader gets, what a draft may contain, and its plain-text summary.
/// </summary>
/// <remarks>
/// <para>Beta feedback, 2026-09-14: a research note became a page of blocks — text, pictures, files, link cards and maps
/// — saved as a draft while it is written and shown to the rest of the group only when it is published.</para>
/// <para>Everything a posted page contains is decided here, on the server, whatever the editor sent: text is sanitized,
/// a picture or file must be one uploaded to this page, a link card's picture must be one of this site's own copies,
/// and a map's numbers must be places on Earth. The editor cannot be the thing that keeps a page safe to draw.</para>
/// </remarks>
public static partial class ResearchPages
{
    public const int MaxBlocks = 500;
    public const int MaxMapStops = BlockMapLimits.MaxStops;

    /// <summary>A legacy note — written before pages existed — has neither copy stored and counts as published.</summary>
    public static bool IsLegacyNote(CaseResearchEntry e) =>
        e.ResearchType == CaseResearchType.Note && e.DraftBlocksJson is null && e.PublishedBlocksJson is null;

    public static bool IsPublished(CaseResearchEntry e) => e.PublishedUtc is not null || IsLegacyNote(e);

    /// <summary>A draft exists that is not what is published.</summary>
    public static bool HasUnpublishedDraft(CaseResearchEntry e) =>
        e.DraftBlocksJson is not null && e.DraftRevision != (e.PublishedRevision ?? -1);

    /// <summary>What everybody but the draft's author reads.</summary>
    public static BlockDocument PublishedOf(CaseResearchEntry e, ICmsMarkupSanitizer sanitizer)
    {
        if (e.PublishedBlocksJson is not null) return BlockDocumentSerializer.Parse(e.PublishedBlocksJson);
        if (!IsLegacyNote(e) || !PlainTextHtml.HasText(e.Body)) return new BlockDocument();

        // A note from before pages: its body, shown as one text block. Sanitized on the way out because these bodies were
        // stored as sent.
        var html = PlainTextHtml.LooksLikeHtml(e.Body) ? sanitizer.SanitizeHtml(e.Body) : PlainTextHtml.FromPlainText(e.Body);
        return new BlockDocument { Blocks = [new Block { Id = $"legacy-{e.Id:N}", Kind = BlockKinds.Text, Html = html }] };
    }

    /// <summary>What the draft's author works on: the draft, or the published copy to start a new draft from.</summary>
    public static BlockDocument DraftOf(CaseResearchEntry e, ICmsMarkupSanitizer sanitizer) =>
        e.DraftBlocksJson is not null ? BlockDocumentSerializer.Parse(e.DraftBlocksJson) : PublishedOf(e, sanitizer);

    /// <summary>The summary lists and the timeline show.</summary>
    public static string? ExcerptOf(CaseResearchEntry e, ICmsMarkupSanitizer sanitizer)
    {
        if (e.Excerpt is not null) return e.Excerpt;
        if (e.ResearchType != CaseResearchType.Note) return null;
        var text = BlockDocumentSerializer.PlainTextExcerpt(PublishedOf(e, sanitizer));
        return text.Length == 0 ? null : text;
    }

    /// <summary>An upload a block may show: one in this page's rail.</summary>
    public sealed record AllowedUpload(string FileName, string ContentType, long FileSize);

    /// <summary>
    /// The document as it may be stored, or the sentence saying why it may not.
    /// </summary>
    /// <param name="uploads">The uploads in this page's rail, by id. A block may show nothing else.</param>
    public static (BlockDocument? Document, string? Refusal) Clean(
        BlockDocument doc, ICmsMarkupSanitizer sanitizer, IReadOnlyDictionary<Guid, AllowedUpload> uploads)
    {
        if (doc.Version > BlockDocument.CurrentVersion)
            return (null, "This page was saved by a newer version of the site. Reload to edit it.");
        if (doc.Blocks.Count > MaxBlocks)
            return (null, $"A page can hold up to {MaxBlocks} blocks.");

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var clean = new BlockDocument();

        foreach (var b in doc.Blocks)
        {
            if (b is null || string.IsNullOrWhiteSpace(b.Id) || b.Id.Length > 64 || !ids.Add(b.Id))
                return (null, "The page has a block without a usable id. Reload and try again.");
            if (!BlockKinds.All.Contains(b.Kind))
                return (null, $"The page has a kind of block this site doesn't know (\"{Cap(b.Kind, 20)}\").");

            var kept = new Block { Id = b.Id, Kind = b.Kind };
            switch (b.Kind)
            {
                case BlockKinds.Text:
                    kept.Html = CleanHtml(b.Html, sanitizer);
                    break;

                case BlockKinds.Image:
                case BlockKinds.File:
                    if (b.UploadFileId is not { } fileId || !uploads.TryGetValue(fileId, out var upload))
                        return (null, "A picture or file on the page isn't one uploaded to this page.");
                    if (b.Kind == BlockKinds.Image && !upload.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                        return (null, $"\"{upload.FileName}\" isn't a picture, so it can only be shown as a file.");
                    kept.UploadFileId = fileId;
                    kept.FileName = upload.FileName;
                    kept.ContentType = upload.ContentType;
                    kept.FileSize = upload.FileSize;
                    kept.Caption = Words(b.Caption, 500);
                    if (b.Kind == BlockKinds.Image) kept.Alt = Words(b.Alt, 300);
                    break;

                case BlockKinds.Link:
                    if (!Uri.TryCreate(b.Url?.Trim(), UriKind.Absolute, out var url)
                        || url.Scheme is not ("http" or "https") || url.AbsoluteUri.Length > 2000)
                        return (null, "A link on the page isn't a web address.");
                    kept.Url = url.AbsoluteUri;
                    kept.Title = Words(b.Title, 200);
                    kept.PreviewTitle = Words(b.PreviewTitle, 200);
                    kept.PreviewDescription = Words(b.PreviewDescription, 500);
                    kept.PreviewSiteName = Words(b.PreviewSiteName, 100);
                    // Worked out here, never trusted: the domain is what tells a reader where the link really goes.
                    kept.PreviewDomain = url.Host;
                    kept.PreviewImageUrl = b.PreviewImageUrl is { } img && OwnPreviewImage().IsMatch(img) ? img : null;
                    kept.PreviewFetchedUtc = b.PreviewFetchedUtc;
                    break;

                case BlockKinds.Map:
                    var stops = b.MapStops ?? [];
                    if (stops.Count > MaxMapStops)
                        return (null, $"A map can show up to {MaxMapStops} places.");
                    var keptStops = new List<BlockMapStop>();
                    foreach (var s in stops)
                    {
                        if (s is null || double.IsNaN(s.Latitude) || double.IsNaN(s.Longitude)
                            || s.Latitude is < -90 or > 90 || s.Longitude is < -180 or > 180)
                            return (null, "A place on a map isn't a place on Earth.");
                        keptStops.Add(new BlockMapStop(
                            Math.Round(s.Latitude, 6), Math.Round(s.Longitude, 6),
                            Words(s.Label, 120), Words(s.Note, 500), Words(s.AddressText, 300), s.PlaceId));
                    }
                    var route = string.IsNullOrWhiteSpace(b.MapRoute) ? BlockMapRoutes.None : b.MapRoute;
                    if (!BlockMapRoutes.All.Contains(route))
                        return (null, "A map on the page has a kind of route this site doesn't know.");
                    kept.MapStops = keptStops;
                    kept.MapRoute = keptStops.Count < 2 ? BlockMapRoutes.None : route;
                    kept.Zoom = b.Zoom is { } z && !double.IsNaN(z) ? Math.Clamp(z, 2, 20) : null;
                    kept.Caption = Words(b.Caption, 500);
                    break;
            }

            clean.Blocks.Add(kept);
        }

        return (clean, null);
    }

    /// <summary>
    /// Sanitized text-block HTML with any picture that does not point at an absolute https address removed.
    /// </summary>
    /// <remarks>
    /// A picture pasted into a text block with a relative address is almost always one of this site's media addresses,
    /// which carry a ticket for one viewer that stops working within the hour — stored, it would be a broken image for
    /// everybody else. Pictures belong in picture blocks, which store the upload rather than an address.
    /// </remarks>
    private static string CleanHtml(string? html, ICmsMarkupSanitizer sanitizer)
    {
        if (string.IsNullOrWhiteSpace(html)) return "";
        var sanitized = sanitizer.SanitizeHtml(html);
        return ImgTag().Replace(sanitized, m => HttpsSrc().IsMatch(m.Value) && !m.Value.Contains("/media/", StringComparison.OrdinalIgnoreCase)
            ? m.Value
            : string.Empty).Trim();
    }

    /// <summary>Plain words, tags removed, trimmed and capped; null when nothing is left.</summary>
    private static string? Words(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var text = PlainTextHtml.ToText(value).Replace('\n', ' ').Trim();
        return text.Length == 0 ? null : Cap(text, max);
    }

    private static string Cap(string value, int max) => value.Length <= max ? value : value[..max];

    [GeneratedRegex(@"<img\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex ImgTag();

    [GeneratedRegex(@"\bsrc\s*=\s*[""']https://", RegexOptions.IgnoreCase)]
    private static partial Regex HttpsSrc();

    [GeneratedRegex(@"^/media/link-preview/[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$")]
    private static partial Regex OwnPreviewImage();
}
