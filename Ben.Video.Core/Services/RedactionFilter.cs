using System.Globalization;
using Ben.Video.Editor.Models;

namespace Ben.Video.Editor.Services;

/// <summary>
/// Builds the ffmpeg graph that obscures part of a picture.
/// </summary>
/// <remarks>
/// <para>Split the frame, crop out each region, blur or pixelate the crop, and lay it back over
/// the original at the same place. Only core filters, so it runs in the browser engine and in the
/// native sidecar alike.</para>
///
/// <para>Pure, and tested against the coordinates rather than the pixels, because the failure that
/// matters here is a box in the wrong place: a redaction that misses is worse than no redaction,
/// since the person who drew it believes the face is covered.</para>
/// </remarks>
public static class RedactionFilter
{
    /// <summary>Regions smaller than this many pixels are dropped rather than fed to ffmpeg.</summary>
    /// <remarks>
    /// crop refuses a zero or negative size and takes the whole export down with it. A box this
    /// small hides nothing anyway, so dropping it costs nothing and losing the render costs a lot.
    /// </remarks>
    public const int MinimumRegionPixels = 2;

    /// <summary>
    /// The filter_complex for <paramref name="regions"/>, or null when there is nothing to do.
    /// </summary>
    /// <param name="regions">The regions to obscure, in frame fractions.</param>
    /// <param name="frameWidth">Output frame width in pixels.</param>
    /// <param name="frameHeight">Output frame height in pixels.</param>
    /// <returns>A graph producing <c>[vout]</c>, or null.</returns>
    public static string? Build(
        IReadOnlyList<RedactionRegion> regions, int frameWidth, int frameHeight)
    {
        ArgumentNullException.ThrowIfNull(regions);
        if (frameWidth <= 0 || frameHeight <= 0) return null;

        var boxes = regions.Select(r => ToPixels(r, frameWidth, frameHeight))
                           .Where(b => b is not null)
                           .Select(b => b!.Value)
                           .ToList();

        if (boxes.Count == 0) return null;

        var sb = new System.Text.StringBuilder();

        // One copy of the frame per region, plus the one everything is laid back onto.
        sb.Append("[0:v]split=").Append(boxes.Count + 1).Append("[bv]");
        for (var i = 0; i < boxes.Count; i++) sb.Append("[r").Append(i).Append(']');
        sb.Append(';');

        for (var i = 0; i < boxes.Count; i++)
        {
            var (x, y, w, h, region) = boxes[i];
            sb.Append($"[r{i}]crop={w}:{h}:{x}:{y},");
            sb.Append(Obscure(region, w, h));
            sb.Append($"[b{i}];");
        }

        var current = "bv";
        for (var i = 0; i < boxes.Count; i++)
        {
            var (x, y, _, _, _) = boxes[i];
            var next = i == boxes.Count - 1 ? "vout" : $"o{i}";
            sb.Append($"[{current}][b{i}]overlay={x}:{y}[{next}];");
            current = next;
        }

        return sb.ToString().TrimEnd(';');
    }

    /// <summary>The filter that actually hides the cropped-out piece.</summary>
    /// <remarks>
    /// Both are driven off one 1–10 strength, because the two need very different numbers to look
    /// equally obscured and nobody redacting a face should have to know that. Blur scales sigma
    /// with the size of the region, so a small face is hidden as thoroughly as a large one.
    /// </remarks>
    private static string Obscure(RedactionRegion region, int w, int h)
    {
        var ic       = CultureInfo.InvariantCulture;
        var strength = Math.Clamp(region.Strength, 1.0, 10.0);

        if (region.Style is RedactionStyle.Pixelate)
        {
            // Down to blocks and back up with no interpolation either way, which is what makes
            // the blocks hard-edged instead of smeared.
            var blocks = Math.Max(2, (int)Math.Round(22 - strength * 2));
            var dw     = Math.Max(1, w / blocks);
            var dh     = Math.Max(1, h / blocks);

            return $"scale={dw}:{dh}:flags=neighbor,scale={w}:{h}:flags=neighbor";
        }

        var smallestSide = Math.Max(1, Math.Min(w, h));
        var sigma        = Math.Max(2.0, smallestSide * strength / 40.0);

        return "gblur=sigma=" + sigma.ToString("F1", ic);
    }

    /// <summary>
    /// One region in whole pixels, clamped inside the frame, or null when nothing is left of it.
    /// </summary>
    /// <remarks>
    /// Even sizes and offsets throughout: chroma-subsampled output cannot crop on an odd boundary,
    /// and ffmpeg either refuses or shifts the crop by a pixel — which on a redaction means the
    /// edge of what was being hidden stays visible.
    /// </remarks>
    private static (int X, int Y, int W, int H, RedactionRegion Region)? ToPixels(
        RedactionRegion region, int frameWidth, int frameHeight)
    {
        var left   = Math.Clamp(region.X, 0.0, 1.0) * frameWidth;
        var top    = Math.Clamp(region.Y, 0.0, 1.0) * frameHeight;
        var right  = Math.Clamp(region.X + region.Width,  0.0, 1.0) * frameWidth;
        var bottom = Math.Clamp(region.Y + region.Height, 0.0, 1.0) * frameHeight;

        var x = Even((int)Math.Floor(left));
        var y = Even((int)Math.Floor(top));
        var w = Even((int)Math.Ceiling(right)  - x);
        var h = Even((int)Math.Ceiling(bottom) - y);

        // Rounding outward can push the box past the frame; pull it back rather than letting
        // ffmpeg refuse a crop that starts inside and ends outside.
        if (x + w > frameWidth)  w = Even(frameWidth  - x);
        if (y + h > frameHeight) h = Even(frameHeight - y);

        return w < MinimumRegionPixels || h < MinimumRegionPixels
            ? null
            : (x, y, w, h, region);
    }

    private static int Even(int value) => value - (value % 2);
}
