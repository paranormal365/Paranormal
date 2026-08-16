namespace Ben.Video.Editor.Models;

/// <summary>
/// Per-clip visual effect settings applied during export via ffmpeg filters.
/// Defaults represent a neutral (no-op) state — no filter is emitted when all
/// values are at their neutral value.
/// </summary>
public sealed record ClipEffects
{
    // ── Color grading (ffmpeg eq filter) ──────────────────────────────────────

    /// <summary>
    /// Brightness adjustment. Range: -1.0 (black) to 1.0 (white). Neutral: 0.0.
    /// Maps to ffmpeg <c>eq=brightness={value}</c>.
    /// </summary>
    public double Brightness { get; set; } = 0.0;

    /// <summary>
    /// Contrast multiplier. Range: 0.0 (flat) to 2.0 (high contrast). Neutral: 1.0.
    /// Maps to ffmpeg <c>eq=contrast={value}</c>.
    /// </summary>
    public double Contrast { get; set; } = 1.0;

    /// <summary>
    /// Saturation multiplier. Range: 0.0 (greyscale) to 3.0 (vivid). Neutral: 1.0.
    /// Maps to ffmpeg <c>eq=saturation={value}</c>.
    /// </summary>
    public double Saturation { get; set; } = 1.0;

    // ── Fade in / fade out (ffmpeg fade filter) ────────────────────────────────

    /// <summary>
    /// Duration in seconds of the video fade-in from black at the start of the clip.
    /// 0 = no fade in.
    /// </summary>
    public double FadeInSeconds { get; set; } = 0.0;

    /// <summary>
    /// Duration in seconds of the video fade-out to black at the end of the clip.
    /// 0 = no fade out.
    /// </summary>
    public double FadeOutSeconds { get; set; } = 0.0;

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns true when all properties are at their neutral (no-op) values,
    /// meaning no ffmpeg filter needs to be emitted.
    /// </summary>
    public bool IsNeutral =>
        Math.Abs(Brightness)        < 1e-6 &&
        Math.Abs(Contrast  - 1.0)   < 1e-6 &&
        Math.Abs(Saturation - 1.0)  < 1e-6 &&
        FadeInSeconds  <= 0 &&
        FadeOutSeconds <= 0;
}
