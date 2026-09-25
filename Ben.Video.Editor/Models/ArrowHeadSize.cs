namespace Ben.Video.Editor.Models;

/// <summary>
/// How big the head on a Callout arrow should be.
/// </summary>
/// <remarks>
/// The head used to come from the stroke width alone — <c>max(strokeWidth × 4, 12)</c> — so it was
/// the same 12px triangle whether the arrow spanned 60 pixels or 900. On a 1280×720 exhibit a
/// freshly-added arrow (2px stroke, 20% of the canvas wide) pointed at the evidence with a head
/// you had to look for. Making it grow with the arrow's own length fixes the proportion without
/// shrinking any arrow that already reads: the stroke-derived size is the floor, so an arrow drawn
/// with a heavy stroke keeps exactly the head it has today (2026-09-18 audit).
/// </remarks>
public static class ArrowHeadSize
{
    /// <summary>The old, stroke-only rule — the floor this never goes below.</summary>
    public static double FromStroke(double strokeWidth) => Math.Max(strokeWidth * 4, 12.0);

    /// <summary>Roughly an eighth of the arrow, which is what a drawn arrow looks like.</summary>
    private const double ShareOfLength = 0.12;

    /// <summary>Past this the head stops being a head and the arrow stops being an arrow.</summary>
    private const double MostOfLength = 0.5;

    public static double For(double strokeWidth, double arrowLength)
    {
        var fromStroke = FromStroke(strokeWidth);
        if (arrowLength <= 0) return fromStroke;

        var wanted = Math.Max(fromStroke, arrowLength * ShareOfLength);
        // A stroke heavy enough to demand a head longer than the arrow is the user's own doing and
        // is left alone; the cap only bites when it is the length rule asking for too much.
        return Math.Max(fromStroke, Math.Min(wanted, arrowLength * MostOfLength));
    }
}
