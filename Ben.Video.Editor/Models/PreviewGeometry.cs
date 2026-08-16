namespace Ben.Video.Editor.Models;

/// <summary>
/// Pure geometry for mapping preview-screen pointer coordinates onto the composition canvas.
/// The preview's &lt;video&gt; renders with <c>object-fit: contain</c> inside its container, so the
/// video's actual displayed content box can be smaller than (and offset within) the container
/// whenever the canvas aspect ratio doesn't exactly match the container's — none of the on-canvas
/// overlays accounted for this before this phase, each assuming the container's box equals the
/// content box. Kept separate from any Blazor component so the math is unit-testable without a
/// render context, matching <see cref="CalloutResizeMath"/>'s existing pattern.
/// </summary>
internal static class PreviewGeometry
{
    /// <summary>
    /// Computes the letterboxed/pillarboxed content box's offset and size within a container of size
    /// (<paramref name="containerWidth"/>, <paramref name="containerHeight"/>), for a canvas whose
    /// aspect ratio is <paramref name="canvasWidth"/>:<paramref name="canvasHeight"/> — the same
    /// centering <c>object-fit: contain</c> performs. Falls back to filling the container edge-to-edge
    /// (zero offset) if any dimension is non-positive.
    /// </summary>
    public static (double OffsetX, double OffsetY, double Width, double Height) ComputeContentBox(
        double containerWidth, double containerHeight, double canvasWidth, double canvasHeight)
    {
        if (containerWidth <= 0 || containerHeight <= 0 || canvasWidth <= 0 || canvasHeight <= 0)
            return (0, 0, containerWidth, containerHeight);

        var containerAspect = containerWidth / containerHeight;
        var canvasAspect    = canvasWidth / canvasHeight;

        double contentWidth, contentHeight;
        if (canvasAspect > containerAspect)
        {
            // Canvas is relatively wider than the container — letterboxed top/bottom, fills width.
            contentWidth  = containerWidth;
            contentHeight = containerWidth / canvasAspect;
        }
        else
        {
            // Canvas is relatively taller/narrower — pillarboxed left/right, fills height.
            contentHeight = containerHeight;
            contentWidth  = containerHeight * canvasAspect;
        }

        return ((containerWidth - contentWidth) / 2.0, (containerHeight - contentHeight) / 2.0,
                contentWidth, contentHeight);
    }

    /// <summary>
    /// Converts a point expressed in container-local pixels (i.e. already relative to the container's
    /// own top-left corner) into a canvas fraction, clamped to 0..1 on each axis.
    /// </summary>
    public static (double FractionX, double FractionY) ToFraction(
        double containerLocalX, double containerLocalY,
        double contentOffsetX, double contentOffsetY, double contentWidth, double contentHeight)
    {
        if (contentWidth <= 0 || contentHeight <= 0) return (0, 0);

        var fx = (containerLocalX - contentOffsetX) / contentWidth;
        var fy = (containerLocalY - contentOffsetY) / contentHeight;
        return (Math.Clamp(fx, 0.0, 1.0), Math.Clamp(fy, 0.0, 1.0));
    }

    /// <summary>
    /// Converts a pixel delta (e.g. between two pointermove events) into a canvas-fraction delta —
    /// unlike <see cref="ToFraction"/>, this is a pure scale (no offset, no clamping), since a delta
    /// isn't a position.
    /// </summary>
    public static (double FractionDeltaX, double FractionDeltaY) DeltaToFraction(
        double deltaX, double deltaY, double contentWidth, double contentHeight)
    {
        if (contentWidth <= 0 || contentHeight <= 0) return (0, 0);
        return (deltaX / contentWidth, deltaY / contentHeight);
    }
}
