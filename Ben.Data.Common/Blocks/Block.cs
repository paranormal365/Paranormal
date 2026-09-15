namespace Ben.Data.Common.Blocks;

/// <summary>
/// One block in a block document: a paragraph of text, a picture, a file, a link card or a map.
/// </summary>
/// <remarks>
/// <para>Flat, with every field optional, rather than a class per kind: the document is JSON that the website edits,
/// the API cleans and the database stores, and a flat shape serializes the same way on all three with no type
/// discriminator to keep in step. Which fields mean anything is decided by <see cref="Kind"/>, and the API's cleaning
/// step drops the rest.</para>
/// <para>Nothing here names a case, a group or a place. The block editor is a Kit component meant for more than
/// research pages (Ben, 2026-09-14), so its model knows only about blocks.</para>
/// </remarks>
public sealed class Block
{
    /// <summary>Stable within its document; the editor uses it to move and focus blocks.</summary>
    public string Id { get; set; } = "";

    /// <summary>One of <see cref="BlockKinds"/>.</summary>
    public string Kind { get; set; } = BlockKinds.Text;

    // ── text ──────────────────────────────────────────────────────────────────────────────────────────
    /// <summary>Text: sanitized HTML.</summary>
    public string? Html { get; set; }

    // ── image and file ───────────────────────────────────────────────────────────────────────────────
    /// <summary>
    /// Image and file: the stored upload. Never a URL: the site's media addresses carry a ticket for one viewer and
    /// one hour, so the address is made for each reader when the page is drawn.
    /// </summary>
    public Guid? UploadFileId { get; set; }

    public string? FileName { get; set; }
    public string? ContentType { get; set; }
    public long? FileSize { get; set; }

    /// <summary>Image: what the picture shows, for someone who cannot see it.</summary>
    public string? Alt { get; set; }

    /// <summary>Image, file, map: a line under the block.</summary>
    public string? Caption { get; set; }

    // ── link ─────────────────────────────────────────────────────────────────────────────────────────
    /// <summary>Link: the address, http or https.</summary>
    public string? Url { get; set; }

    /// <summary>Link: the author's own title for it, when they gave one.</summary>
    public string? Title { get; set; }

    /// <summary>Link: the page's own title, copied from the site's link preview when the block was made.</summary>
    public string? PreviewTitle { get; set; }

    public string? PreviewDescription { get; set; }

    /// <summary>Link: the preview picture, always one of this site's own un-ticketed link-preview addresses.</summary>
    public string? PreviewImageUrl { get; set; }

    public string? PreviewSiteName { get; set; }

    /// <summary>Link: the host, worked out from <see cref="Url"/> by the API rather than trusted from the editor.</summary>
    public string? PreviewDomain { get; set; }

    public DateTime? PreviewFetchedUtc { get; set; }

    // ── map ──────────────────────────────────────────────────────────────────────────────────────────
    /// <summary>Map: the places, in route order. Empty until the author chooses one — never filled in for them.</summary>
    public List<BlockMapStop>? MapStops { get; set; }

    /// <summary>Map: one of <see cref="BlockMapRoutes"/>.</summary>
    public string? MapRoute { get; set; }

    /// <summary>Map: the zoom the author left it at.</summary>
    public double? Zoom { get; set; }
}

/// <summary>One place on a map block.</summary>
/// <param name="PlaceId">A place listed on this site, when the stop is one — kept for linking, never required.</param>
public sealed record BlockMapStop(
    double Latitude,
    double Longitude,
    string? Label = null,
    string? Note = null,
    string? AddressText = null,
    Guid? PlaceId = null);

/// <summary>A document of blocks, as stored.</summary>
public sealed class BlockDocument
{
    /// <summary>The version this build writes and reads.</summary>
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;

    public List<Block> Blocks { get; set; } = [];
}
