using Ben.Video.Editor.Effects;

namespace Ben.Video.Editor.Models;

/// <summary>
/// Horizontal alignment of a text overlay.
/// </summary>
public enum TextHorizontalAlign { Left, Center, Right }

/// <summary>
/// Vertical alignment of a text overlay.
/// </summary>
public enum TextVerticalAlign { Top, Middle, Bottom }

/// <summary>
/// A timed text or title overlay rendered on top of the video output.
/// Requires the TextOverlays feature flag.
/// Rendered via <see cref="TextOverlayRenderer"/>'s per-frame SVG rasterization pipeline (see
/// <see cref="Services.ExportService"/>) — not ffmpeg's native <c>drawtext</c> filter, which has no
/// working cross-OS font-loading mechanism in this app's ffmpeg.wasm environment.
/// </summary>
public sealed record TextOverlay : TrackItem
{
    /// <summary>The text content to display.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>Font family name. Must be available in the ffmpeg-core.wasm build (freetype2/libass).</summary>
    public string FontFamily { get; set; } = "Arial";

    /// <summary>Font size in points.</summary>
    public int FontSize { get; set; } = 48;

    /// <summary>Font color as a hex string, e.g. "#FFFFFF".</summary>
    public string FontColor { get; set; } = "#FFFFFF";

    /// <summary>Bold weight for the whole text block (item #16). Per-character/inline mixed
    /// formatting is a separate, larger future slice (needs a runs/spans data model instead of one
    /// style per overlay).</summary>
    public bool FontBold { get; set; }

    /// <summary>Underline for the whole text block (item #16). Same scope note as <see cref="FontBold"/>.</summary>
    public bool FontUnderline { get; set; }

    /// <summary>
    /// Inline-styled runs (item #16 — subscript/superscript + inline mixed formatting), the
    /// rendering source of truth when set. <c>null</c>/empty means "use <see cref="Text"/>/
    /// <see cref="FontBold"/>/<see cref="FontUnderline"/> exactly as before this field existed" —
    /// every project saved before this phase has no <see cref="Runs"/> and keeps rendering
    /// identically. <see cref="Text"/> is still kept in sync as the flat concatenation of all
    /// runs' text whenever <see cref="Runs"/> is set, so existing code that reads <see cref="Text"/>
    /// (e.g. <see cref="TrackItem.Name"/> truncation) keeps working unchanged.
    /// </summary>
    public List<TextRun>? Runs { get; set; }

    /// <summary>Optional background box color behind the text, e.g. "#000000@0.5". Null = no box.</summary>
    public string? BoxColor { get; set; }

    /// <summary>Horizontal alignment of the text block.</summary>
    public TextHorizontalAlign HorizontalAlign { get; set; } = TextHorizontalAlign.Center;

    /// <summary>Vertical alignment of the text block.</summary>
    public TextVerticalAlign VerticalAlign { get; set; } = TextVerticalAlign.Bottom;

    /// <summary>Horizontal offset from the aligned edge in pixels.</summary>
    public int OffsetX { get; set; }

    /// <summary>Vertical offset from the aligned edge in pixels.</summary>
    public int OffsetY { get; set; } = 40;

    /// <summary>
    /// Optional on-canvas position override (canvas fraction 0..1), set by dragging the text directly
    /// on the preview. When set, this takes priority over <see cref="HorizontalAlign"/>/
    /// <see cref="OffsetX"/> for the X position — null (the default) leaves existing alignment-based
    /// behavior completely unchanged.
    /// </summary>
    public double? OverrideX { get; set; }

    /// <summary>Same as <see cref="OverrideX"/>, for the Y position — overrides
    /// <see cref="VerticalAlign"/>/<see cref="OffsetY"/> when set.</summary>
    public double? OverrideY { get; set; }

    /// <summary>Fade-in duration in seconds (0 = instant appear).</summary>
    public double FadeInSeconds { get; set; } = 0.3;

    /// <summary>Fade-out duration in seconds (0 = instant disappear).</summary>
    public double FadeOutSeconds { get; set; } = 0.3;

    /// <summary>
    /// The widest the title may draw, as a fraction of the canvas. Null means no limit.
    /// </summary>
    /// <remarks>
    /// <para>Titles never wrapped. A sentence of any length drew as one line and ran straight off
    /// both sides of the frame, with the only remedy being to type the line breaks yourself
    /// (2026-09-05 audit, titles-6). Callouts have wrapped for a while; the wrapping code is
    /// shared, and only titles had no way to ask for it.</para>
    ///
    /// <para>A fraction rather than pixels so it survives a change of canvas size, which is the
    /// same reason positions are fractions. Null rather than 1.0 as the default so nothing about
    /// an existing project's titles changes until somebody sets a width.</para>
    /// </remarks>
    public double? MaxWidth { get; set; }

    /// <summary>Opacity/alpha (0.0 = transparent, 1.0 = fully opaque). Multiplies with the fade-in/out envelope.</summary>
    public double Opacity { get; set; } = 1.0;

    // ── Shadow ───────────────────────────────────────────────────────────────

    /// <summary>Shadow colour as a packed ARGB double (<see cref="Effects.ColorHelper"/>).</summary>
    public double ShadowColor { get; set; } = ColorHelper.Pack(0, 0, 0, 120);

    /// <summary>Shadow offset in pixels along X.</summary>
    public double ShadowOffsetX { get; set; } = 3.0;

    /// <summary>Shadow offset in pixels along Y.</summary>
    public double ShadowOffsetY { get; set; } = 3.0;

    /// <summary>
    /// Shadow blur radius in pixels. Note: ffmpeg's native <c>drawtext</c> shadow has no
    /// blur capability — this only takes visual effect once the overlay is animated and
    /// rendered through the per-frame SVG pipeline (backlog item #19, phase 71). Until
    /// then it acts only as a presence gate for the static (unblurred) native shadow,
    /// matching <c>CalloutClip</c>'s own native-path shadow limitation.
    /// </summary>
    public double ShadowBlur { get; set; } = 4.0;

    /// <summary>
    /// Computes the fade-in/fade-out opacity envelope at a given moment within this overlay's own
    /// lifetime (0 = <see cref="TrackItem.TimelinePosition"/>). Pure function, used by the per-frame SVG
    /// export pipeline (<see cref="Services.ExportService"/>) to render a per-frame <see cref="Opacity"/>
    /// for static (non-animated) overlays — the SVG-pipeline equivalent of what the old, now-removed
    /// ffmpeg-native <c>drawtext</c> <c>alpha=</c> expression computed at render time.
    /// </summary>
    /// <param name="elapsedSeconds">Seconds since this overlay's own <see cref="TrackItem.TimelinePosition"/>.</param>
    public double ComputeFadeAlpha(double elapsedSeconds)
        => FadeEnvelope.Compute(elapsedSeconds, Duration, FadeInSeconds, FadeOutSeconds);
}
