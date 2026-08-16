namespace Ben.Video.Editor.Models;

/// <summary>An axis-aligned hit-test region in canvas-fraction (0..1) coordinates.</summary>
public readonly record struct CanvasHitRect(double X, double Y, double Width, double Height);

/// <summary>
/// Pure point-in-rect hit-testing for the preview canvas's click-to-select overlay. Rotation is
/// deliberately ignored, matching every existing control-point overlay's own axis-aligned
/// bounding box (<c>ClipArtControlPointOverlay</c>'s resize handles don't rotate with
/// <c>ClipArtClip.Rotation</c> either) — not a new inconsistency introduced here.
/// </summary>
public static class CanvasHitTester
{
    /// <summary>Returns the index of the first rect in <paramref name="rects"/> containing
    /// (<paramref name="pointX"/>, <paramref name="pointY"/>), or null if none match. Callers
    /// must order <paramref name="rects"/> topmost-first (e.g. by descending LayerIndex) so the
    /// visually front-most item wins when overlapping items' boxes both contain the point.</summary>
    public static int? HitTest(IReadOnlyList<CanvasHitRect> rects, double pointX, double pointY)
    {
        for (var i = 0; i < rects.Count; i++)
        {
            var r = rects[i];
            if (pointX >= r.X && pointX <= r.X + r.Width &&
                pointY >= r.Y && pointY <= r.Y + r.Height)
                return i;
        }
        return null;
    }
}
