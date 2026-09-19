using Ben.Video.Editor.Effects;

namespace Ben.Video.Editor.Plugins.Video;

/// <summary>
/// Brightness, Contrast, and Saturation colour grading via the ffmpeg <c>eq</c> filter.
/// Migrates the original <c>ClipEffects.Brightness/Contrast/Saturation</c> properties.
/// </summary>
public sealed class ColorGradingEffect : IClipEffect
{
    public string EffectId    => "color_grading";
    public string DisplayName => "Color Grading";

    public IReadOnlyList<ClipEffectParameter> ParameterSchema =>
    [
        new() { Key = "brightness", Label = "Brightness", Min = -1.0, Max = 1.0, Step = 0.05, LargeStep = 0.25, DefaultValue = 0.0 },
        new() { Key = "contrast",   Label = "Contrast",   Min =  0.0, Max = 2.0, Step = 0.05, LargeStep = 0.25, DefaultValue = 1.0 },
        new() { Key = "saturation", Label = "Saturation", Min =  0.0, Max = 3.0, Step = 0.05, LargeStep = 0.25, DefaultValue = 1.0 },
    ];

    public AppliedEffect CreateDefault() => new()
    {
        EffectId   = EffectId,
        Parameters = new() { ["brightness"] = 0.0, ["contrast"] = 1.0, ["saturation"] = 1.0 },
    };

    public string BuildFilterFragment(
        IReadOnlyDictionary<string, double> p, double clipDuration, double speed = 1.0,
        int canvasWidth = 0, int canvasHeight = 0)
    {
        var ic  = System.Globalization.CultureInfo.InvariantCulture;
        var b   = p.GetValueOrDefault("brightness", 0.0);
        var c   = p.GetValueOrDefault("contrast",   1.0);
        var sat = p.GetValueOrDefault("saturation", 1.0);

        bool hb = Math.Abs(b)         > 1e-6;
        bool hc = Math.Abs(c - 1.0)   > 1e-6;
        bool hs = Math.Abs(sat - 1.0) > 1e-6;

        if (!hb && !hc && !hs) return string.Empty;

        return "eq=brightness=" + b.ToString("F4", ic)
             + ":contrast="    + c.ToString("F4", ic)
             + ":saturation="  + sat.ToString("F4", ic);
    }
}

