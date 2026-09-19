using Ben.Video.Editor.Effects;

namespace Ben.Video.Editor.Plugins.Video;

/// <summary>
/// Applies a Gaussian blur to the clip. Can be used as a constant blur or
/// to create a "focus pull" by combining with other effects.
/// </summary>
public sealed class BlurEffect : IClipEffect
{
    public string EffectId    => "blur";
    public string DisplayName => "Blur";

    public IReadOnlyList<ClipEffectParameter> ParameterSchema =>
    [
        new() { Key = "sigma", Label = "Strength", Min = 0.1, Max = 30.0, Step = 0.5, LargeStep = 5.0, DefaultValue = 5.0 },
    ];

    public AppliedEffect CreateDefault() => new()
    {
        EffectId   = EffectId,
        Parameters = new() { ["sigma"] = 5.0 },
    };

    public string BuildFilterFragment(
        IReadOnlyDictionary<string, double> p, double clipDuration, double speed = 1.0,
        int canvasWidth = 0, int canvasHeight = 0)
    {
        var ic    = System.Globalization.CultureInfo.InvariantCulture;
        var sigma = p.GetValueOrDefault("sigma", 5.0);
        if (sigma <= 0) return string.Empty;
        return "gblur=sigma=" + sigma.ToString("F1", ic);
    }
}
