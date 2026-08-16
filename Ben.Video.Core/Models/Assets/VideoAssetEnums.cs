namespace Ben.Video.Editor.Models.Assets;

/// <summary>
/// The broad category of a video asset served from the asset catalog API.
/// Extend this enum in future versions as new asset categories are supported.
/// </summary>
public enum VideoAssetType
{
    /// <summary>Standalone illustration or photograph used as an overlay or clip.</summary>
    Clipart,

    /// <summary>Overlay graphic with a directional purpose — arrow, speech bubble, highlight ring, etc.</summary>
    Callout,

    /// <summary>Geometric primitive with server-defined colour/size constraints.</summary>
    Shape,

    /// <summary>Border or decorative frame composited around the video frame.</summary>
    Frame,

    /// <summary>Background texture or pattern clip.</summary>
    Texture,

    /// <summary>Pre-composed decorative sticker — typically small and non-editable.</summary>
    Sticker,

    /// <summary>
    /// Watermark asset. Handled by the export pipeline, not the timeline.
    /// Ben.Video will automatically embed this during export when the server
    /// enables it via <see cref="VideoWatermarkConfig.Enabled"/>.
    /// </summary>
    Watermark,
}

/// <summary>
/// The file format of the asset binary stored on the server.
/// Extend as additional formats are introduced.
/// </summary>
public enum VideoAssetFormat
{
    /// <summary>Scalable Vector Graphics — supports per-element control point animation.</summary>
    Svg,

    /// <summary>AV1 Image File Format — high-quality, HDR-capable raster.</summary>
    Avif,

    /// <summary>Portable Network Graphics — lossless raster with alpha.</summary>
    Png,

    /// <summary>WebP — compressed raster, lossy or lossless.</summary>
    WebP,

    /// <summary>Animated GIF — limited palette; treated as a short looping clip.</summary>
    Gif,

    /// <summary>
    /// Lottie JSON animation file — future support.
    /// Will be rendered frame-by-frame via a Lottie player in an OffscreenCanvas worker.
    /// </summary>
    Lottie,
}

/// <summary>
/// The type of manipulation a named SVG control point exposes to the user.
/// Each type maps to a different SVG attribute or transform applied per-frame during export.
/// </summary>
public enum SvgControlPointType
{
    /// <summary>Translate the target SVG element along X and Y axes.</summary>
    Move,

    /// <summary>Uniform scale the target SVG element around its natural origin.</summary>
    Scale,

    /// <summary>Scale only the width of the target element (non-uniform).</summary>
    ScaleX,

    /// <summary>Scale only the height of the target element (non-uniform).</summary>
    ScaleY,

    /// <summary>Rotate the target SVG element around its natural origin (degrees).</summary>
    Rotate,

    /// <summary>Animate the <c>stroke-opacity</c> attribute (0–1). Fades just the outline/stroke.</summary>
    StrokeAlpha,

    /// <summary>Animate the <c>fill-opacity</c> attribute (0–1). Fades just the fill/inner content.</summary>
    FillAlpha,

    /// <summary>Animate both <c>stroke-opacity</c> and <c>fill-opacity</c> together.</summary>
    FullAlpha,

    /// <summary>Animate the <c>stroke</c> colour attribute.</summary>
    StrokeColor,

    /// <summary>Animate the <c>fill</c> colour attribute.</summary>
    FillColor,

    /// <summary>Animate stroke width (<c>stroke-width</c>) of the target element.</summary>
    StrokeWidth,
}

/// <summary>
/// Which provider/service supplied this asset entry.
/// Shown as a badge in the asset browser so the user knows where each item lives.
/// </summary>
public enum AssetSource
{
    /// <summary>
    /// File the user imported themselves — stored in OPFS under <c>bv-clips/</c>.
    /// Always available offline.
    /// </summary>
    LocalOpfs,

    /// <summary>
    /// File from the user's personal account media library on the Ben server
    /// (served by the existing MediaLibrary WebAPI).
    /// </summary>
    AccountLibrary,

    /// <summary>
    /// Shared clipart, callout, or shape from the Ben app's managed asset catalog
    /// (served by <c>GET /api/video-assets</c>).
    /// </summary>
    SharedCatalog,
}

/// <summary>
/// Where a watermark image is composited within the video frame during export.
/// </summary>
public enum WatermarkPosition
{
    TopLeft,
    TopCenter,
    TopRight,
    MiddleLeft,
    Center,
    MiddleRight,
    BottomLeft,
    BottomCenter,
    BottomRight,
}
