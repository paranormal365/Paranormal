namespace Ben.Video.Editor.Models;

/// <summary>
/// Pure geometry for dragging a bounding-box resize handle on a <see cref="CalloutClip"/>.
/// Kept separate from <c>CalloutControlPointOverlay.razor</c> so the math is unit-testable
/// without a Blazor render context.
/// </summary>
internal static class CalloutResizeMath
{
    /// <summary>
    /// Handle keys recognised by <see cref="ApplyResize"/>, in the same order the overlay renders them.
    /// </summary>
    public static readonly string[] HandleKeys = ["tl", "t", "tr", "r", "br", "b", "bl", "l"];

    /// <summary>
    /// Computes the new bounding box after dragging <paramref name="handle"/> by
    /// (<paramref name="deltaXFraction"/>, <paramref name="deltaYFraction"/>) — both already expressed
    /// as canvas-fraction deltas (pixel delta / canvas dimension), matching <see cref="CalloutClip.X"/>'s
    /// own units. Corner handles move two edges at once; edge handles move one. The edge(s) opposite the
    /// dragged handle stay fixed. Width/Height are clamped to <paramref name="minSize"/> so a drag can't
    /// collapse the box to zero or negative size — collapsing further just holds at the minimum.
    /// </summary>
    public static (double X, double Y, double Width, double Height) ApplyResize(
        double origX, double origY, double origWidth, double origHeight,
        string handle, double deltaXFraction, double deltaYFraction, double minSize = 0.02)
    {
        var left   = origX;
        var top    = origY;
        var right  = origX + origWidth;
        var bottom = origY + origHeight;

        var movesLeft   = handle is "tl" or "l" or "bl";
        var movesRight  = handle is "tr" or "r" or "br";
        var movesTop    = handle is "tl" or "t" or "tr";
        var movesBottom = handle is "bl" or "b" or "br";

        if (movesLeft)   left   = Math.Min(left   + deltaXFraction, right  - minSize);
        if (movesRight)  right  = Math.Max(right  + deltaXFraction, left   + minSize);
        if (movesTop)    top    = Math.Min(top    + deltaYFraction, bottom - minSize);
        if (movesBottom) bottom = Math.Max(bottom + deltaYFraction, top    + minSize);

        return (left, top, right - left, bottom - top);
    }
}
