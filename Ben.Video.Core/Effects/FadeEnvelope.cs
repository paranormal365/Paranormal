namespace Ben.Video.Editor.Effects;

/// <summary>
/// The triangular fade-in/fade-out opacity envelope shared by overlay layer types
/// (<see cref="Models.TextOverlay"/>, <see cref="Models.CalloutClip"/>). Pure math — used by the
/// per-frame SVG export pipeline to compute a frame's opacity multiplier for animated overlays
/// (static overlays express the same envelope as ffmpeg <c>fade=…:alpha=1</c> filters instead,
/// see <c>ExportArgBuilders.BuildStaticOverlayFilter</c>).
/// </summary>
public static class FadeEnvelope
{
    /// <summary>
    /// Opacity multiplier (0–1) at <paramref name="elapsedSeconds"/> into an item's own lifetime:
    /// ramps 0→1 over <paramref name="fadeInSeconds"/>, holds at 1, then ramps 1→0 over the final
    /// <paramref name="fadeOutSeconds"/> of <paramref name="durationSeconds"/>. Clamped to [0,1]
    /// outside the lifetime.
    /// </summary>
    public static double Compute(double elapsedSeconds, double durationSeconds,
        double fadeInSeconds, double fadeOutSeconds)
    {
        if (fadeInSeconds > 0 && elapsedSeconds < fadeInSeconds)
            return Math.Clamp(elapsedSeconds / fadeInSeconds, 0.0, 1.0);

        var remaining = durationSeconds - elapsedSeconds;
        if (fadeOutSeconds > 0 && remaining < fadeOutSeconds)
            return Math.Clamp(remaining / fadeOutSeconds, 0.0, 1.0);

        return 1.0;
    }
}
