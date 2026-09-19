using Ben.Video.Editor.Models;

namespace Ben.Video.Editor.Services;

/// <summary>
/// How tall a piece of clip art is when its height was never set.
/// </summary>
/// <remarks>
/// <para>One answer, in one place, because there used to be three. The selection box and the live
/// preview read a missing height as equal to the width — a fraction of the frame, so on a 16:9
/// canvas that is a wide rectangle. The export read it as equal to the width in pixels, which is a
/// square. So the same piece of artwork was drawn at one shape, selected at another and rendered
/// at a third (2026-09-05 audit, callouts-10).</para>
///
/// <para>Pure, and takes the canvas, because "the same shape" is a statement about pixels and
/// cannot be answered without knowing how wide and tall the frame is.</para>
/// </remarks>
public static class ClipArtGeometry
{
    /// <summary>
    /// The clip's height as a fraction of the canvas height.
    /// </summary>
    /// <remarks>
    /// An explicit height wins. Otherwise the artwork's own proportions decide, and where those
    /// are unknown the answer is a square on screen — which is what "preserve aspect ratio" means
    /// in the absence of any ratio to preserve.
    /// </remarks>
    public static double HeightFraction(ClipArtClip clip, int canvasWidth, int canvasHeight)
    {
        ArgumentNullException.ThrowIfNull(clip);

        if (clip.Height > 0) return clip.Height;
        if (canvasWidth <= 0 || canvasHeight <= 0) return clip.Width;

        var pixelWidth = clip.Width * canvasWidth;

        var pixelHeight = clip is { NativeWidth: > 0, NativeHeight: > 0 }
            ? pixelWidth * clip.NativeHeight.Value / clip.NativeWidth.Value
            : pixelWidth;

        return pixelHeight / canvasHeight;
    }
}
