using Ben.Video.Editor.Effects;

namespace Ben.Video.Editor.Models;

/// <summary>The geometric shape rendered by a <see cref="CalloutClip"/>.</summary>
public enum ShapeType
{
    /// <summary>Filled/stroked rectangle.</summary>
    Rectangle,

    /// <summary>Filled/stroked ellipse (circle when width == height).</summary>
    Ellipse,

    /// <summary>Horizontal or diagonal arrow.</summary>
    Arrow,

    /// <summary>Straight line.</summary>
    Line,

    /// <summary>5-pointed star.</summary>
    Star,

    /// <summary>Custom SVG or AVIF asset loaded from OPFS <c>bv-callouts/</c>.</summary>
    Custom,
}

/// <summary>
/// An annotation / callout layer placed on a video track.
/// Rendered as an ffmpeg video filter overlay on the composited output.
///
/// <para><b>Coordinates:</b> <see cref="X"/> and <see cref="Y"/> are fractions of
/// the output canvas (0.0 = left/top, 1.0 = right/bottom). <see cref="Width"/> and
/// <see cref="Height"/> are also fractions of the canvas.</para>
///
/// <para><b>Colours:</b> stored as packed ARGB doubles via <see cref="ColorHelper"/>.</para>
/// </summary>
public sealed record CalloutClip : TrackItem
{
    // ── Shape ──────────────────────────────────────────────────────────────────

    /// <summary>The shape to render.</summary>
    public ShapeType Shape { get; set; } = ShapeType.Rectangle;

    // ── Geometry (all as canvas fractions 0–1) ────────────────────────────────

    /// <summary>Horizontal position of the shape's top-left corner (0 = left edge).</summary>
    public double X { get; set; } = 0.1;

    /// <summary>Vertical position of the shape's top-left corner (0 = top edge).</summary>
    public double Y { get; set; } = 0.1;

    /// <summary>Width as a fraction of the canvas (0.1 = 10 % of frame width).</summary>
    public double Width { get; set; } = 0.2;

    /// <summary>Height as a fraction of the canvas.</summary>
    public double Height { get; set; } = 0.15;

    /// <summary>Rotation in degrees (clockwise, 0 = no rotation).</summary>
    public double Rotation { get; set; }

    // ── Appearance ────────────────────────────────────────────────────────────

    /// <summary>Fill colour as packed ARGB double (<see cref="ColorHelper"/>). Default: semi-transparent yellow.</summary>
    public double FillColor { get; set; } = ColorHelper.Pack(255, 255, 0, 180);

    /// <summary>Stroke colour. Default: opaque black.</summary>
    public double StrokeColor { get; set; } = ColorHelper.OpaqueBlack;

    /// <summary>Stroke width in pixels (0 = no stroke).</summary>
    public double StrokeWidth { get; set; } = 2.0;

    /// <summary>Opacity multiplier applied to the whole shape (0.0–1.0).</summary>
    public double Opacity { get; set; } = 1.0;

    // ── Fade ──────────────────────────────────────────────────────────────────

    /// <summary>Fade-in duration in seconds (0 = instant appear, the default — matching
    /// pre-existing callouts).</summary>
    public double FadeInSeconds { get; set; }

    /// <summary>Fade-out duration in seconds (0 = instant disappear).</summary>
    public double FadeOutSeconds { get; set; }

    /// <summary>The fade opacity envelope at <paramref name="elapsedSeconds"/> into this callout's
    /// own lifetime — see <see cref="FadeEnvelope.Compute"/>.</summary>
    public double ComputeFadeAlpha(double elapsedSeconds)
        => FadeEnvelope.Compute(elapsedSeconds, Duration, FadeInSeconds, FadeOutSeconds);

    // ── Shadow ────────────────────────────────────────────────────────────────

    /// <summary>Shadow colour. Default: semi-transparent black.</summary>
    public double ShadowColor { get; set; } = ColorHelper.Pack(0, 0, 0, 120);

    /// <summary>Shadow horizontal offset in pixels (positive = right).</summary>
    public double ShadowOffsetX { get; set; } = 3.0;

    /// <summary>Shadow vertical offset in pixels (positive = down).</summary>
    public double ShadowOffsetY { get; set; } = 3.0;

    /// <summary>Shadow blur radius in pixels (0 = hard shadow).</summary>
    public double ShadowBlur { get; set; } = 4.0;

    // ── Text (optional label rendered centered on the shape) ──────────────────

    /// <summary>Optional text label rendered centered on the shape's bounding box. Null/empty = no text
    /// (shape only, the default). Supports multiple lines via <c>\n</c>.</summary>
    public string? Text { get; set; }

    /// <summary>Font family name for <see cref="Text"/>. Resolved against the browser's own installed
    /// fonts at render time (see <see cref="CalloutShapeRenderer"/>) — not an ffmpeg-native font file.</summary>
    public string FontFamily { get; set; } = "Arial";

    /// <summary>Font size in pixels for <see cref="Text"/>.</summary>
    public int FontSize { get; set; } = 28;

    /// <summary>Text colour as packed ARGB double (<see cref="ColorHelper"/>). Default: opaque black
    /// (reads better than white against the default semi-transparent-yellow fill).</summary>
    public double FontColor { get; set; } = ColorHelper.OpaqueBlack;

    /// <summary>Bold weight for <see cref="Text"/> (item #16). Applies to the whole text block —
    /// per-character/inline mixed formatting is a separate, larger future slice (needs a runs/spans
    /// data model instead of one style per overlay).</summary>
    public bool FontBold { get; set; }

    /// <summary>Underline for <see cref="Text"/> (item #16). Whole-block, same scope note as <see cref="FontBold"/>.</summary>
    public bool FontUnderline { get; set; }

    /// <summary>
    /// Inline-styled runs for <see cref="Text"/> (item #16 — subscript/superscript + inline mixed
    /// formatting), the rendering source of truth when set. <c>null</c>/empty means "use
    /// <see cref="Text"/>/<see cref="FontBold"/>/<see cref="FontUnderline"/> exactly as before this
    /// field existed" — see <see cref="TextRun"/>'s own doc comment for the full backward-compat
    /// contract (identical to <see cref="TextOverlay.Runs"/>).
    /// </summary>
    public List<TextRun>? Runs { get; set; }

    // ── Text layout inside the shape (item #31) ───────────────────────────────
    // Before this, text was hard-anchored dead-centre. These reach parity with TextOverlay's own
    // alignment model by reusing its enums outright, so the two editors describe layout the same
    // way. Every default reproduces the previous centred behaviour exactly, so existing saved
    // projects are unaffected.

    /// <summary>Horizontal placement of <see cref="Text"/> within the shape's bounding box.</summary>
    public TextHorizontalAlign TextAlign { get; set; } = TextHorizontalAlign.Center;

    /// <summary>Vertical placement of <see cref="Text"/> within the shape's bounding box.</summary>
    public TextVerticalAlign TextVerticalAlign { get; set; } = TextVerticalAlign.Middle;

    /// <summary>
    /// Word-wrap <see cref="Text"/> to the shape's width instead of only breaking on explicit
    /// <c>\n</c>. Off by default — turning it on changes the line count of existing text, so it
    /// must be opt-in rather than a silent reflow of saved projects.
    ///
    /// <para>Applies to both the plain-text and the <see cref="Runs"/> path. The rich-text editor
    /// always produces runs, so a plain-text-only implementation would leave this toggle inert in
    /// the real UI. See <see cref="CalloutTextWrapper"/> for the measurement caveat.</para>
    /// </summary>
    public bool TextWrap { get; set; }

    /// <summary>
    /// Apply the callout's existing drop shadow (<see cref="ShadowColor"/> and friends) to the text
    /// as well as the shape. Off by default: the shape's shadow was the only consumer before, and
    /// enabling it for text unconditionally would visibly change every existing callout that has a
    /// label and a blur.
    /// </summary>
    public bool TextShadow { get; set; }

    /// <summary>Inset in pixels between the shape's bounding box and left/right/top/bottom-aligned
    /// text, so aligned text doesn't sit flush against (or under) the stroke.</summary>
    public double TextPadding { get; set; } = 8.0;

    // ── Custom asset (ShapeType.Custom) ───────────────────────────────────────

    /// <summary>Filename of the custom SVG/AVIF asset in OPFS <c>bv-callouts/</c>.
    /// Null when <see cref="Shape"/> is not <see cref="ShapeType.Custom"/>.</summary>
    public string? OpfsAssetName { get; set; }

    // ── Media missing (used after project re-open when asset not in OPFS) ─────

    public bool AssetMissing { get; set; }

    // ── SVG control points (Arrow/Line curve, Star radius/points, Rect radius) ─

    /// <summary>
    /// Per-control-point values. Keys are the constants defined in
    /// <see cref="CalloutControlPoints"/>. For shapes that use SVG rendering
    /// (Arrow, Line, Star) these drive the Bezier handles, radii, etc.
    /// Empty = use defaults from <see cref="CalloutShapeRenderer.SetDefaults"/>.
    /// </summary>
    public Dictionary<string, double> ControlPointValues { get; set; } = [];
}
