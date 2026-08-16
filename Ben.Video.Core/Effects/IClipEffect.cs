namespace Ben.Video.Editor.Effects;

/// <summary>
/// Contract for a pluggable clip effect. Implement this interface and register
/// the instance with <see cref="Ben.Video.Editor.Services.ClipEffectRegistry"/> to make
/// the effect available in the editor without modifying any core files.
/// </summary>
public interface IClipEffect
{
    /// <summary>
    /// Stable unique identifier used for serialisation and registry lookup.
    /// Must not change between versions once a project file has been saved.
    /// Example: <c>"color_grading"</c>, <c>"fade_in"</c>.
    /// </summary>
    string EffectId { get; }

    /// <summary>Human-readable name shown in the effects dropdown.</summary>
    string DisplayName { get; }

    /// <summary>
    /// Ordered list of configurable parameters. The UI renders one control per entry.
    /// </summary>
    IReadOnlyList<ClipEffectParameter> ParameterSchema { get; }

    /// <summary>
    /// Returns a new <see cref="AppliedEffect"/> pre-populated with default parameter values.
    /// </summary>
    AppliedEffect CreateDefault();

    /// <summary>
    /// Builds the ffmpeg video filter fragment for this effect given the current
    /// <paramref name="parameters"/> and clip timing context.
    /// Returns an empty string when the effect produces no filter (neutral state).
    /// </summary>
    /// <param name="parameters">Current values keyed by <see cref="ClipEffectParameter.Key"/>.</param>
    /// <param name="clipDuration">Wall-clock duration of the clip after trim and speed.</param>
    /// <param name="speed">Playback speed multiplier (1.0 = normal).</param>
    string BuildFilterFragment(
        IReadOnlyDictionary<string, double> parameters,
        double clipDuration,
        double speed = 1.0);
}
