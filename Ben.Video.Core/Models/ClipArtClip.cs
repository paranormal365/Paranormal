using Ben.Video.Editor.Models.Assets;

namespace Ben.Video.Editor.Models;

/// <summary>
/// A clipart, callout, or shape asset placed on the timeline as an overlay layer.
/// The asset source file is cached in OPFS; the server-defined capabilities
/// (from <see cref="VideoAssetSettings"/>) control which editor controls are shown.
/// </summary>
public sealed record ClipArtClip : TrackItem
{
    // ── Asset identity (from catalog) ─────────────────────────────────────────

    /// <summary>The <see cref="VideoAssetCatalogItem.Id"/> this clip references.</summary>
    public string AssetId { get; set; } = string.Empty;

    /// <summary>
    /// Which provider the asset came from.
    /// Used to re-download the file if the local OPFS copy is missing.
    /// </summary>
    public AssetSource AssetSource { get; set; } = AssetSource.SharedCatalog;

    /// <summary>File format — drives rendering path (raster overlay vs SVG frame renderer).</summary>
    public VideoAssetFormat AssetFormat { get; set; } = VideoAssetFormat.Png;

    /// <summary>
    /// Snapshot of the server-defined capability settings taken at add-time.
    /// The user keeps working with these settings even if the server later
    /// restricts them, so existing projects are not broken.
    /// </summary>
    public VideoAssetSettings Settings { get; set; } = new();

    /// <summary>
    /// Snapshot of SVG control point definitions taken at add-time.
    /// Null for raster assets.
    /// </summary>
    public IReadOnlyList<SvgControlPoint>? ControlPoints { get; set; }

    // ── Canvas geometry (canvas fractions 0–1) ────────────────────────────────

    /// <summary>Horizontal position of the top-left corner as a fraction of the frame width.</summary>
    public double X { get; set; } = 0.1;

    /// <summary>Vertical position of the top-left corner as a fraction of the frame height.</summary>
    public double Y { get; set; } = 0.1;

    /// <summary>Width as a fraction of the frame width.</summary>
    public double Width { get; set; } = 0.2;

    /// <summary>Height as a fraction of the frame height. -1 = preserve aspect ratio from Width.</summary>
    public double Height { get; set; } = -1.0;

    /// <summary>Rotation in degrees (clockwise).</summary>
    public double Rotation { get; set; }

    // ── Appearance ────────────────────────────────────────────────────────────

    /// <summary>Overall opacity of the layer (0–1). Only shown when <see cref="VideoAssetSettings.AllowOpacity"/> is true.</summary>
    public double Opacity { get; set; } = 1.0;

    /// <summary>
    /// Recolor tint as a packed ARGB double (via <see cref="Effects.ColorHelper"/>).
    /// Applied as a color-matrix filter during export.
    /// Only used when <see cref="VideoAssetSettings.AllowRecolor"/> is true.
    /// Default: transparent (no tint).
    /// </summary>
    public double? TintColor { get; set; }

    // ── SVG control-point current values ─────────────────────────────────────

    /// <summary>
    /// Current user-set numeric value per control-point id.
    /// Keys match <see cref="SvgControlPoint.PointId"/>.
    /// For color-type points use <see cref="ControlPointColors"/>.
    /// </summary>
    public Dictionary<string, double> ControlPointValues { get; set; } = [];

    /// <summary>
    /// Current user-set hex color per control-point id.
    /// Keys match <see cref="SvgControlPoint.PointId"/> for color-type points.
    /// </summary>
    public Dictionary<string, string> ControlPointColors { get; set; } = [];
}
