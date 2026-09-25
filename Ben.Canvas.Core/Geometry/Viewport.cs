using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ben.Canvas.Core.Geometry;

/// <summary>A point in world coordinates.</summary>
public readonly record struct CanvasPoint(double X, double Y);

/// <summary>An axis-aligned rectangle in world coordinates (CSS pixels at zoom 1).</summary>
public readonly record struct WorldRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
    public double CenterX => X + Width / 2;
    public double CenterY => Y + Height / 2;

    public static WorldRect Empty => new(0, 0, 0, 0);

    public bool IsEmpty => Width <= 0 || Height <= 0;

    public bool Contains(double x, double y) => x >= X && x <= Right && y >= Y && y <= Bottom;

    /// <summary>True when the rectangles overlap or touch.</summary>
    public bool Intersects(WorldRect other) =>
        other.X <= Right && other.Right >= X && other.Y <= Bottom && other.Bottom >= Y;

    public WorldRect Union(WorldRect other)
    {
        var left = Math.Min(X, other.X);
        var top = Math.Min(Y, other.Y);
        return new(left, top, Math.Max(Right, other.Right) - left, Math.Max(Bottom, other.Bottom) - top);
    }

    public WorldRect Inflate(double d) => new(X - d, Y - d, Width + 2 * d, Height + 2 * d);

    /// <summary>The union of many rectangles, or <see cref="Empty"/> when there are none.</summary>
    public static WorldRect Bounds(IEnumerable<WorldRect> rects)
    {
        WorldRect? acc = null;
        foreach (var r in rects) acc = acc is { } a ? a.Union(r) : r;
        return acc ?? Empty;
    }
}

/// <summary>Numbers written into CSS or SVG.</summary>
/// <remarks>
/// Under a French or German culture <c>2.5.ToString()</c> is "2,5", which CSS and SVG read as two numbers
/// or none - the class of bug that broke the video editor's exports for non-English browsers. Every number
/// the canvas writes into a style or a path goes through here.
/// </remarks>
public static class CssNumber
{
    public static string F(double value) =>
        double.IsFinite(value) ? value.ToString("0.###", CultureInfo.InvariantCulture) : "0";
}

/// <summary>Where the board's camera is: pan in screen pixels, zoom as a scale.</summary>
public readonly record struct CanvasViewport(double PanX, double PanY, double Zoom)
{
    public const double MinZoom = 0.1;
    public const double MaxZoom = 4.0;

    public static CanvasViewport Identity => new(0, 0, 1);
}

/// <summary>Converting between the board's screen and its world, zooming and fitting.</summary>
/// <remarks>
/// Pure, and the only place these formulas live: the board's JavaScript uses the same zoom-about-point
/// arithmetic for a pinch so what the fingers do and what C# commits cannot disagree.
/// </remarks>
public static class ViewportMath
{
    public static double ClampZoom(double zoom) =>
        double.IsFinite(zoom) ? Math.Clamp(zoom, CanvasViewport.MinZoom, CanvasViewport.MaxZoom) : 1;

    /// <summary>Clamps zoom only: the board is infinite, so pan has no limit.</summary>
    public static CanvasViewport Clamp(CanvasViewport vp) => vp with { Zoom = ClampZoom(vp.Zoom) };

    public static CanvasPoint WorldToClient(CanvasViewport vp, double wx, double wy) =>
        new(wx * vp.Zoom + vp.PanX, wy * vp.Zoom + vp.PanY);

    /// <summary>A board-relative screen point to world coordinates.</summary>
    public static CanvasPoint ClientToWorld(CanvasViewport vp, double cx, double cy) =>
        new((cx - vp.PanX) / vp.Zoom, (cy - vp.PanY) / vp.Zoom);

    public static CanvasPoint ClientDeltaToWorld(CanvasViewport vp, double dcx, double dcy) =>
        new(dcx / vp.Zoom, dcy / vp.Zoom);

    /// <summary>A screen distance in world units, so a snap or a hit tolerance feels the same at every zoom.</summary>
    public static double ScreenPxToWorld(CanvasViewport vp, double px) => px / vp.Zoom;

    /// <summary>Zooms by <paramref name="factor"/> keeping the world point under (cx, cy) where it is.</summary>
    public static CanvasViewport ZoomAboutPoint(CanvasViewport vp, double cx, double cy, double factor)
    {
        var z2 = ClampZoom(vp.Zoom * factor);
        var ratio = z2 / vp.Zoom;
        return new(cx - (cx - vp.PanX) * ratio, cy - (cy - vp.PanY) * ratio, z2);
    }

    /// <summary>
    /// A two-finger gesture: scale by how far the fingers spread about where they started, then pan by how
    /// far their midpoint moved.
    /// </summary>
    public static CanvasViewport PinchFrom(
        CanvasViewport start, double startDistance, double distance,
        double startFocalX, double startFocalY, double focalX, double focalY)
    {
        if (startDistance <= 0 || !double.IsFinite(distance)) return start;

        var zoomed = ZoomAboutPoint(start, startFocalX, startFocalY, distance / startDistance);
        return zoomed with { PanX = zoomed.PanX + (focalX - startFocalX), PanY = zoomed.PanY + (focalY - startFocalY) };
    }

    /// <summary>A viewport that shows all of <paramref name="bounds"/> with a margin, centred.</summary>
    public static CanvasViewport FitToContent(WorldRect bounds, double viewW, double viewH)
    {
        if (bounds.IsEmpty || viewW <= 0 || viewH <= 0) return CanvasViewport.Identity;

        var margin = Math.Max(40, 0.05 * Math.Min(viewW, viewH));
        var availW = Math.Max(1, viewW - 2 * margin);
        var availH = Math.Max(1, viewH - 2 * margin);
        var zoom = ClampZoom(Math.Min(availW / bounds.Width, availH / bounds.Height));

        return new(viewW / 2 - bounds.CenterX * zoom, viewH / 2 - bounds.CenterY * zoom, zoom);
    }

    /// <summary>The part of the world the board currently shows.</summary>
    public static WorldRect VisibleWorldRect(CanvasViewport vp, double viewW, double viewH)
    {
        var topLeft = ClientToWorld(vp, 0, 0);
        return new(topLeft.X, topLeft.Y, viewW / vp.Zoom, viewH / vp.Zoom);
    }
}

/// <summary>
/// A board's pan and zoom as stored on this device under <c>bc-view-{localId}</c>.
/// </summary>
/// <remarks>
/// Copied from the video editor's layout snapshot pattern: every field nullable, and <see cref="Apply"/>
/// clamps each one, because an older build may have written fewer fields and a hand-edited entry can say
/// anything.
/// </remarks>
public sealed record ViewSnapshot
{
    public const string StorageKeyPrefix = "bc-view-";

    [JsonPropertyName("panX")] public double? PanX { get; init; }
    [JsonPropertyName("panY")] public double? PanY { get; init; }
    [JsonPropertyName("zoom")] public double? Zoom { get; init; }

    public static string Serialise(CanvasViewport vp) =>
        JsonSerializer.Serialize(new ViewSnapshot { PanX = vp.PanX, PanY = vp.PanY, Zoom = vp.Zoom });

    public static ViewSnapshot? Deserialise(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<ViewSnapshot>(json, new JsonSerializerOptions { NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals });
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public CanvasViewport Apply() => new(
        Finite(PanX, 0),
        Finite(PanY, 0),
        Zoom is { } z && double.IsFinite(z) ? ViewportMath.ClampZoom(z) : 1);

    private static double Finite(double? v, double fallback) => v is { } d && double.IsFinite(d) ? d : fallback;
}
