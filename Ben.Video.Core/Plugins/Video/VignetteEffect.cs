using Ben.Video.Editor.Effects;

namespace Ben.Video.Editor.Plugins.Video;

/// <summary>
/// Adds a vignette (darkened corners) to the clip.
/// </summary>
public sealed class VignetteEffect : IClipEffect
{
    public string EffectId    => "vignette";
    public string DisplayName => "Vignette";

    public IReadOnlyList<ClipEffectParameter> ParameterSchema =>
    [
        new() { Key = "angle",  Label = "Strength",  Min = 0.1, Max = 1.57, Step = 0.05, LargeStep = 0.25, DefaultValue = 0.5 },
    ];

    public AppliedEffect CreateDefault() => new()
    {
        EffectId   = EffectId,
        Parameters = new() { ["angle"] = 0.5 },
    };

    public string BuildFilterFragment(
        IReadOnlyDictionary<string, double> p, double clipDuration, double speed = 1.0)
    {
        var ic  = System.Globalization.CultureInfo.InvariantCulture;
        var ang = Math.Clamp(p.GetValueOrDefault("angle", 0.5), 0.1, 1.57);
        return "vignette=angle=" + ang.ToString("F3", ic);
    }
}
