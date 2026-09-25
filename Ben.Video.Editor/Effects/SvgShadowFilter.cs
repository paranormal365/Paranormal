using System.Globalization;

namespace Ben.Video.Editor.Effects;

/// <summary>
/// Builds the shared inline-SVG drop-shadow filter definition used by both
/// <see cref="Ben.Video.Editor.Models.CalloutShapeRenderer"/> and
/// <see cref="Ben.Video.Editor.Models.TextOverlayRenderer"/>, so the two renderers
/// never carry two independently-drifting copies of the same <c>feDropShadow</c> fragment.
/// </summary>
public static class SvgShadowFilter
{
    /// <summary>
    /// Returns an SVG <c>&lt;defs&gt;</c> block defining a <c>feDropShadow</c> filter with
    /// id <c>bv-shadow</c>, or an empty string when <paramref name="blur"/> is not positive
    /// (matching every caller's "no shadow" convention).
    /// </summary>
    /// <param name="shadowColor">Packed ARGB double (<see cref="ColorHelper"/>).</param>
    /// <param name="offsetX">Shadow X offset in pixels.</param>
    /// <param name="offsetY">Shadow Y offset in pixels.</param>
    /// <param name="blur">Shadow blur radius in pixels.</param>
    /// <param name="canvasW">Output canvas width in pixels.</param>
    /// <param name="canvasH">Output canvas height in pixels.</param>
    /// <remarks>
    /// The region is given in user space — over the whole canvas — and not as a percentage of each
    /// filtered shape's bounding box, which is what it used to be. A perfectly horizontal arrow or
    /// line has a bounding box of zero height, so a region of "140% of that box" was 140% of
    /// nothing: the shaft was not drawn at all, on screen or in the export, leaving a floating
    /// arrowhead. It took a drop shadow to trigger it and a flat shape to show it, which is why it
    /// survived — a tilted arrow, and every rectangle and ellipse, were always fine
    /// (2026-09-18 audit).
    /// </remarks>
    public static string Build(double shadowColor, double offsetX, double offsetY, double blur,
                               int canvasW, int canvasH)
    {
        if (blur <= 0) return string.Empty;

        var sc    = ColorHelper.ToRgbaCss(shadowColor);
        var alpha = ColorHelper.Unpack(shadowColor).A / 255.0;
        string F(double v) => v.ToString("F3", CultureInfo.InvariantCulture);

        // Room for the shadow to fall outside the canvas before it is cropped: the offset it is
        // pushed by, plus the reach of the blur (three standard deviations covers it).
        var margin = Math.Abs(offsetX) + Math.Abs(offsetY) + blur * 1.5 + 8;

        return $"""
            <defs>
              <filter id="bv-shadow" filterUnits="userSpaceOnUse"
                      x="{F(-margin)}" y="{F(-margin)}"
                      width="{F(canvasW + 2 * margin)}" height="{F(canvasH + 2 * margin)}">
                <feDropShadow dx="{F(offsetX)}" dy="{F(offsetY)}" stdDeviation="{F(blur / 2)}"
                              flood-color="{sc}" flood-opacity="{F(alpha)}" />
              </filter>
            </defs>
            """;
    }
}
