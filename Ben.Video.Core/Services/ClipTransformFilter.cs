using System.Globalization;
using Ben.Video.Editor.Models;

namespace Ben.Video.Editor.Services;

/// <summary>
/// Turns a clip's placement into ffmpeg's terms.
/// </summary>
/// <remarks>
/// <para>Two halves. <see cref="BuildSourceChain"/> prepares the picture — cut the crop off it,
/// turn it, scale it to the box it is going in. <see cref="Offset"/> says where that box is. The
/// caller overlays one on the other, which is how a corner inset, a side-by-side pair, an upright
/// phone clip and a DVR export with its bars removed all come out of the same three steps.</para>
///
/// <para>Pure, and tested on the numbers. A layout that is right in the preview and wrong in the
/// render is the failure worth guarding against, and both read this.</para>
/// </remarks>
public static class ClipTransformFilter
{
    private static string F(double v) => v.ToString("F6", CultureInfo.InvariantCulture);

    /// <summary>
    /// The filter chain that prepares one clip's picture, or null when nothing needs doing.
    /// </summary>
    /// <param name="transform">The clip's placement, or null for "fill the frame".</param>
    /// <param name="frameWidth">Output frame width in pixels.</param>
    /// <param name="frameHeight">Output frame height in pixels.</param>
    public static string? BuildSourceChain(ClipTransform? transform, int frameWidth, int frameHeight)
    {
        if (transform is null || transform.IsIdentity) return null;
        if (frameWidth <= 0 || frameHeight <= 0) return null;

        var parts = new List<string>();

        // Crop first: everything after it works on the picture that is actually being kept, so the
        // scale below fills the box with the wanted part rather than with the part being cut off.
        var keepW = 1.0 - Math.Clamp(transform.CropLeft, 0, 1) - Math.Clamp(transform.CropRight, 0, 1);
        var keepH = 1.0 - Math.Clamp(transform.CropTop, 0, 1) - Math.Clamp(transform.CropBottom, 0, 1);

        if (keepW < 1.0 || keepH < 1.0)
        {
            // A crop that leaves nothing is a mistake, not an instruction. Clamping to something
            // visible beats handing ffmpeg a zero width and losing the whole render.
            keepW = Math.Max(0.02, keepW);
            keepH = Math.Max(0.02, keepH);

            parts.Add($"crop=iw*{F(keepW)}:ih*{F(keepH)}"
                    + $":iw*{F(Math.Clamp(transform.CropLeft, 0, 0.98))}"
                    + $":ih*{F(Math.Clamp(transform.CropTop, 0, 0.98))}");
        }

        if (Math.Abs(transform.Rotation) > 0.001)
        {
            var radians = (transform.Rotation * Math.PI / 180.0).ToString("F6", CultureInfo.InvariantCulture);
            // The output grows to hold the turned picture rather than clipping its corners off,
            // and the new corners are transparent so whatever is underneath shows through.
            parts.Add($"rotate={radians}:ow=rotw({radians}):oh=roth({radians}):c=black@0.0");
        }

        var boxW = Even((int)Math.Round(Math.Clamp(transform.Width, 0.01, 1.0) * frameWidth));
        var boxH = Even((int)Math.Round(Math.Clamp(transform.Height, 0.01, 1.0) * frameHeight));

        // decrease + pad rather than a plain scale: the picture keeps its proportions inside the
        // box it was given, which is what somebody dragging a corner inset expects. A plain scale
        // would stretch a 16:9 camera into whatever shape the box happened to be.
        parts.Add($"scale={boxW}:{boxH}:force_original_aspect_ratio=decrease");
        parts.Add($"pad={boxW}:{boxH}:(ow-iw)/2:(oh-ih)/2:color=black@0.0");
        parts.Add("format=rgba");

        if (transform.Opacity < 1.0)
        {
            var alpha = Math.Clamp(transform.Opacity, 0.0, 1.0).ToString("F4", CultureInfo.InvariantCulture);
            parts.Add($"colorchannelmixer=aa={alpha}");
        }

        return string.Join(",", parts);
    }

    /// <summary>Where the prepared picture is laid down, in whole pixels.</summary>
    public static (int X, int Y) Offset(ClipTransform? transform, int frameWidth, int frameHeight)
    {
        if (transform is null) return (0, 0);

        return (Even((int)Math.Round(Math.Clamp(transform.X, 0.0, 1.0) * frameWidth)),
                Even((int)Math.Round(Math.Clamp(transform.Y, 0.0, 1.0) * frameHeight)));
    }

    /// <summary>
    /// Whether this clip needs the transform pass at all.
    /// </summary>
    public static bool NeedsWork(ClipTransform? transform) =>
        transform is not null && !transform.IsIdentity;

    private static int Even(int value) => Math.Max(0, value - (value % 2));
}
