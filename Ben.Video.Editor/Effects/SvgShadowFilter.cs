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
    public static string Build(double shadowColor, double offsetX, double offsetY, double blur)
    {
        if (blur <= 0) return string.Empty;

        var sc    = ColorHelper.ToRgbaCss(shadowColor);
        var alpha = ColorHelper.Unpack(shadowColor).A / 255.0;
        string F(double v) => v.ToString("F3", CultureInfo.InvariantCulture);

        return $"""
            <defs>
              <filter id="bv-shadow" x="-20%" y="-20%" width="140%" height="140%">
                <feDropShadow dx="{F(offsetX)}" dy="{F(offsetY)}" stdDeviation="{F(blur / 2)}"
                              flood-color="{sc}" flood-opacity="{F(alpha)}" />
              </filter>
            </defs>
            """;
    }
}
