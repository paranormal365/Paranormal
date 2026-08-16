namespace Ben.Video.Editor.Models.Assets;

/// <summary>
/// Server-defined capability flags for a single asset.
/// Controls which editor panels and interactions are visible when a user works with this asset.
/// All flags default to <c>false</c> — the Ben app must explicitly grant each capability.
/// </summary>
public sealed record VideoAssetSettings
{
    // ── Appearance ────────────────────────────────────────────────────────────

    /// <summary>
    /// User may change fill or stroke colors.
    /// When true and <see cref="PresetColors"/> is non-empty, the editor shows
    /// a curated swatch set rather than a free color picker.
    /// </summary>
    public bool AllowRecolor { get; init; }

    /// <summary>
    /// User may resize the asset on the canvas.
    /// Constrained by <see cref="MinScale"/> and <see cref="MaxScale"/> when set.
    /// </summary>
    public bool AllowResize { get; init; }

    /// <summary>User may change the overall opacity of the asset.</summary>
    public bool AllowOpacity { get; init; }

    /// <summary>User may rotate the asset freely.</summary>
    public bool AllowRotation { get; init; }

    // ── Animation ─────────────────────────────────────────────────────────────

    /// <summary>
    /// User may apply visual effects (e.g. blur, shadow, glow) to the asset.
    /// </summary>
    public bool AllowEffects { get; init; }

    /// <summary>
    /// User may assign easing curves to animation keyframes on this asset.
    /// When false, all keyframe interpolation is forced to linear.
    /// </summary>
    public bool AllowEasing { get; init; }

    /// <summary>
    /// User may add motion keyframes to drive position, scale, or rotation over time.
    /// Pairs with <see cref="AllowControlPoints"/> for SVG assets.
    /// </summary>
    public bool AllowMotion { get; init; }

    /// <summary>
    /// User may interact with SVG control points (move, scale, recolor individual elements).
    /// Only meaningful when the asset format is <see cref="VideoAssetFormat.Svg"/>
    /// and the asset has at least one <see cref="SvgControlPoint"/> defined.
    /// </summary>
    public bool AllowControlPoints { get; init; }

    // ── Color constraints ─────────────────────────────────────────────────────

    /// <summary>
    /// Optional curated palette of hex colors the user may choose from
    /// when <see cref="AllowRecolor"/> is true.
    /// Null or empty = unrestricted (full color picker available).
    /// </summary>
    public IReadOnlyList<string>? PresetColors { get; init; }

    // ── Size constraints ──────────────────────────────────────────────────────

    /// <summary>Minimum scale factor. Null = no lower bound.</summary>
    public double? MinScale { get; init; }

    /// <summary>Maximum scale factor. Null = no upper bound.</summary>
    public double? MaxScale { get; init; }

    // ── Export behavior ───────────────────────────────────────────────────────

    /// <summary>
    /// When true, this asset must be flattened into the video during export
    /// (cannot be left as a soft/subtitle track). Used for callouts and shapes
    /// where vector export is not meaningful.
    /// </summary>
    public bool FlattenOnExport { get; init; } = true;
}
