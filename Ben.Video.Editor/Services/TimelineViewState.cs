using Ben.Video.Editor.Models;
using Microsoft.Extensions.Options;

namespace Ben.Video.Editor.Services;

/// <summary>
/// Scoped UI state for the timeline panel: zoom level, scroll offset, and
/// display-mode toggle.  Consumed by <c>VideoTimeline.razor</c>.
/// </summary>
public sealed class TimelineViewState
{
    // ── Constants ────────────────────────────────────────────────────────────

    /// <summary>Base pixels rendered per second of timeline at zoom = 1×.</summary>
    public const double BasePxPerSecond = 80.0;

    /// <summary>Assumed frames-per-second used when <see cref="DisplayMode"/> is <see cref="TimelineDisplayMode.Frames"/>.</summary>
    public const double DefaultFps = 30.0;

    // ── State ─────────────────────────────────────────────────────────────────

    /// <summary>Current zoom multiplier. Range is [0.05, 20] to support very long clips.</summary>
    public double ZoomScale { get; set; } = 1.0;

    /// <summary>Current ruler/track label display mode.</summary>
    public TimelineDisplayMode DisplayMode { get; set; } = TimelineDisplayMode.Time;

    // ── Derived helpers ───────────────────────────────────────────────────────

    /// <summary>Effective pixels-per-second at the current zoom.</summary>
    public double PxPerSecond => BasePxPerSecond * ZoomScale;

    /// <summary>Pixel width of the full timeline canvas for a given total duration.</summary>
    public double CanvasWidth(double totalDuration) => PxPerSecond * totalDuration;

    /// <summary>Resets zoom to 1×.</summary>
    public void ResetZoom() => ZoomScale = 1.0;

    /// <summary>
    /// Adjusts <see cref="ZoomScale"/> so that <paramref name="totalDuration"/> seconds
    /// fits within <paramref name="visibleWidthPx"/> pixels with a small margin.
    /// Clamps to [0.05, 20].
    /// </summary>
    public void FitToView(double totalDuration, double visibleWidthPx)
    {
        if (totalDuration <= 0 || visibleWidthPx <= 0) return;
        var neededPxPerSec = (visibleWidthPx * 0.95) / totalDuration; // 5% margin
        ZoomScale = Math.Clamp(neededPxPerSec / BasePxPerSecond, 0.05, 20.0);
    }

    // ── Tick computation ──────────────────────────────────────────────────────

    /// <summary>
    /// Computes evenly-spaced major tick positions (in seconds) for the ruler.
    /// The interval is chosen so that ticks are at least <paramref name="minSpacingPx"/> apart.
    /// </summary>
    public IReadOnlyList<double> ComputeTicks(double totalDuration, double minSpacingPx = 60.0)
    {
        if (totalDuration <= 0 || PxPerSecond <= 0)
            return [];

        // Candidate intervals in seconds: 0.1 s, 0.25, 0.5, 1, 2, 5, 10, 15, 30, 60, 120, 300, 600 …
        var candidates = new[]
        {
            0.1, 0.25, 0.5,
            1, 2, 5, 10, 15, 30,
            60, 120, 300, 600, 1800, 3600
        };

        var intervalSec = candidates.FirstOrDefault(
            c => c * PxPerSecond >= minSpacingPx,
            candidates[^1]);

        var ticks = new List<double>();
        for (var t = 0.0; t <= totalDuration + intervalSec * 0.01; t += intervalSec)
            ticks.Add(t);

        return ticks;
    }

    // ── Label formatting ──────────────────────────────────────────────────────

    /// <summary>Formats a tick position according to the current <see cref="DisplayMode"/>.</summary>
    public string FormatTick(double seconds) => DisplayMode switch
    {
        TimelineDisplayMode.Frames => ((int)Math.Round(seconds * DefaultFps)).ToString(),
        _ => FormatTimecode(seconds),
    };

    private static string FormatTimecode(double seconds)
    {
        var ts = TimeSpan.FromSeconds(seconds);
        return ts.TotalHours >= 1
            ? ts.ToString(@"h\:mm\:ss")
            : ts.TotalMinutes >= 1
                ? ts.ToString(@"m\:ss")
                : ts.ToString(@"s\.f") + "s";
    }
}
