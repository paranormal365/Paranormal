namespace Ben.Video.Editor.Models;

/// <summary>
/// Shared clamp math for a transition's editable duration range — extracted from
/// <c>TransitionEditor.razor</c>'s own inline formula (item #57 T4) so the timeline's new
/// edge-drag resize can reuse the exact same rule instead of drifting from a second copy.
/// </summary>
public static class TransitionDurationClamp
{
    public const double MinDurationSeconds = 0.3;

    /// <summary>The longest duration allowed given the two adjacent clips' own lengths — 90% of
    /// the shorter one, so a transition never consumes an entire clip.</summary>
    public static double MaxDurationSeconds(double fromDurationSeconds, double toDurationSeconds) =>
        Math.Max(MinDurationSeconds, Math.Min(fromDurationSeconds, toDurationSeconds) * 0.9);

    public static double Clamp(double requestedDurationSeconds, double fromDurationSeconds, double toDurationSeconds) =>
        Math.Clamp(requestedDurationSeconds, MinDurationSeconds, MaxDurationSeconds(fromDurationSeconds, toDurationSeconds));
}
