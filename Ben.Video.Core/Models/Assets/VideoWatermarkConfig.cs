namespace Ben.Video.Editor.Models.Assets;

/// <summary>
/// Server-controlled watermark configuration returned by
/// <c>GET /api/video-assets/watermark-config</c>.
///
/// <para>When <see cref="Enabled"/> is true, Ben.Video.Editor will automatically
/// composite the watermark file at <see cref="FileUrl"/> into every export,
/// regardless of user preferences. The user has no control over this — it is
/// enforced by the export pipeline.</para>
/// </summary>
public sealed record VideoWatermarkConfig
{
    /// <summary>
    /// When true, every export will include the watermark.
    /// When false, no watermark is applied even if <see cref="FileUrl"/> is set.
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// Absolute or relative URL to the watermark file (PNG, WebP, or SVG).
    /// Null when no watermark is configured or <see cref="Enabled"/> is false.
    /// The file is cached locally in OPFS under <c>bv-watermark.{ext}</c>.
    /// </summary>
    public string? FileUrl { get; init; }

    /// <summary>
    /// Content hash of the watermark file — used to detect when the cached
    /// local copy is stale and a fresh download is needed.
    /// </summary>
    public string? Version { get; init; }

    /// <summary>
    /// Opacity of the composited watermark (0.0 = invisible, 1.0 = opaque).
    /// Default: 0.5
    /// </summary>
    public double Opacity { get; init; } = 0.5;

    /// <summary>
    /// Where in the video frame the watermark is placed.
    /// Default: <see cref="WatermarkPosition.BottomRight"/>
    /// </summary>
    public WatermarkPosition Position { get; init; } = WatermarkPosition.BottomRight;

    /// <summary>
    /// Watermark width expressed as a fraction of the video frame width (0.0–1.0).
    /// The height is scaled proportionally to preserve aspect ratio.
    /// Default: 0.15 (15% of frame width)
    /// </summary>
    public double ScaleFraction { get; init; } = 0.15;

    /// <summary>Horizontal margin from the nearest edge in pixels. Default: 20</summary>
    public int MarginX { get; init; } = 20;

    /// <summary>Vertical margin from the nearest edge in pixels. Default: 20</summary>
    public int MarginY { get; init; } = 20;
}
