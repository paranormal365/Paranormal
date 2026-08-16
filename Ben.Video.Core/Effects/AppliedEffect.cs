namespace Ben.Video.Editor.Effects;

/// <summary>
/// Represents a single effect instance applied to a clip, identified by
/// <see cref="EffectId"/> with its current parameter values.
/// Stored on <c>VideoClip.AppliedEffects</c> / <c>ImageClip.AppliedEffects</c>.
/// </summary>
public sealed class AppliedEffect
{
    /// <summary>Matches <see cref="IClipEffect.EffectId"/> in the registry.</summary>
    public required string EffectId { get; set; }

    /// <summary>
    /// Current parameter values keyed by <see cref="ClipEffectParameter.Key"/>.
    /// Populated with defaults when the effect is first added.
    /// </summary>
    public Dictionary<string, double> Parameters { get; set; } = [];

    /// <summary>Deep-clone this instance.</summary>
    public AppliedEffect Clone() => new()
    {
        EffectId   = EffectId,
        Parameters = new Dictionary<string, double>(Parameters),
    };
}
