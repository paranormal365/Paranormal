namespace Ben.Video.Editor.Models.Assets;

/// <summary>
/// A single item in the video asset catalog returned by
/// <c>GET /api/video-assets</c>.
///
/// <para>This record is the canonical contract between the Ben app's WebAPI and
/// Ben.Video.Editor. The Ben app serialises this; Ben.Video.Editor deserialises it.
/// Unknown JSON fields are captured in <see cref="Metadata"/> for forward compatibility.</para>
/// </summary>
public sealed record VideoAssetCatalogItem
{
    // ── Identity ──────────────────────────────────────────────────────────────

    /// <summary>Stable server-assigned identifier (GUID string or slug).</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Display name shown in the asset browser.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Optional short description for tooltip/detail view.</summary>
    public string? Description { get; init; }

    /// <summary>
    /// Optional category name for grouping in the browser UI (e.g. "Arrows", "Shapes").
    /// </summary>
    public string? Category { get; init; }

    /// <summary>Zero or more search/filter tags.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    // ── Provenance ────────────────────────────────────────────────────────────

    /// <summary>
    /// Which service provided this entry.
    /// The asset browser uses this to show source badges and group/filter items.
    /// </summary>
    public AssetSource Source { get; init; }

    // ── Type and format ───────────────────────────────────────────────────────

    /// <summary>Broad asset category — determines timeline behavior.</summary>
    public VideoAssetType Type { get; init; }

    /// <summary>Binary format of the file returned by the download endpoint.</summary>
    public VideoAssetFormat Format { get; init; }

    // ── Server-side URLs ──────────────────────────────────────────────────────

    /// <summary>
    /// Absolute or relative URL to the thumbnail image (small raster, served without auth).
    /// Used in the asset browser grid before the full file is downloaded.
    /// </summary>
    public string ThumbnailUrl { get; init; } = string.Empty;

    // ── Change detection ──────────────────────────────────────────────────────

    /// <summary>
    /// Content hash (SHA-256 of the binary file, hex string) used by the
    /// local sync logic to detect when a cached copy is stale.
    /// </summary>
    public string Version { get; init; } = string.Empty;

    /// <summary>UTC timestamp of the last server-side modification.</summary>
    public DateTimeOffset UpdatedAt { get; init; }

    // ── File metadata ─────────────────────────────────────────────────────────

    /// <summary>Size of the full asset file in bytes. Used for progress reporting during download.</summary>
    public long FileSizeBytes { get; init; }

    /// <summary>Native width in pixels (or SVG viewBox width). Null if unknown.</summary>
    public int? NativeWidth { get; init; }

    /// <summary>Native height in pixels (or SVG viewBox height). Null if unknown.</summary>
    public int? NativeHeight { get; init; }

    // ── Capability settings ───────────────────────────────────────────────────

    /// <summary>
    /// What the end user is allowed to do with this asset in the editor.
    /// Drives which controls are visible in the side panel.
    /// </summary>
    public VideoAssetSettings Settings { get; init; } = new();

    // ── SVG control points ────────────────────────────────────────────────────

    /// <summary>
    /// Named manipulation handles defined by the admin on SVG line art.
    /// Null for raster formats (Avif, Png, WebP, Gif) and built-in shapes.
    /// Each point becomes an independent keyframe track in MotionKeyframeService
    /// using id <c>"{assetInstanceId}/{PointId}"</c>.
    /// </summary>
    public IReadOnlyList<SvgControlPoint>? ControlPoints { get; init; }

    // ── Built-in shape template (VideoAssetType.Callout only) ─────────────────

    /// <summary>
    /// Present when <see cref="Type"/> is <see cref="VideoAssetType.Callout"/>
    /// and the asset is a server-defined built-in shape template.
    /// Null for SVG/raster callout assets.
    ///
    /// <para>When non-null, adding this asset to the timeline creates a
    /// <see cref="Ben.Video.Editor.Models.CalloutClip"/> pre-configured with
    /// the server's shape type and allowed control points.</para>
    /// </summary>
    public CalloutShapeDefinition? ShapeDefinition { get; init; }

    // ── Forward-compatibility bag ─────────────────────────────────────────────

    /// <summary>
    /// Any additional key-value metadata from the server not covered by typed properties.
    /// Allows the Ben app to add new fields without breaking older Ben.Video builds.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }

    // ── Local cache state (populated by VideoAssetCatalogService, not the API) ─

    /// <summary>
    /// True when the full asset file is already cached in OPFS and ready for use
    /// without a network request. Always true for <see cref="AssetSource.LocalOpfs"/> items.
    /// </summary>
    public bool IsLocalAvailable { get; init; }

    /// <summary>
    /// True when the item is in the local cache but the server has a newer
    /// <see cref="Version"/> — the file should be re-downloaded before next use.
    /// Always false for <see cref="AssetSource.LocalOpfs"/> items.
    /// </summary>
    public bool IsUpdateAvailable { get; init; }

    /// <summary>
    /// True when the server has removed this item but the local OPFS copy is retained
    /// so existing projects can still render. The browser shows a "removed" badge.
    /// </summary>
    public bool IsServerRemoved { get; init; }
}
