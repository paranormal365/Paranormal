namespace Ben.Data.Common.Enums;

// ─────────────────────────────────────────────────────────────────────────────
// These three enums are a WIRE CONTRACT with Ben.Video.Editor, which defines its
// own copies in Ben.Video.Core/Models/Assets/VideoAssetEnums.cs (a separate repo:
// github.com/VandyBen/Ben.Video).
//
// Neither side configures JsonStringEnumConverter, so these cross the wire as
// INTEGERS. The member ORDER is therefore load-bearing — inserting a value
// anywhere but the end silently remaps every existing asset in the editor's
// cache. Append only, and keep both copies in step.
//
// VideoAssetEnumContractTests pins the numeric values so a reorder fails a test
// rather than a user's project.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Broad category of a video asset — determines timeline behaviour in the editor.</summary>
public enum VideoAssetType
{
    /// <summary>Standalone illustration or photograph used as an overlay or clip.</summary>
    Clipart = 0,

    /// <summary>Overlay graphic with a directional purpose — arrow, speech bubble, highlight ring.</summary>
    Callout = 1,

    /// <summary>Geometric primitive with server-defined colour/size constraints.</summary>
    Shape = 2,

    /// <summary>Border or decorative frame composited around the video frame.</summary>
    Frame = 3,

    /// <summary>Background texture or pattern clip.</summary>
    Texture = 4,

    /// <summary>Pre-composed decorative sticker — typically small and non-editable.</summary>
    Sticker = 5,

    /// <summary>Watermark asset, handled by the export pipeline rather than the timeline.</summary>
    Watermark = 6,
}

/// <summary>File format of the asset binary.</summary>
public enum VideoAssetFormat
{
    /// <summary>Scalable Vector Graphics — supports per-element control point animation.</summary>
    Svg = 0,

    /// <summary>AV1 Image File Format — high-quality, HDR-capable raster.</summary>
    Avif = 1,

    /// <summary>Portable Network Graphics — lossless raster with alpha.</summary>
    Png = 2,

    /// <summary>WebP — compressed raster, lossy or lossless.</summary>
    WebP = 3,

    /// <summary>Animated GIF — limited palette; treated as a short looping clip.</summary>
    Gif = 4,

    /// <summary>Lottie JSON animation. Accepted by the catalog; editor support is future work.</summary>
    Lottie = 5,
}

/// <summary>
/// Which provider supplied a catalog entry. The Ben app only ever emits
/// <see cref="SharedCatalog"/>; the other two are produced inside the editor.
/// </summary>
public enum AssetSource
{
    /// <summary>A file the user imported themselves, cached in the browser.</summary>
    LocalOpfs = 0,

    /// <summary>A file from the user's own media library on this server.</summary>
    AccountLibrary = 1,

    /// <summary>Shared clipart or callout from this app's managed catalog.</summary>
    SharedCatalog = 2,
}
