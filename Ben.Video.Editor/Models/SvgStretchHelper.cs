using System.Text.RegularExpressions;

namespace Ben.Video.Editor.Models;

/// <summary>
/// Forces an arbitrary SVG document's root element to fill its container, regardless of
/// whatever width/height/viewBox the source file itself declares. Used by
/// <see cref="Ben.Video.Editor.Components.LiveOverlayPreview"/> to inline a ClipArt SVG asset
/// (item #47) so it fills the clip's positioned X/Y/Width/Height box the same way a raster
/// asset's &lt;img&gt; does — <c>ClipArtClip.Width</c>/<c>Height</c> are independent fractions,
/// not necessarily aspect-preserving, matching the export overlay filter's own non-uniform
/// ffmpeg <c>scale</c> behavior.
/// </summary>
public static class SvgStretchHelper
{
    private static readonly Regex RootSvgTag = new(@"<svg\b([^>]*)>", RegexOptions.IgnoreCase);
    private static readonly Regex WidthOrHeightAttr = new(@"\s(width|height)=""[^""]*""", RegexOptions.IgnoreCase);

    /// <summary>Returns null unchanged if the text has no recognizable root &lt;svg&gt; tag.</summary>
    public static string ForceFillDimensions(string svgText) =>
        RootSvgTag.Replace(svgText, m =>
        {
            var attrs = WidthOrHeightAttr.Replace(m.Groups[1].Value, "");
            return $"<svg{attrs} width=\"100%\" height=\"100%\" preserveAspectRatio=\"none\">";
        }, 1);
}
