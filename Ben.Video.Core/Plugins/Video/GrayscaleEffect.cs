using Ben.Video.Editor.Effects;

namespace Ben.Video.Editor.Plugins.Video;

/// <summary>
/// Desaturate the clip to greyscale using the ffmpeg <c>hue</c> filter (<c>hue=s=0</c>).
/// The <c>intensity</c> parameter blends between colour (0) and full greyscale (1).
/// </summary>
public sealed class GrayscaleEffect : IClipEffect
{
    public string EffectId    => "grayscale";
    public string DisplayName => "Grayscale";

    public IReadOnlyList<ClipEffectParameter> ParameterSchema =>
    [
        new() { Key = "intensity", Label = "Intensity", Min = 0.0, Max = 1.0, Step = 0.05, LargeStep = 0.25, DefaultValue = 1.0 },
    ];

    public AppliedEffect CreateDefault() => new()
    {
        EffectId   = EffectId,
        Parameters = new() { ["intensity"] = 1.0 },
    };

    public string BuildFilterFragment(
        IReadOnlyDictionary<string, double> p, double clipDuration, double speed = 1.0,
        int canvasWidth = 0, int canvasHeight = 0)
    {
        var ic  = System.Globalization.CultureInfo.InvariantCulture;
        var sat = 1.0 - Math.Clamp(p.GetValueOrDefault("intensity", 1.0), 0.0, 1.0);
        if (Math.Abs(sat - 1.0) < 1e-6) return string.Empty; // no-op
        return "hue=s=" + sat.ToString("F4", ic);
    }
}

